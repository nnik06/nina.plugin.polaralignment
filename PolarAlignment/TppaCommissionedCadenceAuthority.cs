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
        string RuntimeManifestSha256,
        string CommissioningPolicySha256,
        string HardwareConfigurationId,
        string MechanicalStateSha256,
        string LoadProfileId,
        double MinimumTemperatureC,
        double MaximumTemperatureC,
        double QualifiedSettleSeconds,
        double MaximumFreshDeterminationSeconds,
        string SourceNominationSha256,
        string SourceVectorPairCampaignSha256,
        string SourceNullPairCampaignSha256,
        string SourceTimingCampaignSha256,
        int SourceTransitionCount,
        int SourceNullPairCount,
        int SourceTimingDeterminationCount,
        int SourceNightCount,
        double MaximumVectorSeparationMinutes,
        double NullMaximumSeparationMinutes,
        double CandidateMaximumSeparationMinutes,
        double CandidateToNullMaximumRatio,
        double TimingObservedMaximumSeconds,
        double TimingUpperToleranceSeconds,
        int TimingExcludedSampleCount,
        bool DirectionOrderCoveragePassed,
        bool NoSingleNightDominance,
        bool ZeroFalseStableExits);

    internal static class TppaCommissionedCadenceAuthorityParser {
        private static readonly string[] ExactProperties = {
            "schemaVersion", "authorityId", "commissionedUtc", "validUntilUtc",
            "repositoryHead", "pluginAssemblySha256", "runtimeManifestSha256",
            "commissioningPolicySha256", "hardwareConfigurationId",
            "mechanicalStateId", "loadProfileId", "temperatureC",
            "qualifiedSettleSeconds", "maximumFreshDeterminationSeconds",
            "sourceNominationSha256", "sourceVectorPairCampaignSha256",
            "sourceNullPairCampaignSha256", "sourceTimingCampaignSha256",
            "sourceTransitionCount", "sourceNullPairCount",
            "sourceTimingDeterminationCount", "sourceNightCount",
            "maximumVectorSeparationMinutes", "nullMaximumSeparationMinutes",
            "candidateMaximumSeparationMinutes", "candidateToNullMaximumRatio",
            "timingObservedMaximumSeconds", "timingUpperToleranceSeconds",
            "timingExcludedSampleCount", "directionOrderCoveragePassed",
            "noSingleNightDominance", "zeroFalseStableExits"
        };

        internal const string CommissioningPolicy =
            "tppa-cadence-v3|transitions>=20|nullPairs>=10|timings>=59|nights>=2|" +
            "maxVector<=0.5|nullMaximum<=0.25|candidateToNullMaximum<=1.5|" +
            "timingMax+5<=timingUpperTolerance<=75|excluded=0|" +
            "directionOrderCoverage=true|nightDominance<=0.70|falseStableExits=0";

        internal static string CommissioningPolicySha256 =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CommissioningPolicy)))
                .ToLowerInvariant();

        public static TppaCommissionedCadenceAuthority Parse(
                byte[] exactArtifactBytes,
                DateTime nowUtc,
                string loadedPluginAssemblySha256,
                string currentRuntimeManifestSha256,
                string currentHardwareConfigurationId,
                string currentMechanicalStateId,
                string currentLoadProfileId,
                double currentTemperatureC) {
            if (exactArtifactBytes == null || exactArtifactBytes.Length == 0) {
                throw new ArgumentException("Cadence authority artifact is empty.", nameof(exactArtifactBytes));
            }
            RequireUtc(nowUtc, nameof(nowUtc));
            RequireLowerHex(loadedPluginAssemblySha256, 64, nameof(loadedPluginAssemblySha256));
            RequireLowerHex(currentRuntimeManifestSha256, 64, nameof(currentRuntimeManifestSha256));
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
            if (RequireInteger(root, "schemaVersion") != 3) {
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
            var runtimeManifestSha = RequireLowerHex(root, "runtimeManifestSha256", 64);
            if (runtimeManifestSha != currentRuntimeManifestSha256) {
                throw new JsonException("Cadence authority runtime manifest does not match the installed package.");
            }
            var policySha = RequireLowerHex(root, "commissioningPolicySha256", 64);
            if (policySha != CommissioningPolicySha256) {
                throw new JsonException("Cadence authority commissioning policy does not match this plugin build.");
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
            var nullPairs = RequireLowerHex(root, "sourceNullPairCampaignSha256", 64);
            var timing = RequireLowerHex(root, "sourceTimingCampaignSha256", 64);
            var transitions = checked((int)RequireInteger(root, "sourceTransitionCount"));
            var nullPairCount = checked((int)RequireInteger(root, "sourceNullPairCount"));
            var timingCount = checked((int)RequireInteger(root, "sourceTimingDeterminationCount"));
            var nights = checked((int)RequireInteger(root, "sourceNightCount"));
            var maximumSeparation = RequireFinite(root, "maximumVectorSeparationMinutes");
            var nullMaximum = RequireFinite(root, "nullMaximumSeparationMinutes");
            var candidateMaximum = RequireFinite(root, "candidateMaximumSeparationMinutes");
            var candidateToNullMaximum = RequireFinite(root, "candidateToNullMaximumRatio");
            var timingMaximum = RequireFinite(root, "timingObservedMaximumSeconds");
            var timingUpperTolerance = RequireFinite(root, "timingUpperToleranceSeconds");
            var timingExcluded = checked((int)RequireInteger(root, "timingExcludedSampleCount"));
            if (transitions < 20 || nullPairCount < 10 || timingCount < 59 || nights < 2
                    || maximumSeparation < 0.0 || maximumSeparation > 0.5
                    || nullMaximum <= 0.0 || nullMaximum > 0.25
                    || candidateMaximum < 0.0 || candidateMaximum > 0.5
                    || candidateToNullMaximum < 0.0 || candidateToNullMaximum > 1.5
                    || timingMaximum <= settle || timingMaximum > freshDuration
                    || timingUpperTolerance < timingMaximum + TppaFastAlignmentExecutionBudget.ObservedCadenceSlackSeconds
                    || timingUpperTolerance > freshDuration
                    || timingExcluded != 0) {
                throw new JsonException("Cadence authority source campaign does not meet the commissioned evidence floor.");
            }
            if (!RequireTrue(root, "directionOrderCoveragePassed")
                    || !RequireTrue(root, "noSingleNightDominance")) {
                throw new JsonException("Cadence authority source coverage is incomplete.");
            }
            if (!RequireTrue(root, "zeroFalseStableExits")) {
                throw new JsonException("Cadence authority must attest zero false-stable exits.");
            }

            return new(
                Convert.ToHexString(SHA256.HashData(exactArtifactBytes)).ToLowerInvariant(),
                authorityId, commissioned, validUntil, repositoryHead, pluginSha,
                runtimeManifestSha, policySha, hardware, mechanical, loadProfile,
                minimumTemperature, maximumTemperature, settle, freshDuration, nomination,
                vectorPairs, nullPairs, timing, transitions, nullPairCount, timingCount,
                nights, maximumSeparation, nullMaximum, candidateMaximum, candidateToNullMaximum,
                timingMaximum, timingUpperTolerance, timingExcluded, true, true, true);
        }

        private static bool RequireTrue(JObject value, string name) =>
            value[name]?.Type == JTokenType.Boolean && value[name]!.Value<bool>();

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
