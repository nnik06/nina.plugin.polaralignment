using System;
using System.Collections.Generic;
using System.Linq;
using NINA.Astrometry;
using NINA.Core.Enum;
using NINA.Core.Utility;

namespace NINA.Plugins.PolarAlignment {
    internal readonly record struct TppaRaRotationWitnessHorizontalPosition(
        double AzimuthDegrees,
        double AltitudeDegrees);

    internal readonly record struct TppaRaRotationWitnessTrajectorySample(
        string SegmentId,
        double RightAscensionOffsetDegrees,
        DateTime PredictedTimeUtc,
        double AzimuthDegrees,
        double AltitudeDegrees,
        PierSide PredictedPierSide);

    internal sealed record TppaRaRotationWitnessTrajectoryPreflightResult(
        bool IsSafe,
        double TotalArcDegrees,
        double DesignConditionProxy,
        double MinimumPredictedAltitudeDegrees,
        IReadOnlyList<TppaRaRotationWitnessTrajectorySample> Samples,
        string Reason);

    internal static class TppaRaRotationWitnessTrajectoryPreflight {
        public const double DefaultMinimumTotalArcDegrees = 45;
        public const double AbsoluteMinimumTotalArcDegrees = 30;
        public const double DefaultMaximumSampleStepDegrees = 1;
        public const double DefaultOperationalAltitudeFloorDegrees = 40;
        public const double DefaultMaximumDesignConditionProxy = 4;

        public static TppaRaRotationWitnessTrajectoryPreflightResult Evaluate(
                Coordinates pointA,
                double legDistanceDegrees,
                bool eastDirection,
                DateTime startTimeUtc,
                Angle latitude,
                Angle longitude,
                double elevationMeters,
                RefractionParameters refraction,
                TppaMountMotionEnvelope hardEnvelope,
                Func<Coordinates, PierSide> destinationSideOfPier,
                TimeSpan? captureDuration = null,
                TimeSpan? movementDurationPerLeg = null,
                double maximumSampleStepDegrees = DefaultMaximumSampleStepDegrees,
                double minimumTotalArcDegrees = DefaultMinimumTotalArcDegrees,
                double operationalAltitudeFloorDegrees = DefaultOperationalAltitudeFloorDegrees,
                double maximumDesignConditionProxy = DefaultMaximumDesignConditionProxy) {
            ArgumentNullException.ThrowIfNull(pointA);
            ArgumentNullException.ThrowIfNull(refraction);
            ArgumentNullException.ThrowIfNull(destinationSideOfPier);

            return EvaluateProjected(
                pointA,
                legDistanceDegrees,
                eastDirection,
                startTimeUtc,
                hardEnvelope,
                coordinate => {
                    var horizontal = coordinate.Transform(
                        latitude,
                        longitude,
                        elevationMeters,
                        refraction.PressureHPa,
                        refraction.Temperature,
                        refraction.RelativeHumidity,
                        refraction.Wavelength,
                        coordinate.DateTime.UtcNow);
                    return new TppaRaRotationWitnessHorizontalPosition(
                        horizontal.Azimuth.Degree,
                        horizontal.Altitude.Degree);
                },
                destinationSideOfPier,
                captureDuration,
                movementDurationPerLeg,
                maximumSampleStepDegrees,
                minimumTotalArcDegrees,
                operationalAltitudeFloorDegrees,
                maximumDesignConditionProxy);
        }

