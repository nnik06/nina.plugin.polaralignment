using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;

namespace NINA.Plugins.PolarAlignment {
    internal static class UpasCoarsePlanningSafetyPolicy {
        public const int EvidenceSchemaVersion = 2;
        public const double PhysicalHardLimitDegrees = 5.4;
        public const double MinimumReservedTravelDegrees = 1.0;
        public const string RequiredCoordinateConvention = "azEastPositive_altUpPositive";
    }

    internal static class UpasSupervisorCoarseEvidenceSchemaGate {
        public static JObject ParseSupportedRoot(string json) {
            if (string.IsNullOrWhiteSpace(json)) {
                throw new JsonException("Supervisor coarse-planning evidence response was empty.");
            }

            JObject root;
            try {
                root = JObject.Parse(json);
            } catch (JsonException) {
                throw;
            }

            var schema = root["schemaVersion"];
            if (schema?.Type != JTokenType.Integer
                    || schema.Value<long>() != UpasCoarsePlanningSafetyPolicy.EvidenceSchemaVersion) {
                throw new JsonException(
                    $"Unsupported supervisor coarse-planning evidence schema; exact integer " +
                    $"{UpasCoarsePlanningSafetyPolicy.EvidenceSchemaVersion} is required.");
            }
            return root;
        }

        public static void ValidateCompiledSafetyCrossChecks(
                double negativeHardLimitDegrees,
                double positiveHardLimitDegrees,
                double operationalReserveDegrees) {
            if (!double.IsFinite(negativeHardLimitDegrees)
                    || !double.IsFinite(positiveHardLimitDegrees)
                    || !double.IsFinite(operationalReserveDegrees)
                    || IsNegativeZero(negativeHardLimitDegrees)
                    || IsNegativeZero(positiveHardLimitDegrees)
                    || IsNegativeZero(operationalReserveDegrees)
                    || negativeHardLimitDegrees != -UpasCoarsePlanningSafetyPolicy.PhysicalHardLimitDegrees
                    || positiveHardLimitDegrees != UpasCoarsePlanningSafetyPolicy.PhysicalHardLimitDegrees
                    || operationalReserveDegrees != UpasCoarsePlanningSafetyPolicy.MinimumReservedTravelDegrees) {
                throw new JsonException("Supervisor safety limits do not exactly cross-check compiled TPPA policy.");
            }
        }

        private static bool IsNegativeZero(double value) =>
            value == 0.0 && BitConverter.DoubleToInt64Bits(value) < 0;
    }
}
