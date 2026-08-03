using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NINA.Plugins.PolarAlignment {
    internal sealed record UpasResponseMatrix(
        double AzErrorPerAzDelta,
        double AzErrorPerAltDelta,
        double AltErrorPerAzDelta,
        double AltErrorPerAltDelta,
        double ConditionNumber);

    internal sealed record UpasDirectionalBounds(
        double PositiveDegrees,
        double NegativeDegrees) {
        public double WorstDegrees => Math.Max(PositiveDegrees, NegativeDegrees);
    }

    internal sealed record UpasSupervisorCoarseResponseCalibration(
        Guid CalibrationId,
        string ArtifactSha256,
        DateTimeOffset CapturedUtc,
        string CoordinateConvention,
        UpasResponseMatrix ResponseMatrix,
        double[,] MatrixElementCovariance,
        double FixedAzimuthCommandUncertaintyDegrees,
        double FixedAltitudeCommandUncertaintyDegrees,
        UpasDirectionalBounds AzimuthDeadbandDegrees,
        UpasDirectionalBounds AltitudeDeadbandDegrees,
        double MinimumAzimuthPositionDegrees,
        double MaximumAzimuthPositionDegrees,
        double MinimumAltitudePositionDegrees,
        double MaximumAltitudePositionDegrees,
        double MinimumTemperatureC,
        double MaximumTemperatureC,
        string LoadProfileId,
        double AgeAtResponseMilliseconds,
        double ValidForMilliseconds,
        Guid SupervisorBootId);

    internal static class UpasSupervisorCoarseResponseCalibrationParser {
        public const int CurrentSchemaVersion = 2;
        public const double MaximumConditionNumber = 10.0;
        public const string RequiredEquation =
            "tppaErrorAfter=tppaErrorBefore+R*physicalDelta";
        public const string RequiredUnits = "degrees";
        private const double MatrixTolerance = 1e-12;

        private static readonly string[] Properties = {
            "schemaVersion", "calibrationId", "artifactSha256", "equation", "units",
            "capturedUtc", "coordinateConvention", "responseMatrix", "matrixElementCovariance",
            "fixedCommandUncertaintyDegrees", "deadbandDegrees",
            "applicablePositionDegrees", "applicableDirections", "temperatureC",
            "loadProfileId", "ageAtResponseMilliseconds", "validForMilliseconds",
            "supervisorBootId"
        };
        private static readonly string[] AxisPairProperties = { "az", "alt" };
        private static readonly string[] DirectionProperties = { "positive", "negative" };
        private static readonly string[] MatrixProperties = { "coordinateConvention", "values" };
        private static readonly string[] RangeProperties = { "minimum", "maximum" };

        public static UpasSupervisorCoarseResponseCalibration Parse(
                JObject value,
                UpasSupervisorCoarseEvidenceEnvelope envelope,
                UpasSupervisorAxesEvidence axes,
                double currentTemperatureC,
                string currentLoadProfileId) {
            if (value == null) {
                throw new ArgumentNullException(nameof(value));
            }
            if (envelope == null) {
                throw new ArgumentNullException(nameof(envelope));
            }
            if (axes == null) {
                throw new ArgumentNullException(nameof(axes));
            }
            RequireFinite(currentTemperatureC, nameof(currentTemperatureC));
            if (string.IsNullOrWhiteSpace(currentLoadProfileId)) {
                throw new ArgumentException("Current load profile is required.", nameof(currentLoadProfileId));
            }
            RequireExactProperties(value, Properties, "responseCalibration");
            if (RequireInteger(value, "schemaVersion") != CurrentSchemaVersion) {
                throw new JsonException("Unsupported response-calibration schema.");
            }

            var calibrationId = RequireGuid(value, "calibrationId");
            var artifact = RequireLowerHexSha256(value, "artifactSha256");
            var capturedUtc = RequireUtcTimestamp(value, "capturedUtc");
            if (RequireString(value, "equation") != RequiredEquation) {
                throw new JsonException("Response calibration equation is unsupported.");
            }
            if (RequireString(value, "units") != RequiredUnits) {
                throw new JsonException("Response calibration units are unsupported.");
            }
            var convention = RequireString(value, "coordinateConvention");
            if (convention != envelope.CoordinateConvention
                    || convention != UpasCoarsePlanningSafetyPolicy.RequiredCoordinateConvention) {
                throw new JsonException("Response calibration coordinate convention does not match evidence.");
            }

            var matrixObject = RequireObject(value, "responseMatrix");
            RequireExactProperties(matrixObject, MatrixProperties, "responseMatrix");
            var matrixConvention = RequireString(matrixObject, "coordinateConvention");
            if (matrixConvention != convention) {
                throw new JsonException(
                    "Response-matrix coordinate convention does not match calibration evidence.");
            }
            var matrixValues = ParseMatrix(
                matrixObject["values"], 2, "responseMatrix.values");
            var conditionNumber = CalculateConditionNumber(matrixValues);
            if (!double.IsFinite(conditionNumber) || conditionNumber > MaximumConditionNumber) {
                throw new JsonException(
                    $"Response matrix condition number {conditionNumber:F3} exceeds " +
                    $"the compiled maximum {MaximumConditionNumber:F1}.");
            }
            var matrix = new UpasResponseMatrix(
                matrixValues[0, 0], matrixValues[0, 1],
                matrixValues[1, 0], matrixValues[1, 1], conditionNumber);

            var covariance = ParseMatrix(
                value["matrixElementCovariance"], 4, "matrixElementCovariance");
            RequireSymmetricPositiveSemidefinite(covariance, "matrixElementCovariance");

            var fixedUncertainty = RequireAxisPair(
                value, "fixedCommandUncertaintyDegrees", RequireNonNegativeFinite);
            var deadband = RequireObject(value, "deadbandDegrees");
            RequireExactProperties(deadband, AxisPairProperties, "deadbandDegrees");
            var azDeadband = ParseDirectionalBounds(
                RequireObject(deadband, "az"), "deadbandDegrees.az");
            var altDeadband = ParseDirectionalBounds(
                RequireObject(deadband, "alt"), "deadbandDegrees.alt");

            var positions = RequireObject(value, "applicablePositionDegrees");
            RequireExactProperties(positions, AxisPairProperties, "applicablePositionDegrees");
            var azRange = ParseRange(RequireObject(positions, "az"), "applicablePositionDegrees.az");
            var altRange = ParseRange(RequireObject(positions, "alt"), "applicablePositionDegrees.alt");
            RequireRangeCoversAxis(azRange, axes.Azimuth, "azimuth");
            RequireRangeCoversAxis(altRange, axes.Altitude, "altitude");

            var directions = RequireObject(value, "applicableDirections");
            RequireExactProperties(directions, AxisPairProperties, "applicableDirections");
            RequireBothDirections(directions["az"], "applicableDirections.az");
            RequireBothDirections(directions["alt"], "applicableDirections.alt");

            var temperatureRange = ParseRange(
                RequireObject(value, "temperatureC"), "temperatureC");
            if (currentTemperatureC < temperatureRange.Minimum
                    || currentTemperatureC > temperatureRange.Maximum) {
                throw new JsonException("Current temperature is outside response-calibration applicability.");
            }
            var loadProfileId = RequireString(value, "loadProfileId");
            if (!string.Equals(loadProfileId, currentLoadProfileId, StringComparison.Ordinal)) {
                throw new JsonException("Current load profile does not match response calibration.");
            }

            var age = RequireNonNegativeFinite(value, "ageAtResponseMilliseconds");
            var validFor = RequirePositiveFinite(value, "validForMilliseconds");
            if (age > envelope.OldestEvidenceAgeMilliseconds
                    || validFor > envelope.ValidForMilliseconds
                    || age + envelope.ObservedRoundTripMilliseconds > validFor) {
                throw new JsonException("Response calibration is stale or exceeds envelope validity.");
            }
            var bootId = RequireGuid(value, "supervisorBootId");
            if (bootId != envelope.SupervisorBootId) {
                throw new JsonException("Response calibration belongs to a different supervisor boot.");
            }

            return new UpasSupervisorCoarseResponseCalibration(
                calibrationId, artifact, capturedUtc, convention, matrix, covariance,
                fixedUncertainty.Az, fixedUncertainty.Alt,
                azDeadband, altDeadband,
                azRange.Minimum, azRange.Maximum, altRange.Minimum, altRange.Maximum,
                temperatureRange.Minimum, temperatureRange.Maximum,
                loadProfileId, age, validFor, bootId);
        }

        private static double CalculateConditionNumber(double[,] matrix) {
            var a = matrix[0, 0];
            var b = matrix[0, 1];
            var c = matrix[1, 0];
            var d = matrix[1, 1];
            var trace = a * a + b * b + c * c + d * d;
            var determinant = a * d - b * c;
            var discriminant = Math.Max(0.0, trace * trace - 4.0 * determinant * determinant);
            var lambdaMaximum = 0.5 * (trace + Math.Sqrt(discriminant));
            var lambdaMinimum = 0.5 * (trace - Math.Sqrt(discriminant));
            if (lambdaMinimum <= MatrixTolerance || lambdaMaximum <= 0.0) {
                return double.PositiveInfinity;
            }
            return Math.Sqrt(lambdaMaximum / lambdaMinimum);
        }

        private static void RequireSymmetricPositiveSemidefinite(double[,] matrix, string name) {
            var size = matrix.GetLength(0);
            for (var row = 0; row < size; row++) {
                for (var column = row + 1; column < size; column++) {
                    if (Math.Abs(matrix[row, column] - matrix[column, row]) > MatrixTolerance) {
                        throw new JsonException($"{name} must be symmetric.");
                    }
                }
            }

            var factor = new double[size, size];
            for (var row = 0; row < size; row++) {
                for (var column = 0; column <= row; column++) {
                    var residual = matrix[row, column];
                    for (var index = 0; index < column; index++) {
                        residual -= factor[row, index] * factor[column, index];
                    }
                    if (row == column) {
                        if (residual < -MatrixTolerance) {
                            throw new JsonException($"{name} must be positive semidefinite.");
                        }
                        factor[row, column] = Math.Sqrt(Math.Max(0.0, residual));
                    } else if (factor[column, column] > MatrixTolerance) {
                        factor[row, column] = residual / factor[column, column];
                    } else if (Math.Abs(residual) > MatrixTolerance) {
                        throw new JsonException($"{name} must be positive semidefinite.");
                    }
                }
            }
        }

        private static double[,] ParseMatrix(JToken token, int size, string name) {
            if (token is not JArray rows || rows.Count != size
                    || rows.Any(row => row is not JArray columns || columns.Count != size)) {
                throw new JsonException($"{name} must be a {size}x{size} array.");
            }
            var result = new double[size, size];
            for (var row = 0; row < size; row++) {
                for (var column = 0; column < size; column++) {
                    result[row, column] = RequireFinite(rows[row][column], $"{name}[{row},{column}]");
                }
            }
            return result;
        }

        private static (double Az, double Alt) RequireAxisPair(
                JObject parent,
                string name,
                Func<JObject, string, double> parser) {
            var pair = RequireObject(parent, name);
            RequireExactProperties(pair, AxisPairProperties, name);
            return (parser(pair, "az"), parser(pair, "alt"));
        }

        private static UpasDirectionalBounds ParseDirectionalBounds(JObject value, string name) {
            RequireExactProperties(value, DirectionProperties, name);
            return new(
                RequireNonNegativeFinite(value, "positive"),
                RequireNonNegativeFinite(value, "negative"));
        }

        private static (double Minimum, double Maximum) ParseRange(JObject value, string name) {
            RequireExactProperties(value, RangeProperties, name);
            var minimum = RequireFinite(value, "minimum");
            var maximum = RequireFinite(value, "maximum");
            if (minimum >= maximum) {
                throw new JsonException($"{name} must have minimum less than maximum.");
            }
            return (minimum, maximum);
        }

        private static void RequireRangeCoversAxis(
                (double Minimum, double Maximum) range,
                UpasSupervisorAxisEvidence axis,
                string name) {
            var lower = axis.PositionDegrees - 3.0 * axis.PositionStandardUncertaintyDegrees;
            var upper = axis.PositionDegrees + 3.0 * axis.PositionStandardUncertaintyDegrees;
            if (lower < range.Minimum || upper > range.Maximum) {
                throw new JsonException($"{name} position uncertainty is outside calibration applicability.");
            }
        }

        private static void RequireBothDirections(JToken token, string name) {
            if (token is not JArray array
                    || array.Count != 2
                    || array.Any(item => item.Type != JTokenType.String)
                    || !array.Select(item => item.Value<string>()).ToHashSet(StringComparer.Ordinal)
                        .SetEquals(new[] { "positive", "negative" })) {
                throw new JsonException($"{name} must contain positive and negative exactly once.");
            }
        }

        private static void RequireExactProperties(
                JObject value, IEnumerable<string> expected, string name) {
            var actual = value.Properties().Select(property => property.Name)
                .OrderBy(item => item, StringComparer.Ordinal);
            var required = expected.OrderBy(item => item, StringComparer.Ordinal);
            if (!actual.SequenceEqual(required, StringComparer.Ordinal)) {
                throw new JsonException($"{name} properties do not match the frozen V2 contract.");
            }
        }

        private static JObject RequireObject(JObject value, string name) =>
            value[name] is JObject result
                ? result
                : throw new JsonException($"{name} must be an object.");

        private static int RequireInteger(JObject value, string name) =>
            value[name]?.Type == JTokenType.Integer
                ? value[name].Value<int>()
                : throw new JsonException($"{name} must be an integer.");

        private static string RequireString(JObject value, string name) =>
            value[name]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace(value[name].Value<string>())
                ? value[name].Value<string>()
                : throw new JsonException($"{name} must be a non-empty string.");

        private static Guid RequireGuid(JObject value, string name) {
            if (!Guid.TryParse(RequireString(value, name), out var result) || result == Guid.Empty) {
                throw new JsonException($"{name} must be a non-empty UUID.");
            }
            return result;
        }

        private static DateTimeOffset RequireUtcTimestamp(JObject value, string name) {
            var text = RequireString(value, name);
            if (!DateTimeOffset.TryParseExact(
                    text,
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var result)
                    || result.Offset != TimeSpan.Zero
                    || !text.EndsWith("Z", StringComparison.Ordinal)) {
                throw new JsonException($"{name} must be an ISO-8601 UTC timestamp.");
            }
            return result;
        }

        private static string RequireLowerHexSha256(JObject value, string name) {
            var result = RequireString(value, name);
            if (result.Length != 64
                    || result.Any(character => !((character >= '0' && character <= '9')
                        || (character >= 'a' && character <= 'f')))) {
                throw new JsonException($"{name} must be exactly 64 lowercase hexadecimal characters.");
            }
            return result;
        }

        private static double RequireFinite(JObject value, string name) =>
            RequireFinite(value[name], name);

        private static double RequireFinite(JToken token, string name) {
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)) {
                throw new JsonException($"{name} must be numeric.");
            }
            var result = token.Value<double>();
            if (!double.IsFinite(result) || IsNegativeZero(result)) {
                throw new JsonException($"{name} must be finite and not negative zero.");
            }
            return result;
        }

        private static double RequireNonNegativeFinite(JObject value, string name) {
            var result = RequireFinite(value, name);
            if (result < 0.0) {
                throw new JsonException($"{name} must be non-negative.");
            }
            return result;
        }

        private static double RequirePositiveFinite(JObject value, string name) {
            var result = RequireFinite(value, name);
            if (result <= 0.0) {
                throw new JsonException($"{name} must be positive.");
            }
            return result;
        }

        private static void RequireFinite(double value, string name) {
            if (!double.IsFinite(value) || IsNegativeZero(value)) {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        private static bool IsNegativeZero(double value) =>
            value == 0.0 && BitConverter.DoubleToInt64Bits(value) < 0;
    }
}