        internal static TppaRaRotationWitnessTrajectoryPreflightResult EvaluateProjected(
                Coordinates pointA,
                double legDistanceDegrees,
                bool eastDirection,
                DateTime startTimeUtc,
                TppaMountMotionEnvelope hardEnvelope,
                Func<Coordinates, TppaRaRotationWitnessHorizontalPosition> horizontalProjector,
                Func<Coordinates, PierSide> destinationSideOfPier,
                TimeSpan? captureDuration = null,
                TimeSpan? movementDurationPerLeg = null,
                double maximumSampleStepDegrees = DefaultMaximumSampleStepDegrees,
                double minimumTotalArcDegrees = DefaultMinimumTotalArcDegrees,
                double operationalAltitudeFloorDegrees = DefaultOperationalAltitudeFloorDegrees,
                double maximumDesignConditionProxy = DefaultMaximumDesignConditionProxy) {
            ArgumentNullException.ThrowIfNull(pointA);
            ArgumentNullException.ThrowIfNull(horizontalProjector);
            ArgumentNullException.ThrowIfNull(destinationSideOfPier);

            var capture = captureDuration ?? TimeSpan.FromSeconds(15);
            var movement = movementDurationPerLeg ?? TimeSpan.FromSeconds(45);
            ValidateInputs(
                pointA,
                legDistanceDegrees,
                startTimeUtc,
                capture,
                movement,
                maximumSampleStepDegrees,
                minimumTotalArcDegrees,
                operationalAltitudeFloorDegrees,
                maximumDesignConditionProxy);

            var totalArcDegrees = 2 * legDistanceDegrees;
            var conditionProxy = 1 / (
                2 * Math.Pow(Math.Sin(totalArcDegrees * Math.PI / 360), 2));
            if (totalArcDegrees < minimumTotalArcDegrees) {
                return Rejected(
                    totalArcDegrees,
                    conditionProxy,
                    $"RA witness total arc {totalArcDegrees:F2} deg is below the {minimumTotalArcDegrees:F2} deg qualification floor");
            }
            if (conditionProxy > maximumDesignConditionProxy) {
                return Rejected(
                    totalArcDegrees,
                    conditionProxy,
                    $"RA witness design condition proxy {conditionProxy:F3} exceeds {maximumDesignConditionProxy:F3}");
            }

            var sign = eastDirection ? 1d : -1d;
            var samples = new List<TppaRaRotationWitnessTrajectorySample>();
            var time = startTimeUtc;
            AddDwell("A-capture", 0, time, time + capture);
            time += capture;
            AddMotion("A-B-slew", 0, sign * legDistanceDegrees, time, time + movement);
            time += movement;
            AddDwell("B-capture", sign * legDistanceDegrees, time, time + capture);
            time += capture;
            AddMotion(
                "B-C-slew",
                sign * legDistanceDegrees,
                sign * totalArcDegrees,
                time,
                time + movement);
            time += movement;
            AddDwell("C-capture", sign * totalArcDegrees, time, time + capture);
            time += capture;
            AddMotion(
                "C-A-return-slew",
                sign * totalArcDegrees,
                0,
                time,
                time + TimeSpan.FromTicks(2 * movement.Ticks));
            time += TimeSpan.FromTicks(2 * movement.Ticks);
            AddDwell("A-return-capture", 0, time, time + capture);

            var minimumAltitude = double.PositiveInfinity;
            PierSide? expectedPierSide = null;
            foreach (var sample in samples) {
                if (!double.IsFinite(sample.AzimuthDegrees)
                        || !double.IsFinite(sample.AltitudeDegrees)) {
                    return Rejected(
                        totalArcDegrees,
                        conditionProxy,
                        $"{sample.SegmentId} produced a non-finite horizontal position",
                        samples);
                }

                minimumAltitude = Math.Min(minimumAltitude, sample.AltitudeDegrees);
                var envelopeIssue = hardEnvelope.Validate(
                    sample.AzimuthDegrees,
                    sample.AltitudeDegrees);
                if (!string.IsNullOrWhiteSpace(envelopeIssue)) {
                    return Rejected(
                        totalArcDegrees,
                        conditionProxy,
                        $"{sample.SegmentId} at RA offset {sample.RightAscensionOffsetDegrees:F2} deg is unsafe: {envelopeIssue}",
                        samples,
                        minimumAltitude);
                }
                if (sample.AltitudeDegrees < operationalAltitudeFloorDegrees) {
                    return Rejected(
                        totalArcDegrees,
                        conditionProxy,
                        $"{sample.SegmentId} altitude {sample.AltitudeDegrees:F2} deg is below the {operationalAltitudeFloorDegrees:F2} deg operational floor",
                        samples,
                        minimumAltitude);
                }
                if (sample.PredictedPierSide == PierSide.pierUnknown) {
                    return Rejected(
                        totalArcDegrees,
                        conditionProxy,
                        $"{sample.SegmentId} has unknown predicted pier side",
                        samples,
                        minimumAltitude);
                }

                expectedPierSide ??= sample.PredictedPierSide;
                if (sample.PredictedPierSide != expectedPierSide) {
                    return Rejected(
                        totalArcDegrees,
                        conditionProxy,
                        $"{sample.SegmentId} changes predicted pier side from {expectedPierSide} to {sample.PredictedPierSide}",
                        samples,
                        minimumAltitude);
                }
            }

            return new TppaRaRotationWitnessTrajectoryPreflightResult(
                true,
                totalArcDegrees,
                conditionProxy,
                minimumAltitude,
                samples.AsReadOnly(),
                $"full A-B-C-A trajectory passed with {samples.Count} samples, minimum altitude {minimumAltitude:F2} deg, constant {expectedPierSide} pier side");

            void AddDwell(
                    string segmentId,
                    double offsetDegrees,
                    DateTime dwellStartUtc,
                    DateTime dwellEndUtc) {
                AddSample(segmentId, offsetDegrees, dwellStartUtc);
                AddSample(segmentId, offsetDegrees, dwellEndUtc);
            }

            void AddMotion(
                    string segmentId,
                    double startOffsetDegrees,
                    double endOffsetDegrees,
                    DateTime motionStartUtc,
                    DateTime motionEndUtc) {
                var steps = Math.Max(
                    1,
                    (int)Math.Ceiling(
                        Math.Abs(endOffsetDegrees - startOffsetDegrees)
                        / maximumSampleStepDegrees));
                for (var step = 0; step <= steps; step++) {
                    var fraction = step / (double)steps;
                    var offset = startOffsetDegrees
                        + (endOffsetDegrees - startOffsetDegrees) * fraction;
                    var predictedTime = motionStartUtc
                        + TimeSpan.FromTicks(
                            (long)Math.Round(
                                (motionEndUtc - motionStartUtc).Ticks * fraction));
                    AddSample(segmentId, offset, predictedTime);
                }
            }

            void AddSample(string segmentId, double offsetDegrees, DateTime predictedTimeUtc) {
                var coordinate = Offset(pointA, offsetDegrees, predictedTimeUtc);
                var horizontal = horizontalProjector(coordinate);
                samples.Add(new TppaRaRotationWitnessTrajectorySample(
                    segmentId,
                    offsetDegrees,
                    predictedTimeUtc,
                    horizontal.AzimuthDegrees,
                    horizontal.AltitudeDegrees,
                    destinationSideOfPier(coordinate)));
            }
        }

