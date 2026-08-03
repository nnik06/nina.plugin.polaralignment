using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NINA.Plugins.PolarAlignment {
    internal sealed record TppaCommissionedCadenceAuthority(
        string ArtifactSha256,
        Guid AuthorityId,
        DateTime CommissionedUtc,
        DateTime ValidUntilUtc,
        string RepositoryHead,
        string PluginAssemblySha256,
        string HardwareConfigurationId,
        string MechanicalStateSha256,
        string LoadProfileId,
        double MinimumTemperatureC,
        double MaximumTemperatureC,
        double QualifiedSettleSeconds,
        double MaximumFreshDeterminationSeconds,
        string SourceNominationSha256,
        string SourceVectorPairCampaignSha256,
        int SourceTransitionCount,
        int SourceNightCount,
        double MaximumVectorSeparationMinutes,
        bool ZeroFalseStableExits);

    internal static class TppaCommissionedCadenceAuthorityParser {
        private static readonly string[] ExactProperties = {
            "schemaVersion", "authorityId", "commissionedUtc", "validUntilUtc",
            "repositoryHead", "pluginAssemblySha256", "hardwareConfigurationId",
            "mechanicalStateId", "loadProfileId", "temperatureC",
            "qualifiedSettleSeconds", "maximumFreshDeterminationSeconds",
            "sourceNominationSha256", "sourceVectorPairCampaignSha256",
            "sourceTransitionCount", "sourceNightCount",
            "maximumVectorSeparationMinutes", "zeroFalseStableExits"
        };

        public static TppaCommissionedCadenceAuthority Parse(
                byte[] exactArtifactBytes,
                DateTime nowUtc,
                string loadedPluginAssemblySha256,
                string currentHardwareConfigurationId,
                string currentMechanicalStateId,
                string currentLoadProfileId,
                double currentTemperatureC) {
            if (exactArtifactBytes == null || exactArtifactBytes.Length == 0) {
                throw new ArgumentException("Cadence authority artifact is empty.", nameof(exactArtifactBytes));
            }
            RequireUtc(nowUtc, nameof(nowUtc));
            RequireLowerHex(loadedPluginAssemblySha256, 64, nameof(loadedPluginAssemblySha256));
            RequireLowerHex(currentHardwareConfigurationId, 64, nameof(currentHardwareConfigurationId));
            RequireLowerHex(currentMechanicalStateId, 64, nameof(currentMechanicalStateId));
            RequireText(currentLoadProfileId, nameof(currentLoadProfileId));
            RequireTemperature(currentTemperatureC, nameof(currentTemperatureC));

            JObject root;
            try {
                using var stream = new System.IO.MemoryStream(exactArtifactBytes, writable: false);
                using var reader = new System.IO.StreamReader(
                    stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
                using var json = new JsonTextReader(reader) { DateParseHandling = DateParseHandling.None };
                root = JObject.Load(json, new JsonLoadSettings {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                });
                if (json.Read()) {
                    throw new JsonException("Cadence authority contains trailing JSON content.");
                }
            } catch (DecoderFallbackException ex) {
                throw new JsonException("Cadence authority is not strict UTF-8.", ex);
            }

            RequireExactProperties(root, ExactProperties);
            if (RequireInteger(root, "schemaVersion") != 1) {
                throw new JsonException("Unsupported cadence authority schema.");
            }
            var authorityIdText = RequireText(root, "authorityId");
            if (!Guid.TryParseExact(authorityIdText, "D", out var authorityId)
                    || authorityId == Guid.Empty || authorityIdText != authorityId.ToString("D")) {
                throw new JsonException("Cadence authority ID is not a canonical UUID.");
            }
            var commissioned = RequireUtc(root, "commissionedUtc");
            var validUntil = RequireUtc(root, "validUntilUtc");
            if (validUntil <= commissioned || nowUtc < commissioned || nowUtc > validUntil) {
                throw new JsonException("Cadence authority is not current.");
            }

            var repositoryHead = RequireLowerHex(root, "repositoryHead", 40);
            var pluginSha = RequireLowerHex(root, "pluginAssemblySha256", 64);
            if (pluginSha != loadedPluginAssemblySha256) {
                throw new JsonException("Cadence authority plugin assembly does not match the loaded DLL.");
            }
            var hardware = RequireLowerHex(root, "hardwareConfigurationId", 64);
            if (hardware != currentHardwareConfigurationId) {
                throw new JsonException("Cadence authority hardware configuration does not match the active TPPA configuration.");
            }
            var mechanical = RequireLowerHex(root, "mechanicalStateId", 64);
            if (mechanical != currentMechanicalStateId) {
                throw new JsonException("Cadence authority mechanical state does not match the active rig epoch.");
            }
            var loadProfile = RequireText(root, "loadProfileId");
            if (loadProfile != currentLoadProfileId) {
                throw new JsonException("Cadence authority load profile does not match the active profile.");
            }

            var temperature = RequireObject(root, "temperatureC");
            RequireExactProperties(temperature, "minimum", "maximum");
            var minimumTemperature = RequireFinite(temperature, "minimum");
            var maximumTemperature = RequireFinite(temperature, "maximum");
            if (minimumTemperature < -100.0 || maximumTemperature > 100.0
                    || minimumTemperature > maximumTemperature
                    || currentTemperatureC < minimumTemperature
                    || currentTemperatureC > maximumTemperature) {
                throw new JsonException("Current temperature exits the commissioned cadence range.");
            }

            var settle = RequireFinite(root, "qualifiedSettleSeconds");
            if (settle < 5.0 || settle >= TppaVerificationSettlePolicy.MinimumQualifiedSettleSeconds) {
                throw new JsonException("Commissioned cadence settle must be at least 5 seconds and below the unconditional 30-second fallback.");
            }
            var freshDuration = RequireFinite(root, "maximumFreshDeterminationSeconds");
            if (freshDuration <= settle
                    || freshDuration > TppaFastAlignmentExecutionBudget.FreshDeterminationReserveSeconds) {
                throw new JsonException("Commissioned fresh-determination duration is outside the qualified range.");
            }
            var nomination = RequireLowerHex(root, "sourceNominationSha256", 64);
            var vectorPairs = RequireLowerHex(root, "sourceVectorPairCampaignSha256", 64);
            var transitions = checked((int)RequireInteger(root, "sourceTransitionCount"));
            var nights = checked((int)RequireInteger(root, "sourceNightCount"));
            var maximumSeparation = RequireFinite(root, "maximumVectorSeparationMinutes");
            if (transitions < 20 || nights < 2 || maximumSeparation < 0.0 || maximumSeparation > 0.5) {
                throw new JsonException("Cadence authority source campaign does not meet the commissioned evidence floor.");
            }
            if (root["zeroFalseStableExits"]?.Type != JTokenType.Boolean
                    || !root["zeroFalseStableExits"]!.Value<bool>()) {
                throw new JsonException("Cadence authority must attest zero false-stable exits.");
            }

            return new(
                Convert.ToHexString(SHA256.HashData(exactArtifactBytes)).ToLowerInvariant(),
                authorityId, commissioned, validUntil, repositoryHead, pluginSha, hardware,
                mechanical, loadProfile, minimumTemperature, maximumTemperature, settle,
                freshDuration, nomination, vectorPairs, transitions, nights, maximumSeparation, true);
        }

        private static void RequireExactProperties(JObject value, params string[] expected) {
            var actual = value.Properties().Select(item => item.Name).OrderBy(item => item, StringComparer.Ordinal);
            var required = expected.OrderBy(item => item, StringComparer.Ordinal);
            if (!actual.SequenceEqual(required, StringComparer.Ordinal)) {
                throw new JsonException("Cadence authority properties do not match the frozen schema.");
            }
        }

        private static JObject RequireObject(JObject value, string name) =>
            value[name] is JObject result ? result : throw new JsonException($"{name} must be an object.");

        private static string RequireText(JObject value, string name) {
            if (value[name]?.Type != JTokenType.String) { throw new JsonException($"{name} must be a string."); }
            var result = value[name]!.Value<string>();
            RequireText(result, name);
            return result;
        }

        private static void RequireText(string value, string name) {
            if (string.IsNullOrWhiteSpace(value)) { throw new JsonException($"{name} must be non-empty."); }
        }

        private static string RequireLowerHex(JObject value, string name, int length) {
            var result = RequireText(value, name);
            RequireLowerHex(result, length, name);
            return result;
        }

        private static void RequireLowerHex(string value, int length, string name) {
            if (value?.Length != length || value.Any(character =>
                    !((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f')))) {
                throw new JsonException($"{name} is not canonical lowercase hexadecimal.");
            }
        }

        private static long RequireInteger(JObject value, string name) {
            if (value[name]?.Type != JTokenType.Integer) { throw new JsonException($"{name} must be an integer."); }
            return value[name]!.Value<long>();
        }

        private static double RequireFinite(JObject value, string name) => RequireFinite(value[name], name);

        private static double RequireFinite(JToken value, string name) {
            if (value?.Type is not (JTokenType.Integer or JTokenType.Float)) {
                throw new JsonException($"{name} must be numeric.");
            }
            var result = value.Value<double>();
            if (!double.IsFinite(result) || (result == 0.0 && BitConverter.DoubleToInt64Bits(result) < 0)) {
                throw new JsonException($"{name} must be finite and not negative zero.");
            }
            return result;
        }

        private static DateTime RequireUtc(JObject value, string name) {
            var text = RequireText(value, name);
            if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var parsed) || parsed.Offset != TimeSpan.Zero) {
                throw new JsonException($"{name} must be UTC.");
            }
            return parsed.UtcDateTime;
        }

        private static void RequireUtc(DateTime value, string name) {
            if (value.Kind != DateTimeKind.Utc) { throw new ArgumentException($"{name} must be UTC.", name); }
        }

        private static void RequireTemperature(double value, string name) {
            if (!double.IsFinite(value) || value < -100.0 || value > 100.0) {
                throw new ArgumentOutOfRangeException(name);
            }
        }
    }
}
