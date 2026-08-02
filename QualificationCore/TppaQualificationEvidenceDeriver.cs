using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugins.PolarAlignment {
    internal sealed record TppaQualificationRunDerivedEvidence(
        IReadOnlyList<TppaQualificationDeterminationEvidence> OrderedDeterminations,
        IReadOnlyList<TppaQualificationVector> MountAxisVectors,
        TppaQualificationVector TargetPoleVector,
        bool FreshSolvesUncached,
        bool GeometryQualified,
        bool MinimumArcSpanQualified,
        bool ClosureQualified,
        bool AtmosphereQualified,
        bool AtmosphereFresh,
        bool StationPressureQualified,
        bool AtmosphereTemperatureQualified,
        bool AtmosphereHumidityQualified,
        bool SiteTimeProvenanceQualified,
        bool CoordinateFrameQualified);

    internal sealed record TppaQualificationWitnessDerivedEvidence(
        TppaQualificationVector MountAxisVector,
        bool GeometryQualified,
        bool MinimumArcSpanQualified,
        bool ClosureQualified);

    /// <summary>
    /// Recomputes absolute-qualification inputs from persisted raw evidence.
    /// Producer-supplied booleans and fitted axes are cross-checks only.
    /// </summary>
    internal static class TppaQualificationEvidenceDeriver {
        private const double DeclaredAxisToleranceArcSeconds = 0.05;
        private const double DeclaredPoleToleranceArcSeconds = 0.05;

        public static TppaQualificationRunDerivedEvidence Derive(
                TppaQualificationRunEvidence evidence,
                ICollection<string> issues) {
            ArgumentNullException.ThrowIfNull(evidence);
            ArgumentNullException.ThrowIfNull(issues);

            var ordered = (evidence.Determinations
                    ?? Array.Empty<TppaQualificationDeterminationEvidence>())
                .Where(value => value != null)
                .OrderBy(value => value.StartedUtc)
                .ToArray();
            var siteQualified = IsSiteQualified(evidence);
            var targetPole = siteQualified
                ? TruePoleVector(evidence.SiteLatitudeDegrees)
                : null;
            if (!siteQualified) {
                issues.Add("TPPA raw site latitude, longitude, or elevation is invalid");
            }
            if (targetPole == null || !IsUnitVector(evidence.TargetPoleVector)
                    || AngularSeparationArcSeconds(
                        targetPole,
                        evidence.TargetPoleVector) > DeclaredPoleToleranceArcSeconds) {
                issues.Add("TPPA declared target-pole vector does not match the raw site latitude");
            }

            var axisVectors = new List<TppaQualificationVector>();
            var allDigests = new HashSet<string>(StringComparer.Ordinal);
            var allFresh = ordered.Length >= 3;
            var allGeometry = ordered.Length >= 3;
            var allSpan = ordered.Length >= 3;
            foreach (var determination in ordered) {
                var solves = determination.SourceSolves?.Where(value => value != null)
                    .ToArray() ?? Array.Empty<TppaQualificationSolveEvidence>();
                if (solves.Length != 3
                        || solves.Length != (determination.SourceSolves?.Count ?? 0)) {
                    issues.Add("each TPPA determination must persist exactly three raw solves");
                    allFresh = false;
                    allGeometry = false;
                    allSpan = false;
                    continue;
                }

                var declaredDigests = determination.SourceVectorDigests?.ToArray()
                    ?? Array.Empty<string>();
                if (!declaredDigests.SequenceEqual(
                        solves.Select(value => value.ContentSha256),
                        StringComparer.Ordinal)) {
                    issues.Add("TPPA raw solve digests do not match the declared source-vector digests");
                    allFresh = false;
                }

                DateTime? previousObservation = null;
                string pierSide = null;
                foreach (var solve in solves) {
                    var solveValid = IsUtc(solve.ObservationUtc)
                        && solve.ObservationUtc >= determination.StartedUtc
                        && solve.ObservationUtc <= determination.CompletedUtc
                        && (!previousObservation.HasValue
                            || solve.ObservationUtc > previousObservation.Value)
                        && IsSha256(solve.ContentSha256)
                        && allDigests.Add(solve.ContentSha256)
                        && double.IsFinite(solve.RightAscensionDegrees)
                        && solve.RightAscensionDegrees >= 0
                        && solve.RightAscensionDegrees < 360
                        && double.IsFinite(solve.DeclinationDegrees)
                        && solve.DeclinationDegrees >= -90
                        && solve.DeclinationDegrees <= 90
                        && IsKnownPierSide(solve.PierSide)
                        && IsUnitVector(solve.UnitVector);
                    if (!solveValid) {
                        issues.Add("TPPA raw solve evidence is stale, unordered, malformed, reused, or outside its interval");
                        allFresh = false;
                    }
                    if (pierSide != null && solve.PierSide != pierSide) {
                        issues.Add("TPPA pier side changed within a determination");
                        allFresh = false;
                    }
                    pierSide ??= solve.PierSide;
                    previousObservation = solve.ObservationUtc;
                }

                if (!solves.All(value => IsUnitVector(value.UnitVector))) {
                    allGeometry = false;
                    allSpan = false;
                    continue;
                }
                var geometry = EvaluateGeometry(
                    solves[0].UnitVector,
                    solves[1].UnitVector,
                    solves[2].UnitVector);
                var geometryQualified = geometry.IsFinite && !geometry.IsDegenerate;
                var spanQualified = geometryQualified
                    && geometry.MinimumPairwiseSeparationDegrees
                        >= TppaAbsoluteEvidenceBinder.MinimumQualifiedArcSpanDegrees;
                allGeometry &= geometryQualified;
                allSpan &= spanQualified;

                if (determination.GeometryQualified != geometryQualified) {
                    issues.Add("TPPA declared geometry qualification disagrees with raw vectors");
                }
                if (determination.MinimumArcSpanQualified != spanQualified) {
                    issues.Add("TPPA declared minimum-span qualification disagrees with raw vectors");
                }

                if (!geometryQualified || targetPole == null) {
                    continue;
                }
                var axis = DeterminePlaneVector(
                    solves[0].UnitVector,
                    solves[1].UnitVector,
                    solves[2].UnitVector);
                if ((evidence.SiteLatitudeDegrees >= 0 && axis.X < 0)
                        || (evidence.SiteLatitudeDegrees < 0 && axis.X > 0)) {
                    axis = Scale(axis, -1);
                }
                axisVectors.Add(axis);
                if (!IsUnitVector(determination.MountAxisVector)
                        || AngularSeparationArcSeconds(
                            axis,
                            determination.MountAxisVector)
                            > DeclaredAxisToleranceArcSeconds) {
                    issues.Add("TPPA declared mount-axis vector does not match the raw three-point fit");
                }
            }

            if (ordered.Any(value => value.FreshSolvesUncached != allFresh)) {
                issues.Add("TPPA declared solve freshness disagrees with raw solve evidence");
            }
            if (axisVectors.Count != ordered.Length) {
                issues.Add("TPPA raw solve vectors do not yield one mount-axis fit per determination");
            }

            var closureQualified = IsClosureQualified(ordered);
            if (ordered.Any(value => value.ClosureQualified != closureQualified)) {
                issues.Add("TPPA declared closure qualification disagrees with raw solves");
            }

            var runStartedUtc = ordered.Length > 0 ? ordered[0].StartedUtc : default;
            var runCompletedUtc = ordered.Length > 0 ? ordered[^1].CompletedUtc : default;
            var runBoundsQualified = ordered.Length > 0
                && IsUtc(runStartedUtc)
                && IsUtc(runCompletedUtc)
                && runCompletedUtc > runStartedUtc;
            var pressureQualified = double.IsFinite(evidence.AtmospherePressureHPa)
                && evidence.AtmospherePressureHPa >= 500
                && evidence.AtmospherePressureHPa <= 1100;
            var temperatureQualified =
                double.IsFinite(evidence.AtmosphereTemperatureCelsius)
                && evidence.AtmosphereTemperatureCelsius >= -100
                && evidence.AtmosphereTemperatureCelsius <= 100;
            var humidityQualified =
                double.IsFinite(evidence.AtmosphereRelativeHumidityPercent)
                && evidence.AtmosphereRelativeHumidityPercent >= 0
                && evidence.AtmosphereRelativeHumidityPercent <= 100;
            var atmosphereFresh = runBoundsQualified
                && IsUtc(evidence.AtmosphereObservationUtc)
                && evidence.AtmosphereObservationUtc
                    >= runStartedUtc.AddSeconds(
                        -TppaAbsoluteEvidenceBinder.MaximumAtmosphereAgeSeconds)
                && evidence.AtmosphereObservationUtc
                    <= runCompletedUtc.AddSeconds(
                        TppaAbsoluteEvidenceBinder.MaximumAtmosphereAgeSeconds);
            var atmosphereQualified = evidence.AtmosphereSource
                    == TppaFastQualificationConventions.QualifiedLocalWeatherStation
                && atmosphereFresh
                && pressureQualified
                && temperatureQualified
                && humidityQualified;
            var clockQualified = double.IsFinite(
                    evidence.ClockUncertaintyMilliseconds)
                && evidence.ClockUncertaintyMilliseconds >= 0
                && evidence.ClockUncertaintyMilliseconds
                    <= TppaAbsoluteEvidenceBinder.MaximumClockUncertaintyMilliseconds;
            var frameQualified = evidence.CoordinateFrame
                    == TppaFastQualificationConventions.IcrsObservationEpoch
                && evidence.MountAxisVectorFrame
                    == TppaAbsoluteEvidenceBinder.TopocentricHorizonNorthWestUp;

            CrossCheckClaim(evidence.AtmosphereQualified, atmosphereQualified,
                "atmosphere qualification", issues);
            CrossCheckClaim(evidence.AtmosphereFresh, atmosphereFresh,
                "atmosphere freshness", issues);
            CrossCheckClaim(evidence.StationPressureQualified, pressureQualified,
                "station-pressure qualification", issues);
            CrossCheckClaim(evidence.AtmosphereTemperatureQualified,
                temperatureQualified, "temperature qualification", issues);
            CrossCheckClaim(evidence.AtmosphereHumidityQualified,
                humidityQualified, "humidity qualification", issues);
            CrossCheckClaim(evidence.SiteTimeProvenanceQualified,
                siteQualified && clockQualified && runBoundsQualified,
                "site/time provenance qualification", issues);
            CrossCheckClaim(evidence.CoordinateFrameQualified, frameQualified,
                "coordinate-frame qualification", issues);

            return new(
                ordered,
                axisVectors,
                targetPole,
                allFresh,
                allGeometry,
                allSpan,
                closureQualified,
                atmosphereQualified,
                atmosphereFresh,
                pressureQualified,
                temperatureQualified,
                humidityQualified,
                siteQualified && clockQualified && runBoundsQualified,
                frameQualified);
        }

        public static TppaQualificationWitnessDerivedEvidence DeriveWitness(
                TppaQualificationWitnessEvidence evidence,
                ICollection<string> issues) {
            ArgumentNullException.ThrowIfNull(evidence);
            ArgumentNullException.ThrowIfNull(issues);

            var solves = evidence.SourceSolves?.Where(value => value != null)
                .ToArray() ?? Array.Empty<TppaQualificationSolveEvidence>();
            if (solves.Length != 4
                    || solves.Length != (evidence.SourceSolves?.Count ?? 0)) {
                issues.Add("witness must persist exactly four raw RA-rotation solves A/B/C/A");
                return new(null, false, false, false);
            }
            var declaredDigests = evidence.SourceVectorDigests?.ToArray()
                ?? Array.Empty<string>();
            if (!declaredDigests.SequenceEqual(
                    solves.Select(value => value.ContentSha256),
                    StringComparer.Ordinal)) {
                issues.Add("witness raw solve digests do not match its declared source-vector digests");
            }

            var uniqueDigests = new HashSet<string>(StringComparer.Ordinal);
            DateTime? previous = null;
            string pierSide = null;
            foreach (var solve in solves) {
                if (!IsUtc(solve.ObservationUtc)
                        || (previous.HasValue
                            && solve.ObservationUtc <= previous.Value)
                        || solve.ObservationUtc > evidence.ObservationUtc
                        || !IsSha256(solve.ContentSha256)
                        || !uniqueDigests.Add(solve.ContentSha256)
                        || !double.IsFinite(solve.RightAscensionDegrees)
                        || solve.RightAscensionDegrees < 0
                        || solve.RightAscensionDegrees >= 360
                        || !double.IsFinite(solve.DeclinationDegrees)
                        || solve.DeclinationDegrees < -90
                        || solve.DeclinationDegrees > 90
                        || !IsKnownPierSide(solve.PierSide)
                        || !IsUnitVector(solve.UnitVector)) {
                    issues.Add("witness raw solve evidence is unordered, malformed, reused, or outside its observation interval");
                }
                if (pierSide != null && solve.PierSide != pierSide) {
                    issues.Add("witness pier side changed within the RA-rotation arc");
                }
                pierSide ??= solve.PierSide;
                previous = solve.ObservationUtc;
            }
            if (solves[^1].ObservationUtc != evidence.ObservationUtc) {
                issues.Add("witness observation time does not equal the final returned-A solve time");
            }

            var geometry = EvaluateGeometry(
                solves[0].UnitVector,
                solves[1].UnitVector,
                solves[2].UnitVector);
            var geometryQualified = geometry.IsFinite && !geometry.IsDegenerate;
            var spanQualified = geometryQualified
                && geometry.MinimumPairwiseSeparationDegrees
                    >= TppaAbsoluteEvidenceBinder.MinimumQualifiedArcSpanDegrees;
            if (!geometryQualified) {
                issues.Add("witness raw RA-rotation geometry is degenerate");
            }
            if (!spanQualified) {
                issues.Add("witness raw RA-rotation arc is below the qualified span");
            }

            var closureQualified = IsSolveClosureQualified(solves[0], solves[^1]);
            if (!closureQualified) {
                issues.Add("witness returned-A closure failed");
            }

            TppaQualificationVector axis = null;
            if (geometryQualified) {
                axis = DeterminePlaneVector(
                    solves[0].UnitVector,
                    solves[1].UnitVector,
                    solves[2].UnitVector);
                if ((evidence.SiteLatitudeDegrees >= 0 && axis.X < 0)
                        || (evidence.SiteLatitudeDegrees < 0 && axis.X > 0)) {
                    axis = Scale(axis, -1);
                }
                if (!IsUnitVector(evidence.MountAxisVector)
                        || AngularSeparationArcSeconds(
                            axis,
                            evidence.MountAxisVector)
                            > DeclaredAxisToleranceArcSeconds) {
                    issues.Add("witness declared mount-axis vector does not match the raw RA-rotation fit");
                }
            }
            return new(axis, geometryQualified, spanQualified, closureQualified);
        }

        private static void CrossCheckClaim(
                bool declared,
                bool derived,
                string label,
                ICollection<string> issues) {
            if (declared != derived) {
                issues.Add($"TPPA declared {label} disagrees with raw evidence");
            }
        }

        private static bool IsSiteQualified(TppaQualificationRunEvidence evidence) =>
            double.IsFinite(evidence.SiteLatitudeDegrees)
            && evidence.SiteLatitudeDegrees >= -90
            && evidence.SiteLatitudeDegrees <= 90
            && double.IsFinite(evidence.SiteLongitudeDegrees)
            && evidence.SiteLongitudeDegrees >= -180
            && evidence.SiteLongitudeDegrees <= 180
            && double.IsFinite(evidence.SiteElevationMeters)
            && evidence.SiteElevationMeters >= -500
            && evidence.SiteElevationMeters <= 10000
            && !string.IsNullOrWhiteSpace(evidence.SiteIdentity);

        private static TppaQualificationVector TruePoleVector(double latitudeDegrees) {
            var latitudeRadians = Math.Abs(latitudeDegrees) * Math.PI / 180.0;
            var hemisphere = latitudeDegrees >= 0 ? 1.0 : -1.0;
            return new(
                hemisphere * Math.Cos(latitudeRadians),
                0,
                Math.Sin(latitudeRadians));
        }

        private static bool IsClosureQualified(
                IReadOnlyList<TppaQualificationDeterminationEvidence> ordered) {
            if (ordered.Count < 2
                    || ordered[0].SourceSolves?.Count != 3
                    || ordered[^1].SourceSolves?.Count != 3) {
                return false;
            }
            var first = ordered[0].SourceSolves[0];
            var returned = ordered[^1].SourceSolves[0];
            if (first == null || returned == null
                    || !IsKnownPierSide(first.PierSide)
                    || !IsKnownPierSide(returned.PierSide)
                    || first.PierSide != returned.PierSide) {
                return false;
            }
            return IsSolveClosureQualified(first, returned);
        }

        private static bool IsSolveClosureQualified(
                TppaQualificationSolveEvidence first,
                TppaQualificationSolveEvidence returned) =>
            first != null
            && returned != null
            && IsKnownPierSide(first.PierSide)
            && first.PierSide == returned.PierSide
            && GreatCircleDistanceDegrees(
                first.RightAscensionDegrees,
                first.DeclinationDegrees,
                returned.RightAscensionDegrees,
                returned.DeclinationDegrees)
                <= TppaAbsoluteEvidenceBinder.MaximumClosureSeparationDegrees
            && AngularSeparationDegrees(first.UnitVector, returned.UnitVector)
                <= TppaAbsoluteEvidenceBinder.MaximumClosureSeparationDegrees;

        private static TppaQualificationVector DeterminePlaneVector(
                TppaQualificationVector first,
                TppaQualificationVector second,
                TppaQualificationVector third) {
            var left = Subtract(second, first);
            var right = Subtract(third, second);
            return Normalize(Cross(left, right));
        }

        private static Geometry EvaluateGeometry(
                TppaQualificationVector first,
                TppaQualificationVector second,
                TppaQualificationVector third) {
            var side12 = Length(Subtract(second, first));
            var side23 = Length(Subtract(third, second));
            var side13 = Length(Subtract(third, first));
            var doubledArea = Length(Cross(
                Subtract(second, first),
                Subtract(third, first)));
            var separations = new[] {
                AngularSeparationDegrees(first, second),
                AngularSeparationDegrees(second, third),
                AngularSeparationDegrees(first, third)
            };
            return new(
                Math.Min(separations[0], Math.Min(separations[1], separations[2])),
                Math.Max(separations[0], Math.Max(separations[1], separations[2])),
                doubledArea,
                side12,
                side23,
                side13);
        }

        private static double GreatCircleDistanceDegrees(
                double firstRaDegrees,
                double firstDecDegrees,
                double secondRaDegrees,
                double secondDecDegrees) {
            var firstDec = DegreesToRadians(firstDecDegrees);
            var secondDec = DegreesToRadians(secondDecDegrees);
            var deltaRa = DegreesToRadians(secondRaDegrees - firstRaDegrees);
            var cosine = Math.Sin(firstDec) * Math.Sin(secondDec)
                + Math.Cos(firstDec) * Math.Cos(secondDec) * Math.Cos(deltaRa);
            return Math.Acos(Math.Clamp(cosine, -1, 1)) * 180.0 / Math.PI;
        }

        private static double AngularSeparationArcSeconds(
                TppaQualificationVector first,
                TppaQualificationVector second) =>
            AngularSeparationDegrees(first, second) * 3600.0;

        private static double AngularSeparationDegrees(
                TppaQualificationVector first,
                TppaQualificationVector second) {
            var denominator = Length(first) * Length(second);
            if (!(denominator > 0)) { return double.NaN; }
            var cosine = Math.Clamp(Dot(first, second) / denominator, -1, 1);
            return Math.Acos(cosine) * 180.0 / Math.PI;
        }

        private static TppaQualificationVector Subtract(
                TppaQualificationVector left,
                TppaQualificationVector right) =>
            new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

        private static TppaQualificationVector Scale(
                TppaQualificationVector value,
                double scale) =>
            new(value.X * scale, value.Y * scale, value.Z * scale);

        private static TppaQualificationVector Cross(
                TppaQualificationVector left,
                TppaQualificationVector right) =>
            new(
                left.Y * right.Z - left.Z * right.Y,
                left.Z * right.X - left.X * right.Z,
                left.X * right.Y - left.Y * right.X);

        private static double Dot(
                TppaQualificationVector left,
                TppaQualificationVector right) =>
            left.X * right.X + left.Y * right.Y + left.Z * right.Z;

        private static double Length(TppaQualificationVector value) =>
            Math.Sqrt(Dot(value, value));

        private static TppaQualificationVector Normalize(
                TppaQualificationVector value) {
            var length = Length(value);
            if (!double.IsFinite(length) || !(length > 0)) {
                return new(double.NaN, double.NaN, double.NaN);
            }
            return Scale(value, 1.0 / length);
        }

        private static bool IsUnitVector(TppaQualificationVector value) {
            if (value == null
                    || !double.IsFinite(value.X)
                    || !double.IsFinite(value.Y)
                    || !double.IsFinite(value.Z)) {
                return false;
            }
            var length = Length(value);
            return double.IsFinite(length) && Math.Abs(length - 1.0) <= 1e-6;
        }

        private static bool IsKnownPierSide(string value) =>
            value == "pierEast" || value == "pierWest";

        private static bool IsUtc(DateTime value) => value.Kind == DateTimeKind.Utc;

        private static bool IsSha256(string value) =>
            value?.Length == 64
            && value.All(character =>
                (character >= '0' && character <= '9')
                || (character >= 'a' && character <= 'f'));

        private static double DegreesToRadians(double degrees) =>
            degrees * Math.PI / 180.0;

        private readonly record struct Geometry(
            double MinimumPairwiseSeparationDegrees,
            double MaximumPairwiseSeparationDegrees,
            double DoubledChordTriangleArea,
            double Side12,
            double Side23,
            double Side13) {
            public bool IsFinite =>
                double.IsFinite(MinimumPairwiseSeparationDegrees)
                && double.IsFinite(MaximumPairwiseSeparationDegrees)
                && double.IsFinite(DoubledChordTriangleArea)
                && double.IsFinite(Side12)
                && double.IsFinite(Side23)
                && double.IsFinite(Side13);

            public bool IsDegenerate =>
                !IsFinite
                || MaximumPairwiseSeparationDegrees <= 0
                || DoubledChordTriangleArea <= 1e-12;
        }
    }
}
