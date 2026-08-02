using NINA.Core.Utility;
using System;
using System.Globalization;
using System.IO.Ports;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugins.PolarAlignment {
    public abstract partial class UniversalPolarAlignmentBase : IPolarAlignmentSystem {
        private readonly SerialPort port;

        protected abstract string SystemName { get; }
        protected virtual string NewLineSequence => "\n";
        protected virtual int ScanReadTimeout => 1000;
        protected virtual int ScanWriteTimeout => 1000;
        protected virtual bool ClearBufferOnConnect => false;

        protected abstract Regex GetStatusRegex();

        protected SerialPort Port => port;

        private const float TargetPositionTolerance = 0.01f;
        private const double MovementTimeoutFactor = 3d;
        private static readonly TimeSpan MinimumMovementTimeout = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan MovementTimeoutGracePeriod = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan StoppedStatusGracePeriod = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan FallbackMovementTimeout = TimeSpan.FromSeconds(30);
        private const int MaxStatusReadAttempts = 12;
        private const int MaxCommandAcknowledgementReadAttempts = 8;
        private const float StoppedPositionToleranceMultiplier = 5f;
        private const int MaxStoppedStatusChecks = 4;
        private const int MaxUnchangedPositionChecks = 15;
        internal const byte GrblJogCancelRealtimeCommand = 0x85;
        private const int RequiredStoppedStatusConfirmations = 2;
        private const int MaximumJogCancelStatusAttempts = 10;
        private static readonly TimeSpan JogCancelStatusConfirmationInterval = TimeSpan.FromMilliseconds(300);

        protected UniversalPolarAlignmentBase() {
            var comPorts = SerialPort.GetPortNames();
            foreach (var comPort in comPorts) {
                var serialPortToTest = new SerialPort() {
                    PortName = comPort,
                    BaudRate = 115200,
                    Parity = Parity.None,
                    DataBits = 8,
                    StopBits = StopBits.One,
                    NewLine = NewLineSequence
                };

                serialPortToTest.ReadTimeout = ScanReadTimeout;
                serialPortToTest.WriteTimeout = ScanWriteTimeout;

                try {
                    serialPortToTest.Open();
                    if (serialPortToTest.IsOpen) {
                        if (ClearBufferOnConnect) {
                            Thread.Sleep(100);
                            serialPortToTest.DiscardInBuffer();
                        }

                        FlushControllerInputLine(serialPortToTest, NewLineSequence);
                        serialPortToTest.WriteLine("?");
                        var status = ReadStatusLine(serialPortToTest);
                        var match = GetStatusRegex().Match(status);
                        if (match.Success) {
                            port = serialPortToTest;
                            Logger.Info($"Found {SystemName} on {comPort}");
                            break;
                        } else {
                            serialPortToTest.Close();
                            serialPortToTest.Dispose();
                            continue;
                        }
                    }
                } catch {
                    serialPortToTest?.Close();
                    serialPortToTest?.Dispose();
                }
            }
            if (port == null) {
                throw new Exception($"Unable to find {SystemName}");
            }
            UpdateStatus();
        }

        public bool Connected => port.IsOpen;
        public string Status { get; private set; }

        private float XPosition { get; set; }
        private float YPosition { get; set; }
        private float ZPosition { get; set; }

        public LastDirection XLastDirection { get; private set; } = LastDirection.Positive;
        public LastDirection YLastDirection { get; private set; } = LastDirection.Positive;
        public LastDirection ZLastDirection { get; private set; } = LastDirection.Positive;

        public float XPosition1 { get => XPosition / XGearRatio; }
        public float YPosition1 { get => YPosition / YGearRatio; }
        public float ZPosition1 { get => ZPosition / ZGearRatio; }

        public abstract float XGearRatio { get; set; }
        public abstract float YGearRatio { get; set; }
        public float ZGearRatio { get; set; } = 1;

        private SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);

        public async Task MoveRelative(Axis axis, int speed, float position, CancellationToken token) {
            await semaphore.WaitAsync(token);
            var jogCommandSent = false;
            try {
                UpdateStatus();
                var axisCommand = axis switch {
                    Axis.XAxis => "X",
                    Axis.YAxis => "Y",
                    Axis.ZAxis => "Z",
                    _ => throw new ArgumentException("Invalid Axis"),
                };
                var gearRatio = axis switch {
                    Axis.XAxis => XGearRatio,
                    Axis.YAxis => YGearRatio,
                    Axis.ZAxis => ZGearRatio,
                    _ => throw new ArgumentException("Invalid Axis"),
                };

                Func<float> checkProperty = axis switch {
                    Axis.XAxis => () => XPosition,
                    Axis.YAxis => () => YPosition,
                    Axis.ZAxis => () => ZPosition,
                    _ => throw new ArgumentException("Invalid Axis"),
                };

                var target = checkProperty() + position * gearRatio;

                switch (axis) {
                    case Axis.XAxis: XLastDirection = position >= 0 ? LastDirection.Positive : LastDirection.Negative; break;
                    case Axis.YAxis: YLastDirection = position >= 0 ? LastDirection.Positive : LastDirection.Negative; break;
                    case Axis.ZAxis: ZLastDirection = position >= 0 ? LastDirection.Positive : LastDirection.Negative; break;
                }

                var command = $"$J=G91G21{axisCommand}{(position * gearRatio).ToString(CultureInfo.InvariantCulture)}F{speed.ToString(CultureInfo.InvariantCulture)}";
                Logger.Info($"Sending command: {command}");
                FlushControllerInputLine(port, NewLineSequence);
                port.WriteLine(command);
                jogCommandSent = true;
                var ok = ReadCommandAcknowledgement(port);
                Logger.Info($"Response: {ok}");

                var startPos = checkProperty();
                var timeout = CalculateMovementTimeout(startPos, target, speed);
                var startTime = DateTime.Now;
                var lastPos = startPos;
                var observedMotion = false;
                var stoppedStatusCount = 0;
                var stuckCount = 0;

                while (Math.Abs(checkProperty() - target) > TargetPositionTolerance) {
                    UpdateStatus();
                    var currentPos = checkProperty();
                    var remainingDistance = Math.Abs(currentPos - target);

                    if (remainingDistance <= TargetPositionTolerance) {
                        break;
                    }

                    if (Math.Abs(currentPos - startPos) > TargetPositionTolerance) {
                        observedMotion = true;
                    }

                    if (IsControllerStoppedStatus(Status)) {
                        if (remainingDistance <= TargetPositionTolerance * StoppedPositionToleranceMultiplier) {
                            Logger.Info($"Movement stopped within relaxed target tolerance. Current: {currentPos}, Target: {target}, Status: {Status}");
                            break;
                        }

                        if (!observedMotion && DateTime.Now - startTime <= StoppedStatusGracePeriod) {
                            Logger.Info($"Ignoring early stopped status before motion starts. Current: {currentPos}, Target: {target}, Status: {Status}");
                            await Task.Delay(300, token);
                            continue;
                        }

                        stoppedStatusCount++;
                        if (stoppedStatusCount < MaxStoppedStatusChecks) {
                            Logger.Info($"Movement reported stopped before target; retrying status check. Current: {currentPos}, Target: {target}, Status: {Status}");
                            await Task.Delay(300, token);
                            continue;
                        }

                        throw new TimeoutException($"Movement stopped before reaching target. Current: {currentPos}, Target: {target}, Status: {Status}");
                    } else {
                        stoppedStatusCount = 0;
                    }

                    if (Math.Abs(currentPos - lastPos) < TargetPositionTolerance) {
                        stuckCount++;
                        if (stuckCount > MaxUnchangedPositionChecks && DateTime.Now - startTime > MovementTimeoutGracePeriod) {
                            throw new TimeoutException($"Motor position did not change while moving. Current: {currentPos}, Target: {target}, Status: {Status}");
                        }
                    } else {
                        stuckCount = 0;
                    }
                    lastPos = currentPos;

                    if (DateTime.Now - startTime > timeout) {
                        throw new TimeoutException($"Movement timeout after {timeout.TotalSeconds:N1}s. Current: {currentPos}, Target: {target}");
                    }

                    await Task.Delay(300, token);
                }
            } catch (Exception ex) when (jogCommandSent) {
                CancelActiveJogAndConfirmStopped(ex);
                throw;
            } finally {
                semaphore.Release();
            }
        }

        public async Task MoveAbsolute(Axis axis, int speed, float position, CancellationToken token) {
            await semaphore.WaitAsync(token);
            var jogCommandSent = false;
            try {
                UpdateStatus();
                var axisCommand = axis switch {
                    Axis.XAxis => "X",
                    Axis.YAxis => "Y",
                    Axis.ZAxis => "Z",
                    _ => throw new ArgumentException("Invalid Axis"),
                };
                var gearRatio = axis switch {
                    Axis.XAxis => XGearRatio,
                    Axis.YAxis => YGearRatio,
                    Axis.ZAxis => ZGearRatio,
                    _ => throw new ArgumentException("Invalid Axis"),
                };

                var target = position * gearRatio;

                switch (axis) {
                    case Axis.XAxis: XLastDirection = position - XPosition1 >= 0 ? LastDirection.Positive : LastDirection.Negative; break;
                    case Axis.YAxis: YLastDirection = position - YPosition1 >= 0 ? LastDirection.Positive : LastDirection.Negative; break;
                    case Axis.ZAxis: ZLastDirection = position - ZPosition1 >= 0 ? LastDirection.Positive : LastDirection.Negative; break;
                }

                var command = $"$J=G53{axisCommand}{target.ToString(CultureInfo.InvariantCulture)}F{speed.ToString(CultureInfo.InvariantCulture)}";
                Logger.Info($"Sending command: {command}");
                FlushControllerInputLine(port, NewLineSequence);
                port.WriteLine(command);
                jogCommandSent = true;
                var ok = ReadCommandAcknowledgement(port);
                Logger.Info($"Response: {ok}");

                Func<float> checkProperty = axis switch {
                    Axis.XAxis => () => XPosition,
                    Axis.YAxis => () => YPosition,
                    Axis.ZAxis => () => ZPosition,
                    _ => throw new ArgumentException("Invalid Axis"),
                };

                var startPos = checkProperty();
                var timeout = CalculateMovementTimeout(startPos, target, speed);
                var startTime = DateTime.Now;
                var lastPos = startPos;
                var observedMotion = false;
                var stoppedStatusCount = 0;
                var stuckCount = 0;

                while (Math.Abs(checkProperty() - target) > TargetPositionTolerance) {
                    UpdateStatus();
                    var currentPos = checkProperty();
                    var remainingDistance = Math.Abs(currentPos - target);

                    if (remainingDistance <= TargetPositionTolerance) {
                        break;
                    }

                    if (Math.Abs(currentPos - startPos) > TargetPositionTolerance) {
                        observedMotion = true;
                    }

                    if (IsControllerStoppedStatus(Status)) {
                        if (remainingDistance <= TargetPositionTolerance * StoppedPositionToleranceMultiplier) {
                            Logger.Info($"Movement stopped within relaxed target tolerance. Current: {currentPos}, Target: {target}, Status: {Status}");
                            break;
                        }

                        if (!observedMotion && DateTime.Now - startTime <= StoppedStatusGracePeriod) {
                            Logger.Info($"Ignoring early stopped status before motion starts. Current: {currentPos}, Target: {target}, Status: {Status}");
                            await Task.Delay(300, token);
                            continue;
                        }

                        stoppedStatusCount++;
                        if (stoppedStatusCount < MaxStoppedStatusChecks) {
                            Logger.Info($"Movement reported stopped before target; retrying status check. Current: {currentPos}, Target: {target}, Status: {Status}");
                            await Task.Delay(300, token);
                            continue;
                        }

                        throw new TimeoutException($"Movement stopped before reaching target. Current: {currentPos}, Target: {target}, Status: {Status}");
                    } else {
                        stoppedStatusCount = 0;
                    }

                    if (Math.Abs(currentPos - lastPos) < TargetPositionTolerance) {
                        stuckCount++;
                        if (stuckCount > MaxUnchangedPositionChecks && DateTime.Now - startTime > MovementTimeoutGracePeriod) {
                            throw new TimeoutException($"Motor position did not change while moving. Current: {currentPos}, Target: {target}, Status: {Status}");
                        }
                    } else {
                        stuckCount = 0;
                    }
                    lastPos = currentPos;

                    if (DateTime.Now - startTime > timeout) {
                        throw new TimeoutException($"Movement timeout after {timeout.TotalSeconds:N1}s. Current: {currentPos}, Target: {target}");
                    }

                    await Task.Delay(300, token);
                }
            } catch (Exception ex) when (jogCommandSent) {
                CancelActiveJogAndConfirmStopped(ex);
                throw;
            } finally {
                semaphore.Release();
            }
        }

        private void CancelActiveJogAndConfirmStopped(Exception movementFailure) {
            Logger.Warning(
                $"Cancelling active {SystemName} GRBL jog after movement failure: {movementFailure.Message}");
            try {
                var result = JogCancellationOrchestrator.Execute(
                    command => port.Write(new[] { command }, 0, 1),
                    () => {
                        UpdateStatus();
                        return new JogCancellationStatusSample(
                            Status,
                            XPosition,
                            YPosition,
                            ZPosition);
                    },
                    Thread.Sleep,
                    RequiredStoppedStatusConfirmations,
                    MaximumJogCancelStatusAttempts,
                    JogCancelStatusConfirmationInterval);
                Logger.Warning(
                    $"Confirmed {SystemName} GRBL jog stopped after failure; " +
                    $"status={result.FinalSample.Status}; " +
                    $"confirmations={result.Confirmations}; attempts={result.Attempts}; " +
                    $"position=({result.FinalSample.X:F3},{result.FinalSample.Y:F3},{result.FinalSample.Z:F3}).");
            } catch (Exception abortFailure) {
                Logger.Error(abortFailure);
                throw new AggregateException(
                    $"{SystemName} movement failed and GRBL jog cancellation could not be verified.",
                    movementFailure,
                    abortFailure);
            }
        }
        internal static TimeSpan CalculateMovementTimeout(float startPosition, float targetPosition, int speed) {
            var distance = Math.Abs(targetPosition - startPosition);
            if (distance <= TargetPositionTolerance) {
                return MinimumMovementTimeout;
            }
            if (speed <= 0) {
                return FallbackMovementTimeout;
            }

            var expectedSeconds = distance / speed * 60d;
            var timeout = TimeSpan.FromSeconds(expectedSeconds * MovementTimeoutFactor) + MovementTimeoutGracePeriod;
            return timeout > MinimumMovementTimeout ? timeout : MinimumMovementTimeout;
        }

        private void UpdateStatus() {
            port.WriteLine("?");
            var status = ReadStatusLine(port);

            if (TryParseStatus(GetStatusRegex(), status, out var controllerStatus, out var xPosition, out var yPosition, out var zPosition)) {
                Status = controllerStatus;
                XPosition = xPosition;
                YPosition = yPosition;
                ZPosition = zPosition;
                return;
            }

            Logger.Error($"Failed to parse {SystemName} status: {status}");
            throw new InvalidOperationException($"Unable to parse {SystemName} status response: {status}");
        }

        internal static bool TryParseStatus(Regex statusRegex,
                                            string status,
                                            out string controllerStatus,
                                            out float xPosition,
                                            out float yPosition,
                                            out float zPosition) {
            controllerStatus = string.Empty;
            xPosition = 0;
            yPosition = 0;
            zPosition = 0;

            if (string.IsNullOrWhiteSpace(status)) {
                return false;
            }

            var match = statusRegex.Match(status.Trim());
            if (!match.Success) {
                return false;
            }

            if (!float.TryParse(match.Groups["x"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out xPosition)
                || !float.TryParse(match.Groups["y"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out yPosition)
                || !float.TryParse(match.Groups["z"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out zPosition)) {
                return false;
            }

            controllerStatus = match.Groups["status"].Value;
            return true;
        }

        private static void FlushControllerInputLine(SerialPort serialPort, string newlineSequence) {
            try {
                serialPort.Write(newlineSequence);
                Thread.Sleep(50);
                serialPort.DiscardInBuffer();
            } catch (Exception ex) {
                Logger.Info($"Unable to flush controller input line: {ex.Message}");
            }
        }

        private static string ReadStatusLine(SerialPort serialPort) {
            for (var attempt = 0; attempt < MaxStatusReadAttempts; attempt++) {
                string line;
                try {
                    line = serialPort.ReadLine();
                } catch (TimeoutException ex) {
                    Logger.Debug($"Timed out while reading status response attempt {attempt + 1}/{MaxStatusReadAttempts}: {ex.Message}");
                    continue;
                }

                var response = line?.Trim();
                if (IsStatusLineCandidate(response)) {
                    return response;
                }

                if (IsNmeaSentence(response)) {
                    Logger.Info($"Rejecting serial port because it is streaming NMEA/GPS data while reading controller status: {response}");
                    return string.Empty;
                }

                Logger.Debug($"Ignoring non-status serial response while reading position: {line}");
            }

            throw new TimeoutException($"No status response received after reading {MaxStatusReadAttempts} serial lines.");
        }

        private static string ReadCommandAcknowledgement(SerialPort serialPort) {
            for (var attempt = 0; attempt < MaxCommandAcknowledgementReadAttempts; attempt++) {
                string line;
                try {
                    line = serialPort.ReadLine();
                } catch (TimeoutException ex) {
                    Logger.Debug($"Timed out while reading command acknowledgement attempt {attempt + 1}/{MaxCommandAcknowledgementReadAttempts}: {ex.Message}");
                    continue;
                }

                var response = line?.Trim();
                if (string.IsNullOrEmpty(response)) {
                    continue;
                }

                if (string.Equals(response, "ok", StringComparison.OrdinalIgnoreCase)) {
                    return response;
                }

                if (response.StartsWith("error", StringComparison.OrdinalIgnoreCase)
                    || response.StartsWith("ALARM", StringComparison.OrdinalIgnoreCase)) {
                    throw new InvalidOperationException($"Movement command was rejected by controller: {response}");
                }

                Logger.Debug($"Ignoring non-ack serial response while waiting for movement command acknowledgement: {line}");
            }

            throw new TimeoutException($"No command acknowledgement received after reading {MaxCommandAcknowledgementReadAttempts} serial lines.");
        }

        private static bool IsNmeaSentence(string line) {
            return line?.StartsWith("$GP", StringComparison.OrdinalIgnoreCase) == true
                   || line?.StartsWith("$GN", StringComparison.OrdinalIgnoreCase) == true
                   || line?.StartsWith("$GL", StringComparison.OrdinalIgnoreCase) == true
                   || line?.StartsWith("$GA", StringComparison.OrdinalIgnoreCase) == true
                   || line?.StartsWith("$GB", StringComparison.OrdinalIgnoreCase) == true;
        }

        internal static bool IsStatusLineCandidate(string line) {
            var response = line?.Trim();
            return !string.IsNullOrEmpty(response)
                && response.StartsWith("<", StringComparison.Ordinal)
                && response.Contains('>');
        }

        internal static bool IsControllerStoppedStatus(string status) {
            var normalizedStatus = status?.Trim();
            return string.Equals(normalizedStatus, "Idle", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedStatus, "Hold:0", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedStatus, "Door:0", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedStatus, "Door:1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedStatus, "Alarm", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsJogCancellationTerminalStatus(string status) =>
            string.Equals(status?.Trim(), "Idle", StringComparison.OrdinalIgnoreCase);

        internal static bool AreControllerPositionsStable(
            (float X, float Y, float Z) previous,
            (float X, float Y, float Z) current) =>
            Math.Abs(previous.X - current.X) <= TargetPositionTolerance
            && Math.Abs(previous.Y - current.Y) <= TargetPositionTolerance
            && Math.Abs(previous.Z - current.Z) <= TargetPositionTolerance;

        public async Task RefreshStatus(CancellationToken token) {
            await semaphore.WaitAsync(token);
            try {
                try {
                    UpdateStatus();
                } catch (Exception ex) when (ex is not OperationCanceledException) {
                    Logger.Info($"Ignoring transient {SystemName} status refresh failure: {ex.Message}");
                }
            } finally {
                semaphore.Release();
            }
        }

        public void Dispose() => port?.Dispose();
    }
}
