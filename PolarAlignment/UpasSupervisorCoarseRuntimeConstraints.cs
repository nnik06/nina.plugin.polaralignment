using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugins.PolarAlignment {
    internal sealed record UpasDirectionalTravelBudget(
        double PositiveDegrees,
        double NegativeDegrees);

    internal sealed record UpasSupervisorCoarseRuntimeConstraints(
        UpasDirectionalTravelBudget Azimuth,
        UpasDirectionalTravelBudget Altitude,
        double CumulativeSessionDegrees,
        Guid? ActiveLeaseId,
        bool PhysicalMotionAvailable,
        bool AtomicBudgetReservationAvailable,
        bool PlanningEvidence,
        bool MotionAuthorityIncluded);

    internal static class UpasSupervisorCoarseRuntimeConstraintsParser {
        public const string RequiredBudgetSignConvention = "adjusterIncreasing";

        private static readonly string[] BudgetProperties = {
            "signConvention", "az", "alt", "cumulativeSessionDegrees"
        };
        private static readonly string[] DirectionalProperties = {
            "positiveDegrees", "negativeDegrees"
        };
        private static readonly string[] CapabilityProperties = {
            "planningEvidence", "physicalMotionAvailable",
            "atomicBudgetReservationAvailable", "motionAuthorityIncluded"
        };

        public static UpasSupervisorCoarseRuntimeConstraints Parse(
                JObject root,
                Guid? expectedCallerLeaseId) {
            if (root == null) {
                throw new ArgumentNullException(nameof(root));
            }
            var budgets = RequireObject(root, "remainingTravelBudgetDegrees");
            RequireExactProperties(budgets, BudgetProperties, "remainingTravelBudgetDegrees");
            if (RequireString(budgets, "signConvention") != RequiredBudgetSignConvention) {
                throw new JsonException("Remaining-travel budget sign convention is unsupported.");
            }
            var azimuth = ParseDirectionalBudget(
                RequireObject(budgets, "az"), "remainingTravelBudgetDegrees.az");
            var altitude = ParseDirectionalBudget(
                RequireObject(budgets, "alt"), "remainingTravelBudgetDegrees.alt");
            var cumulative = RequireNonNegativeFinite(budgets, "cumulativeSessionDegrees");

            var activeLeaseId = RequireNullableCanonicalGuid(root, "activeLeaseId");
            var activeTransactionId = RequireNullableCanonicalGuid(root, "activeTransactionId");
            var lockedReason = RequireNullableString(root, "lockedReason");
            if (lockedReason != null) {
                throw new JsonException("Supervisor snapshot is locked.");
            }
            if (activeTransactionId != null) {
                throw new JsonException("Supervisor snapshot has an active transaction.");
            }
            if (activeLeaseId != expectedCallerLeaseId) {
                throw new JsonException("Supervisor lease does not match the caller lease.");
            }

            var capabilities = RequireObject(root, "capabilities");
            RequireExactProperties(capabilities, CapabilityProperties, "capabilities");
            var planningEvidence = RequireBoolean(capabilities, "planningEvidence");
            var physicalMotionAvailable = RequireBoolean(capabilities, "physicalMotionAvailable");
            var reservationAvailable = RequireBoolean(
                capabilities, "atomicBudgetReservationAvailable");
            var motionAuthorityIncluded = RequireBoolean(
                capabilities, "motionAuthorityIncluded");
            if (!planningEvidence
                    || !physicalMotionAvailable
                    || !reservationAvailable
                    || motionAuthorityIncluded) {
                throw new JsonException(
                    "Supervisor capabilities cannot support non-actuating coarse planning.");
            }

            return new UpasSupervisorCoarseRuntimeConstraints(
                azimuth, altitude, cumulative, activeLeaseId,
                physicalMotionAvailable, reservationAvailable,
                planningEvidence, motionAuthorityIncluded);
        }

        private static UpasDirectionalTravelBudget ParseDirectionalBudget(
                JObject value,
                string name) {
            RequireExactProperties(value, DirectionalProperties, name);
            return new UpasDirectionalTravelBudget(
                RequireNonNegativeFinite(value, "positiveDegrees"),
                RequireNonNegativeFinite(value, "negativeDegrees"));
        }

        private static Guid? RequireNullableCanonicalGuid(JObject value, string name) {
            var token = value[name] ?? throw new JsonException($"{name} is required.");
            if (token.Type == JTokenType.Null) {
                return null;
            }
            if (token.Type != JTokenType.String) {
                throw new JsonException($"{name} must be a canonical UUID or null.");
            }
            var text = token.Value<string>();
            if (!Guid.TryParseExact(text, "D", out var result)
                    || result == Guid.Empty
                    || !string.Equals(text, result.ToString("D"), StringComparison.Ordinal)) {
                throw new JsonException($"{name} must be a lowercase canonical non-nil UUID.");
            }
            return result;
        }

        private static string RequireNullableString(JObject value, string name) {
            var token = value[name] ?? throw new JsonException($"{name} is required.");
            if (token.Type == JTokenType.Null) {
                return null;
            }
            if (token.Type != JTokenType.String || string.IsNullOrWhiteSpace(token.Value<string>())) {
                throw new JsonException($"{name} must be a non-empty string or null.");
            }
            return token.Value<string>();
        }

        private static void RequireExactProperties(
                JObject value,
                IEnumerable<string> expected,
                string name) {
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

        private static string RequireString(JObject value, string name) =>
            value[name]?.Type == JTokenType.String
                && !string.IsNullOrWhiteSpace(value[name].Value<string>())
                ? value[name].Value<string>()
                : throw new JsonException($"{name} must be a non-empty string.");

        private static bool RequireBoolean(JObject value, string name) =>
            value[name]?.Type == JTokenType.Boolean
                ? value[name].Value<bool>()
                : throw new JsonException($"{name} must be a boolean.");

        private static double RequireNonNegativeFinite(JObject value, string name) {
            var token = value[name];
            if (token == null
                    || token.Type != JTokenType.Integer && token.Type != JTokenType.Float) {
                throw new JsonException($"{name} must be numeric.");
            }
            var result = token.Value<double>();
            if (!double.IsFinite(result)
                    || result < 0.0
                    || result == 0.0 && BitConverter.DoubleToInt64Bits(result) < 0) {
                throw new JsonException($"{name} must be finite, non-negative, and not negative zero.");
            }
            return result;
        }
    }
}
