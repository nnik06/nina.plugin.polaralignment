using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace NINA.Plugins.PolarAlignment {
    internal sealed record TppaCoarseSourceSolveEvidence(
        Guid SolveId,
        string SourceImageSha256,
        string SolverOutputSha256,
        DateTime ExposureStartedUtc,
        int ExposureDurationMilliseconds,
        DateTime ObservationMidpointUtc,
        DateTime MountSettledUtc,
        bool TrackingEnabled,
        bool Slewing,
        double SolvedRightAscensionDegrees,
        double SolvedDeclinationDegrees,
        string PierSide);

    internal static class TppaCoarseDeterminationReceiptBuilder {
        internal static JObject Build(
                TppaCoarseDeterminationEvidence value,
                DateTime requestUtc) {
            Validate(value, requestUtc);
            var receipt = new JObject {
                ["schemaVersion"] = 1,
                ["receiptSha256"] = new string('0', 64),
                ["determinationId"] = value.DeterminationId.ToString("D"),
                ["startedUtc"] = Utc(value.StartedUtc),
                ["completedUtc"] = Utc(value.CompletedUtc),
                ["targetSkyArcId"] = value.TargetSkyArcId,
                ["truePoleRefractionEnabled"] = value.TruePoleRefractionEnabled,
                ["stationary"] = value.Stationary,
                ["repositoryHead"] = value.RepositoryHead,
                ["pluginAssemblySha256"] = value.PluginAssemblySha256,
                ["hardwareConfigurationId"] = value.HardwareConfigurationId,
                ["mechanicalStateSha256"] = value.MechanicalStateSha256,
                ["solverIdentity"] = value.SolverIdentity,
                ["catalogIdentity"] = value.CatalogIdentity,
                ["sourceSolves"] = new JArray(value.SourceSolves.Select(BuildSolve)),
                ["azimuthErrorMicrodegrees"] = Scaled(
                    value.AzimuthErrorMinutes / 60.0, 1_000_000.0, "azimuth error"),
                ["altitudeErrorMicrodegrees"] = Scaled(
                    value.AltitudeErrorMinutes / 60.0, 1_000_000.0, "altitude error"),
                ["covarianceAzAzSquareMicrodegrees"] = Covariance(
                    value.CovarianceAzAzSquareMinutes, "azimuth covariance"),
                ["covarianceAzAltSquareMicrodegrees"] = Covariance(
                    value.CovarianceAzAltSquareMinutes, "cross covariance"),
                ["covarianceAltAltSquareMicrodegrees"] = Covariance(
                    value.CovarianceAltAltSquareMinutes, "altitude covariance")
            };
            receipt["receiptSha256"] =
                HttpsUpasSupervisorCoarseTppaExecutor.ComputeRequestBodySha256(receipt);
            return receipt;
        }

        internal static void ValidateIndependent(
                TppaCoarseDeterminationEvidence first,
                TppaCoarseDeterminationEvidence second) {
            if (first.DeterminationId == second.DeterminationId) {
                throw new ArgumentException("Coarse TPPA determinations must be independent.");
            }
            if (first.CompletedUtc > second.StartedUtc
                    && second.CompletedUtc > first.StartedUtc) {
                throw new ArgumentException("Coarse TPPA determination intervals overlap.");
            }
            RequireEqual(first.TargetSkyArcId, second.TargetSkyArcId, "sky arc");
            RequireEqual(first.RepositoryHead, second.RepositoryHead, "repository");
            RequireEqual(first.PluginAssemblySha256, second.PluginAssemblySha256, "plugin");
            RequireEqual(first.HardwareConfigurationId, second.HardwareConfigurationId, "hardware");
            RequireEqual(first.MechanicalStateSha256, second.MechanicalStateSha256, "mechanical state");
            RequireEqual(first.SolverIdentity, second.SolverIdentity, "solver");
            RequireEqual(first.CatalogIdentity, second.CatalogIdentity, "catalog");
            var images = first.SourceSolves.Concat(second.SourceSolves)
                .Select(item => item.SourceImageSha256).ToArray();
            var outputs = first.SourceSolves.Concat(second.SourceSolves)
                .Select(item => item.SolverOutputSha256).ToArray();
            if (images.Distinct(StringComparer.Ordinal).Count() != images.Length
                    || outputs.Distinct(StringComparer.Ordinal).Count() != outputs.Length) {
                throw new ArgumentException(
                    "Coarse TPPA determinations reuse source images or solver outputs.");
            }
        }

        private static JObject BuildSolve(TppaCoarseSourceSolveEvidence value) => new() {
            ["solveId"] = value.SolveId.ToString("D"),
            ["sourceImageSha256"] = value.SourceImageSha256,
            ["solverOutputSha256"] = value.SolverOutputSha256,
            ["exposureStartedUtc"] = Utc(value.ExposureStartedUtc),
            ["exposureDurationMilliseconds"] = value.ExposureDurationMilliseconds,
            ["observationMidpointUtc"] = Utc(value.ObservationMidpointUtc),
            ["mountSettledUtc"] = Utc(value.MountSettledUtc),
            ["trackingEnabled"] = value.TrackingEnabled,
            ["slewing"] = value.Slewing,
            ["solvedRightAscensionMicrodegrees"] = Scaled(
                value.SolvedRightAscensionDegrees, 1_000_000.0, "right ascension"),
            ["solvedDeclinationMicrodegrees"] = Scaled(
                value.SolvedDeclinationDegrees, 1_000_000.0, "declination"),
            ["pierSide"] = value.PierSide
        };

        private static void Validate(TppaCoarseDeterminationEvidence value, DateTime nowUtc) {
            if (value == null || value.DeterminationId == Guid.Empty) {
                throw new ArgumentException("Coarse TPPA determination identity is required.");
            }
            RequireUtc(nowUtc, "request time");
            RequireUtc(value.StartedUtc, "determination start");
            RequireUtc(value.CompletedUtc, "determination completion");
            if (value.CompletedUtc <= value.StartedUtc
                    || value.CompletedUtc > nowUtc
                    || (nowUtc - value.CompletedUtc).TotalSeconds > 180.0) {
                throw new ArgumentException("Coarse TPPA determination is stale or mistimed.");
            }
            if (!value.TruePoleRefractionEnabled || !value.Stationary) {
                throw new ArgumentException("Coarse TPPA requires true-pole stationary evidence.");
            }
            RequireText(value.TargetSkyArcId, "sky arc");
            RequireLowerHex(value.RepositoryHead, 40, "repository head");
            RequireLowerHex(value.PluginAssemblySha256, 64, "plugin assembly");
            RequireText(value.HardwareConfigurationId, "hardware configuration");
            RequireLowerHex(value.MechanicalStateSha256, 64, "mechanical state");
            RequireText(value.SolverIdentity, "solver identity");
            RequireText(value.CatalogIdentity, "catalog identity");
            if (value.SourceSolves?.Count != 3) {
                throw new ArgumentException("Exactly three source solves are required.");
            }
            var solves = value.SourceSolves.ToArray();
            if (solves.Any(item => item == null || item.SolveId == Guid.Empty)
                    || solves.Select(item => item.SolveId).Distinct().Count() != 3
                    || solves.Select(item => item.SourceImageSha256).Distinct().Count() != 3
                    || solves.Select(item => item.SolverOutputSha256).Distinct().Count() != 3
                    || solves.Select(item => item.PierSide).Distinct().Count() != 1) {
                throw new ArgumentException("Source solve identities are incomplete or aliased.");
            }
            DateTime? previousEnd = null;
            foreach (var solve in solves) {
                RequireLowerHex(solve.SourceImageSha256, 64, "source image");
                RequireLowerHex(solve.SolverOutputSha256, 64, "solver output");
                RequireUtc(solve.ExposureStartedUtc, "exposure start");
                RequireUtc(solve.ObservationMidpointUtc, "observation midpoint");
                RequireUtc(solve.MountSettledUtc, "mount settle");
                if (solve.ExposureDurationMilliseconds <= 0
                        || solve.ExposureDurationMilliseconds > 120_000
                        || !solve.TrackingEnabled || solve.Slewing
                        || solve.MountSettledUtc > solve.ExposureStartedUtc
                        || solve.ExposureStartedUtc < value.StartedUtc) {
                    throw new ArgumentException("Source solve is moving, untracked, or mistimed.");
                }
                var end = solve.ExposureStartedUtc
                    + TimeSpan.FromMilliseconds(solve.ExposureDurationMilliseconds);
                var midpoint = solve.ExposureStartedUtc
                    + TimeSpan.FromMilliseconds(solve.ExposureDurationMilliseconds / 2.0);
                if (end > value.CompletedUtc
                        || Math.Abs((solve.ObservationMidpointUtc - midpoint)
                            .TotalMilliseconds) > 250.0
                        || (previousEnd.HasValue && solve.ExposureStartedUtc < previousEnd.Value)) {
                    throw new ArgumentException("Source solve exposure intervals are invalid.");
                }
                if (!double.IsFinite(solve.SolvedRightAscensionDegrees)
                        || solve.SolvedRightAscensionDegrees < 0.0
                        || solve.SolvedRightAscensionDegrees >= 360.0
                        || !double.IsFinite(solve.SolvedDeclinationDegrees)
                        || solve.SolvedDeclinationDegrees < -90.0
                        || solve.SolvedDeclinationDegrees > 90.0
                        || (solve.PierSide != "pierEast" && solve.PierSide != "pierWest")) {
                    throw new ArgumentException("Source solve coordinates or pier side are invalid.");
                }
                previousEnd = end;
            }
            var values = new[] {
                value.AzimuthErrorMinutes,
                value.AltitudeErrorMinutes,
                value.CovarianceAzAzSquareMinutes,
                value.CovarianceAzAltSquareMinutes,
                value.CovarianceAltAltSquareMinutes
            };
            if (values.Any(item => !double.IsFinite(item))
                    || Math.Sqrt(value.AzimuthErrorMinutes * value.AzimuthErrorMinutes
                        + value.AltitudeErrorMinutes * value.AltitudeErrorMinutes) > 300.0
                    || value.CovarianceAzAzSquareMinutes < 0.0
                    || value.CovarianceAltAltSquareMinutes < 0.0
                    || value.CovarianceAzAzSquareMinutes * value.CovarianceAltAltSquareMinutes
                        - value.CovarianceAzAltSquareMinutes * value.CovarianceAzAltSquareMinutes < 0.0) {
                throw new ArgumentException("TPPA result or covariance is invalid.");
            }
        }

        private static void RequireEqual(string first, string second, string label) {
            if (!string.Equals(first, second, StringComparison.Ordinal)) {
                throw new ArgumentException($"Coarse TPPA {label} identities differ.");
            }
        }

        private static void RequireText(string value, string label) {
            if (string.IsNullOrWhiteSpace(value)) {
                throw new ArgumentException($"Coarse TPPA {label} is required.");
            }
        }

        private static void RequireLowerHex(string value, int length, string label) {
            if (value?.Length != length || value.Any(character =>
                    !((character >= '0' && character <= '9')
                        || (character >= 'a' && character <= 'f')))) {
                throw new ArgumentException($"Coarse TPPA {label} is not canonical hexadecimal.");
            }
        }

        private static DateTime RequireUtc(DateTime value, string label) {
            if (value.Kind != DateTimeKind.Utc) {
                throw new ArgumentException($"Coarse TPPA {label} must be UTC.");
            }
            return value;
        }

        private static string Utc(DateTime value) =>
            RequireUtc(value, "timestamp").ToString("O", CultureInfo.InvariantCulture);

        private static long Covariance(double squareMinutes, string label) =>
            Scaled(squareMinutes / 3600.0, 1_000_000_000_000.0, label);

        private static long Scaled(double value, double scale, string label) {
            if (!double.IsFinite(value)) {
                throw new ArgumentOutOfRangeException(label);
            }
            return checked((long)Math.Round(value * scale, MidpointRounding.AwayFromZero));
        }
    }
}