        private static void ValidateInputs(
                Coordinates pointA,
                double legDistanceDegrees,
                DateTime startTimeUtc,
                TimeSpan captureDuration,
                TimeSpan movementDurationPerLeg,
                double maximumSampleStepDegrees,
                double minimumTotalArcDegrees,
                double operationalAltitudeFloorDegrees,
                double maximumDesignConditionProxy) {
            if (!double.IsFinite(pointA.RADegrees)
                    || !double.IsFinite(pointA.Dec)
                    || !double.IsFinite(legDistanceDegrees)
                    || legDistanceDegrees <= 0
                    || legDistanceDegrees >= 90) {
                throw new ArgumentOutOfRangeException(
                    nameof(legDistanceDegrees),
                    "RA witness leg distance must be finite and between 0 and 90 degrees");
            }
            if (startTimeUtc.Kind != DateTimeKind.Utc) {
                throw new ArgumentException(
                    "RA witness preflight start time must be UTC",
                    nameof(startTimeUtc));
            }
            if (captureDuration <= TimeSpan.Zero
                    || movementDurationPerLeg <= TimeSpan.Zero) {
                throw new ArgumentOutOfRangeException(
                    nameof(captureDuration),
                    "capture and movement durations must be positive");
            }
            if (!double.IsFinite(maximumSampleStepDegrees)
                    || maximumSampleStepDegrees <= 0
                    || maximumSampleStepDegrees > DefaultMaximumSampleStepDegrees) {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumSampleStepDegrees),
                    $"trajectory sampling step must be finite, positive, and no greater than {DefaultMaximumSampleStepDegrees:F0} degree");
            }
            if (!double.IsFinite(minimumTotalArcDegrees)
                    || minimumTotalArcDegrees < AbsoluteMinimumTotalArcDegrees
                    || minimumTotalArcDegrees >= 180) {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumTotalArcDegrees),
                    $"minimum total arc must be at least {AbsoluteMinimumTotalArcDegrees:F0} degrees and below 180 degrees");
            }
            if (!double.IsFinite(operationalAltitudeFloorDegrees)
                    || operationalAltitudeFloorDegrees < 0
                    || operationalAltitudeFloorDegrees >= 90) {
                throw new ArgumentOutOfRangeException(
                    nameof(operationalAltitudeFloorDegrees),
                    "operational altitude floor must be finite and between 0 and 90 degrees");
            }
            if (!double.IsFinite(maximumDesignConditionProxy)
                    || maximumDesignConditionProxy <= 0) {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumDesignConditionProxy),
                    "maximum design condition proxy must be finite and positive");
            }
        }

        private static Coordinates Offset(
                Coordinates source,
                double rightAscensionOffsetDegrees,
                DateTime observationTimeUtc) => new(
            Angle.ByDegree(NormalizeDegrees(
                source.RADegrees + rightAscensionOffsetDegrees)),
            Angle.ByDegree(source.Dec),
            source.Epoch,
            new FixedObservationDateTime(observationTimeUtc));

        private static TppaRaRotationWitnessTrajectoryPreflightResult Rejected(
                double totalArcDegrees,
                double conditionProxy,
                string reason,
                IReadOnlyList<TppaRaRotationWitnessTrajectorySample> samples = null,
                double minimumAltitudeDegrees = double.NaN) => new(
            false,
            totalArcDegrees,
            conditionProxy,
            minimumAltitudeDegrees,
            samples ?? Array.Empty<TppaRaRotationWitnessTrajectorySample>(),
            reason);

        private static double NormalizeDegrees(double value) =>
            (value % 360 + 360) % 360;
    }
}
