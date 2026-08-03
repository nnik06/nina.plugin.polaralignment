using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaObservedDeterminationCoordinatorTest {
        private static readonly Guid CampaignId =
            Guid.Parse("50000000-0000-4000-8000-000000000001");
        private static readonly string Nonce = new('a', 64);
        private static readonly string Attestation = new('b', 64);

        [Test]
        public async Task ValidDraftIsValidatedHashedAndSealed() {
            var client = new RecordingClient();
            var coordinator = new TppaObservedDeterminationCoordinator(
                client, TimeSpan.FromSeconds(75));

            var result = await coordinator.AcquireAsync(
                CampaignId,
                (_, _) => Task.FromResult(Draft()),
                CancellationToken.None);

            client.OpenCalls.Should().Be(1);
            client.CloseCalls.Should().Be(1);
            client.AbortCalls.Should().Be(0);
            client.CaptureDigest.Should().MatchRegex("^[0-9a-f]{64}$");
            result.ObservationLeaseNonce.Should().Be(Nonce);
            result.ObservationAttestationSha256.Should().Be(Attestation);
        }

        [Test]
        public async Task InvalidDraftIsAbortedBeforeItCanBeSealed() {
            var client = new RecordingClient();
            var coordinator = new TppaObservedDeterminationCoordinator(
                client, TimeSpan.FromSeconds(75));

            var action = () => coordinator.AcquireAsync(
                CampaignId,
                (_, _) => Task.FromResult(Draft() with {
                    TruePoleRefractionEnabled = false
                }),
                CancellationToken.None);

            await action.Should().ThrowAsync<ArgumentException>()
                .WithMessage("*true-pole*");
            client.CloseCalls.Should().Be(0);
            client.AbortCalls.Should().Be(1);
            client.AbortTokenCanBeCanceled.Should().BeFalse();
        }

        [Test]
        public async Task PreCanceledCallerDoesNotOpenObservation() {
            var client = new RecordingClient();
            var coordinator = new TppaObservedDeterminationCoordinator(
                client, TimeSpan.FromSeconds(75));

            var action = () => coordinator.AcquireAsync(
                CampaignId,
                (_, _) => throw new InvalidOperationException("must not run"),
                new CancellationToken(canceled: true));

            await action.Should().ThrowAsync<OperationCanceledException>();
            client.OpenCalls.Should().Be(0);
            client.AbortCalls.Should().Be(0);
        }

        [Test]
        public async Task FailureAfterOpenAbortsEvenWhenCallerTokenBecomesCanceled() {
            var client = new RecordingClient();
            var coordinator = new TppaObservedDeterminationCoordinator(
                client, TimeSpan.FromSeconds(75));
            using var cts = new CancellationTokenSource();

            var action = () => coordinator.AcquireAsync(
                CampaignId,
                (_, _) => {
                    cts.Cancel();
                    throw new OperationCanceledException(cts.Token);
                },
                cts.Token);

            await action.Should().ThrowAsync<OperationCanceledException>();
            client.AbortCalls.Should().Be(1);
            client.AbortTokenCanBeCanceled.Should().BeFalse();
        }

        [Test]
        public async Task AbortFailurePreservesBothFailures() {
            var client = new RecordingClient { FailAbort = true };
            var coordinator = new TppaObservedDeterminationCoordinator(
                client, TimeSpan.FromSeconds(75));

            var action = () => coordinator.AcquireAsync(
                CampaignId,
                (_, _) => throw new InvalidOperationException("solve failed"),
                CancellationToken.None);

            var exception = await action.Should().ThrowAsync<AggregateException>();
            exception.Which.InnerExceptions.Should().HaveCount(2);
            exception.Which.InnerExceptions[0].Message.Should().Be("solve failed");
            exception.Which.InnerExceptions[1].Message.Should().Be("abort failed");
        }

        private static TppaCoarseDeterminationDraft Draft() {
            var completed = DateTime.UtcNow.AddSeconds(-1);
            var started = completed.AddSeconds(-8);
            var solves = Enumerable.Range(0, 3).Select(index => {
                var exposureStart = started.AddSeconds(1 + index * 2);
                return new TppaCoarseSourceSolveEvidence(
                    Guid.NewGuid(),
                    new string((char)('c' + index), 64),
                    new string((char)('6' + index), 64),
                    exposureStart,
                    1000,
                    exposureStart.AddMilliseconds(500),
                    exposureStart.AddMilliseconds(-100),
                    true,
                    false,
                    10.0 + index,
                    20.0,
                    "pierEast");
            }).ToArray();
            return new TppaCoarseDeterminationDraft(
                Guid.NewGuid(),
                started,
                completed,
                "safe-arc-a",
                true,
                new string('1', 40),
                new string('2', 64),
                "hae29c-ec-full-rig-v1",
                new string('3', 64),
                "astap-test",
                "gaia-dr3",
                solves,
                12.0,
                -8.0,
                0.04,
                0.0,
                0.04);
        }

        private sealed class RecordingClient : IUpasSupervisorTppaObservationClient {
            public int OpenCalls { get; private set; }
            public int CloseCalls { get; private set; }
            public int AbortCalls { get; private set; }
            public bool AbortTokenCanBeCanceled { get; private set; }
            public string CaptureDigest { get; private set; } = string.Empty;
            public bool FailAbort { get; init; }

            public Task<UpasSupervisorTppaObservationLease> OpenAsync(
                    Guid campaignId, TimeSpan duration, CancellationToken token) {
                token.ThrowIfCancellationRequested();
                OpenCalls++;
                return Task.FromResult(new UpasSupervisorTppaObservationLease(
                    "60000000-0000-4000-8000-000000000001",
                    Nonce,
                    campaignId,
                    "70000000-0000-4000-8000-000000000001",
                    4,
                    7,
                    1000,
                    76000));
            }

            public Task<UpasSupervisorTppaObservationAttestation> CloseAsync(
                    UpasSupervisorTppaObservationLease lease,
                    string captureDigestSha256,
                    CancellationToken token) {
                CloseCalls++;
                CaptureDigest = captureDigestSha256;
                return Task.FromResult(new UpasSupervisorTppaObservationAttestation(
                    lease.LeaseId,
                    lease.Nonce,
                    lease.CampaignId,
                    lease.BootId,
                    lease.MotionEpoch,
                    lease.MotionCounter,
                    lease.OpenedMonotonicNs,
                    2000,
                    captureDigestSha256,
                    Attestation));
            }

            public Task AbortAsync(
                    UpasSupervisorTppaObservationLease lease,
                    CancellationToken token) {
                AbortCalls++;
                AbortTokenCanBeCanceled = token.CanBeCanceled;
                if (FailAbort) {
                    throw new InvalidOperationException("abort failed");
                }
                return Task.CompletedTask;
            }
        }
    }
}