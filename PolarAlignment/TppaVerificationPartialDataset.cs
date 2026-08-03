using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NINA.Core.Enum;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NINA.Plugins.PolarAlignment {
    internal sealed record TppaVerificationPointReceipt(
        int SchemaVersion,
        Guid RunId,
        int SequenceIndex,
        string Direction,
        int PointIndex,
        int DirectionSampleCount,
        DateTime ObservationUtc,
        double MountAzimuthDegrees,
        double MountAltitudeDegrees,
        double SolvedRightAscensionDegrees,
        double SolvedDeclinationDegrees,
        double PositionAngleDegrees,
        double VectorX,
        double VectorY,
        double VectorZ,
        PierSide SideOfPier = PierSide.pierUnknown) {

        public const int CurrentSchemaVersion = 1;
        public bool GrantsMotionAuthority => false;
        public bool GrantsCompletionAuthority => false;

        public string ToJson() => TppaVerificationReceiptJson.Serialize(this);
    }

    internal sealed record TppaVerificationPartialDatasetReceipt(
        int SchemaVersion,
        Guid RunId,
        bool MeasurementCompleted,
        bool IsComplete,
        int CollectedSampleCount,
        int ExpectedSampleCount,
        IReadOnlyList<TppaVerificationPointReceipt> Samples,
        IReadOnlyList<string> Issues) {

        public const int CurrentSchemaVersion = 1;
        public bool GrantsMotionAuthority => false;
        public bool GrantsCompletionAuthority => false;

        public string ToJson() => TppaVerificationReceiptJson.Serialize(this);
    }

    internal sealed record TppaVerificationRunSummary(
        int SchemaVersion,
        Guid RunId,
        DateTime StartedUtc,
        DateTime CompletedUtc,
        double ElapsedSeconds,
        bool DiagnosticPassed,
        bool RepeatabilityPassed,
        bool ReciprocityPassed,
        int ExpectedSampleCount,
        bool RefractionAdjustmentEnabled,
        bool OverdeterminedShadowModelCheck,
        double EffectivePointSettleSeconds,
        bool ComponentTotalsConsistent,
        double InitialAzimuthMinutes,
        double InitialAltitudeMinutes,
        double InitialTotalMinutes,
        double ReciprocalAzimuthMinutes,
        double ReciprocalAltitudeMinutes,
        double ReciprocalTotalMinutes,
        double RepeatedForwardAzimuthMinutes,
        double RepeatedForwardAltitudeMinutes,
        double RepeatedForwardTotalMinutes,
        double RepeatabilityVectorSeparationMinutes,
        double ReciprocityVectorSeparationMinutes) {

        public const int CurrentSchemaVersion = 2;
        public bool GrantsMotionAuthority => false;
        public bool GrantsAbsoluteAccuracyClaim => false;

        public static TppaVerificationRunSummary Create(
                Guid runId,
                DateTime startedUtc,
                DateTime completedUtc,
                bool repeatabilityPassed,
                bool reciprocityPassed,
                int expectedSampleCount,
                bool refractionAdjustmentEnabled,
                bool overdeterminedShadowModelCheck,
                double effectivePointSettleSeconds,
                double initialAzimuthMinutes,
                double initialAltitudeMinutes,
                double initialTotalMinutes,
                double reciprocalAzimuthMinutes,
                double reciprocalAltitudeMinutes,
                double reciprocalTotalMinutes,
                double repeatedForwardAzimuthMinutes,
                double repeatedForwardAltitudeMinutes,
                double repeatedForwardTotalMinutes,
                double repeatabilityVectorSeparationMinutes,
                double reciprocityVectorSeparationMinutes) {
            var values = new[] {
                effectivePointSettleSeconds,
                initialAzimuthMinutes,
                initialAltitudeMinutes,
                initialTotalMinutes,
                reciprocalAzimuthMinutes,
                reciprocalAltitudeMinutes,
                reciprocalTotalMinutes,
                repeatedForwardAzimuthMinutes,
                repeatedForwardAltitudeMinutes,
                repeatedForwardTotalMinutes,
                repeatabilityVectorSeparationMinutes,
                reciprocityVectorSeparationMinutes
            };
            if (runId == Guid.Empty) {
                throw new ArgumentException("Verification run summary requires a non-empty run id.", nameof(runId));
            }
            if (startedUtc.Kind != DateTimeKind.Utc || completedUtc.Kind != DateTimeKind.Utc) {
                throw new ArgumentException("Verification run summary timestamps must be UTC.");
            }
            if (completedUtc < startedUtc) {
                throw new ArgumentException("Verification run completion precedes its start.");
            }
            if (expectedSampleCount < 1 || values.Any(value => !double.IsFinite(value))) {
                throw new ArgumentException("Verification run summary is incomplete or non-finite.");
            }
            if (effectivePointSettleSeconds < 0.0) {
                throw new ArgumentOutOfRangeException(nameof(effectivePointSettleSeconds));
            }
            var componentTotalsConsistent = VectorTotalIsConsistent(initialAzimuthMinutes, initialAltitudeMinutes, initialTotalMinutes)
                && VectorTotalIsConsistent(reciprocalAzimuthMinutes, reciprocalAltitudeMinutes, reciprocalTotalMinutes)
                && VectorTotalIsConsistent(repeatedForwardAzimuthMinutes, repeatedForwardAltitudeMinutes, repeatedForwardTotalMinutes);

            return new(
                CurrentSchemaVersion,
                runId,
                startedUtc,
                completedUtc,
                (completedUtc - startedUtc).TotalSeconds,
                repeatabilityPassed && reciprocityPassed,
                repeatabilityPassed,
                reciprocityPassed,
                expectedSampleCount,
                refractionAdjustmentEnabled,
                overdeterminedShadowModelCheck,
                effectivePointSettleSeconds,
                componentTotalsConsistent,
                initialAzimuthMinutes,
                initialAltitudeMinutes,
                initialTotalMinutes,
                reciprocalAzimuthMinutes,
                reciprocalAltitudeMinutes,
                reciprocalTotalMinutes,
                repeatedForwardAzimuthMinutes,
                repeatedForwardAltitudeMinutes,
                repeatedForwardTotalMinutes,
                repeatabilityVectorSeparationMinutes,
                reciprocityVectorSeparationMinutes);
        }

        private static bool VectorTotalIsConsistent(double azimuthMinutes, double altitudeMinutes, double totalMinutes) {
            const double toleranceMinutes = 0.01;
            var expected = Math.Sqrt(azimuthMinutes * azimuthMinutes + altitudeMinutes * altitudeMinutes);
            return Math.Abs(expected - totalMinutes) <= toleranceMinutes;
        }

        public string ToJson() => TppaVerificationReceiptJson.Serialize(this);
    }

    internal sealed class TppaVerificationSampleLedger {
        private readonly Guid runId;
        private readonly int expectedSampleCount;
        private readonly List<TppaVerificationPointReceipt> samples = new();

        public TppaVerificationSampleLedger(Guid runId, int expectedSampleCount) {
            if (runId == Guid.Empty) {
                throw new ArgumentException("Verification sample ledger requires a non-empty run id.", nameof(runId));
            }
            if (expectedSampleCount < 1) {
                throw new ArgumentOutOfRangeException(nameof(expectedSampleCount));
            }
            this.runId = runId;
            this.expectedSampleCount = expectedSampleCount;
        }

        public TppaVerificationPointReceipt Append(TppaVerificationPointReceipt sample) {
            if (sample == null) {
                throw new ArgumentNullException(nameof(sample));
            }
            if (sample.SchemaVersion != TppaVerificationPointReceipt.CurrentSchemaVersion) {
                throw new ArgumentException("Verification point receipt schema is unsupported.", nameof(sample));
            }
            if (sample.RunId != runId) {
                throw new ArgumentException("Verification point receipt run id does not match the active ledger.", nameof(sample));
            }
            if (sample.ObservationUtc.Kind != DateTimeKind.Utc) {
                throw new ArgumentException("Verification point observation time must be UTC.", nameof(sample));
            }
            if (string.IsNullOrWhiteSpace(sample.Direction)
                    || sample.PointIndex < 1
                    || sample.DirectionSampleCount < sample.PointIndex
                    || !AllFinite(sample)) {
                throw new ArgumentException("Verification point receipt is incomplete or non-finite.", nameof(sample));
            }
            if (samples.Count >= expectedSampleCount) {
                throw new InvalidOperationException("Verification sample ledger already contains its expected sample count.");
            }

            var preserved = sample with { SequenceIndex = samples.Count + 1 };
            samples.Add(preserved);
            return preserved;
        }

        public TppaVerificationPartialDatasetReceipt Snapshot(bool measurementCompleted) {
            var issues = new List<string>();
            if (samples.Count != expectedSampleCount) {
                issues.Add($"collected {samples.Count} of {expectedSampleCount} expected samples");
            }
            if (measurementCompleted && samples.Count != expectedSampleCount) {
                issues.Add("measurement completion was claimed before every expected sample was preserved");
            }
            var isComplete = measurementCompleted && samples.Count == expectedSampleCount;
            return new(
                TppaVerificationPartialDatasetReceipt.CurrentSchemaVersion,
                runId,
                measurementCompleted,
                isComplete,
                samples.Count,
                expectedSampleCount,
                samples.ToArray(),
                issues);
        }

        private static bool AllFinite(TppaVerificationPointReceipt sample) =>
            new[] {
                sample.MountAzimuthDegrees,
                sample.MountAltitudeDegrees,
                sample.SolvedRightAscensionDegrees,
                sample.SolvedDeclinationDegrees,
                sample.PositionAngleDegrees,
                sample.VectorX,
                sample.VectorY,
                sample.VectorZ
            }.All(double.IsFinite);
    }

    internal static class TppaVerificationReceiptRunner {
        public static async Task<TResult> Run<TResult>(
                Func<Task<TResult>> operation,
                TppaVerificationSampleLedger ledger,
                Action<TppaVerificationPartialDatasetReceipt> preserveReceipt) {
            if (operation == null) {
                throw new ArgumentNullException(nameof(operation));
            }
            if (ledger == null) {
                throw new ArgumentNullException(nameof(ledger));
            }
            if (preserveReceipt == null) {
                throw new ArgumentNullException(nameof(preserveReceipt));
            }

            var completed = false;
            try {
                var result = await operation().ConfigureAwait(false);
                completed = true;
                return result;
            } finally {
                preserveReceipt(ledger.Snapshot(completed));
            }
        }
    }

    internal static class TppaVerificationReceiptJson {
        public static string Serialize(object value) {
            var settings = new JsonSerializerSettings {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Culture = CultureInfo.InvariantCulture,
                NullValueHandling = NullValueHandling.Include
            };
            return JsonConvert.SerializeObject(value, Formatting.None, settings);
        }
    }
}
