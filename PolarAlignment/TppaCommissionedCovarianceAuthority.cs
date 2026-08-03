using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NINA.Plugins.PolarAlignment {
    internal sealed record TppaCommissionedCovarianceAuthority(
        string ArtifactSha256,
        Guid AuthorityId,
        DateTime CommissionedUtc,
        DateTime ValidUntilUtc,
        string RepositoryHead,
        string PluginAssemblySha256,
        string HardwareConfigurationId,
        string MechanicalStateSha256,
        string LoadProfileId,
        string SolverIdentity,
        string CatalogIdentity,
        string TargetSkyArcId,
        double MinimumTemperatureC,
        double MaximumTemperatureC,
        double CovarianceAzAzSquareMinutes,
        double CovarianceAzAltSquareMinutes,
        double CovarianceAltAltSquareMinutes,
        string SourceCampaignManifestSha256,
        int SourceAttemptCount,
        int SourcePassCount,
        double ConfidenceLevel);

    internal static class TppaCommissionedCovarianceAuthorityParser {
        private static readonly string[] ExactProperties = {
            "schemaVersion", "authorityId", "commissionedUtc", "validUntilUtc",
            "repositoryHead", "pluginAssemblySha256", "hardwareConfigurationId",
            "mechanicalStateId", "loadProfileId", "solverIdentity", "catalogIdentity",
            "targetSkyArcId", "temperatureC", "covarianceFloorSquareDegrees",
            "sharedSystematicIncluded", "sourceCampaignManifestSha256",
            "sourceAttemptCount", "sourcePassCount", "confidenceLevel"
        };

        public static TppaCommissionedCovarianceAuthority Parse(
                byte[] exactArtifactBytes,
                DateTime nowUtc,
                string loadedPluginAssemblySha256,
                string currentHardwareConfigurationId,
                string currentMechanicalStateId,
                string currentLoadProfileId,
                double currentTemperatureC) {
            if (exactArtifactBytes == null || exactArtifactBytes.Length == 0) {
                throw new ArgumentException(
                    "Covariance authority artifact is empty.",
                    nameof(exactArtifactBytes));
            }
            RequireUtc(nowUtc, nameof(nowUtc));
            RequireSha256(loadedPluginAssemblySha256, nameof(loadedPluginAssemblySha256));
            RequireSha256(currentHardwareConfigurationId, nameof(currentHardwareConfigurationId));
            RequireSha256(currentMechanicalStateId, nameof(currentMechanicalStateId));
            RequireText(currentLoadProfileId, nameof(currentLoadProfileId));
            RequireTemperature(currentTemperatureC, nameof(currentTemperatureC));

            JObject root;
            try {
                using var stream = new System.IO.MemoryStream(
                    exactArtifactBytes, writable: false);
                using var reader = new System.IO.StreamReader(
                    stream, new System.Text.UTF8Encoding(false, true),
                    detectEncodingFromByteOrderMarks: false);
                using var json = new JsonTextReader(reader) {
                    DateParseHandling = DateParseHandling.None
                };
                root = JObject.Load(json, new JsonLoadSettings {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                });
                if (json.Read()) {
                    throw new JsonException(
                        "Covariance authority contains trailing JSON content.");
                }
            } catch (DecoderFallbackException ex) {
                throw new JsonException(
                    "Covariance authority is not strict UTF-8.", ex);
            }
            RequireExactProperties(root, ExactProperties);
            if (RequireInteger(root, "schemaVersion") != 1) {
                throw new JsonException("Unsupported covariance authority schema.");
            }
            var authorityIdText = RequireText(root, "authorityId");
            if (!Guid.TryParseExact(authorityIdText, "D", out var authorityId)
                    || authorityId == Guid.Empty
                    || authorityIdText != authorityId.ToString("D")) {
                throw new JsonException(
                    "Covariance authority ID is not a canonical UUID.");
            }
            var commissioned = RequireUtc(root, "commissionedUtc");
            var validUntil = RequireUtc(root, "validUntilUtc");
            if (validUntil <= commissioned
                    || nowUtc < commissioned || nowUtc > validUntil) {
                throw new JsonException(
                    "Covariance authority is not current.");
            }

            var repositoryHead = RequireLowerHex(root, "repositoryHead", 40);
            var pluginSha = RequireLowerHex(root, "pluginAssemblySha256", 64);
            if (pluginSha != loadedPluginAssemblySha256) {
                throw new JsonException(
                    "Covariance authority plugin assembly does not match the loaded DLL.");
            }
            var hardware = RequireLowerHex(root, "hardwareConfigurationId", 64);
            if (hardware != currentHardwareConfigurationId) {
                throw new JsonException(
                    "Covariance authority hardware configuration does not match the active TPPA configuration.");
            }
            var mechanical = RequireLowerHex(root, "mechanicalStateId", 64);
            if (mechanical != currentMechanicalStateId) {
                throw new JsonException(
                    "Covariance authority mechanical state does not match the active rig epoch.");
            }
            var loadProfile = RequireText(root, "loadProfileId");
            if (loadProfile != currentLoadProfileId) {
                throw new JsonException(
                    "Covariance authority load profile does not match the active profile.");
            }
            var solver = RequireText(root, "solverIdentity");
            var catalog = RequireText(root, "catalogIdentity");
            var skyArc = RequireText(root, "targetSkyArcId");

            var temperature = RequireObject(root, "temperatureC");
            RequireExactProperties(temperature, "minimum", "maximum");
            var minimumTemperature = RequireFinite(temperature, "minimum");
            var maximumTemperature = RequireFinite(temperature, "maximum");
            if (minimumTemperature < -100.0 || maximumTemperature > 100.0
                    || minimumTemperature > maximumTemperature
                    || currentTemperatureC < minimumTemperature
                    || currentTemperatureC > maximumTemperature) {
                throw new JsonException(
                    "Current temperature exits the commissioned covariance range.");
            }

            var covariance = RequireCovariance(root["covarianceFloorSquareDegrees"]);
            if (root["sharedSystematicIncluded"]?.Type != JTokenType.Boolean
                    || root["sharedSystematicIncluded"]!.Value<bool>()) {
                throw new JsonException(
                    "Covariance authority must explicitly exclude shared systematic error.");
            }
            var manifest = RequireLowerHex(
                root, "sourceCampaignManifestSha256", 64);
            var attempts = checked((int)RequireInteger(root, "sourceAttemptCount"));
            var passes = checked((int)RequireInteger(root, "sourcePassCount"));
            if (attempts < 20 || passes < 1 || passes > attempts) {
                throw new JsonException(
                    "Covariance authority campaign counts are not qualified.");
            }
            var confidence = RequireFinite(root, "confidenceLevel");
            if (confidence < 0.5 || confidence >= 1.0) {
                throw new JsonException(
                    "Covariance authority confidence is outside [0.5, 1).");
            }

            var artifactSha = Convert.ToHexString(
                SHA256.HashData(exactArtifactBytes)).ToLowerInvariant();
            const double SquareDegreesToSquareMinutes = 3600.0;
            return new(
                artifactSha,
                authorityId,
                commissioned,
                validUntil,
                repositoryHead,
                pluginSha,
                hardware,
                mechanical,
                loadProfile,
                solver,
                catalog,
                skyArc,
                minimumTemperature,
                maximumTemperature,
                covariance.AzAz * SquareDegreesToSquareMinutes,
                covariance.AzAlt * SquareDegreesToSquareMinutes,
                covariance.AltAlt * SquareDegreesToSquareMinutes,
                manifest,
                attempts,
                passes,
                confidence);
        }

        private static (double AzAz, double AzAlt, double AltAlt)
                RequireCovariance(JToken token) {
            if (token is not JArray rows || rows.Count != 2
                    || rows.Any(row => row is not JArray array || array.Count != 2)) {
                throw new JsonException(
                    "Covariance floor must be an exact 2x2 array.");
            }
            var first = (JArray)rows[0];
            var second = (JArray)rows[1];
            var azAz = RequireFinite(first[0], "covariance[0][0]");
            var azAlt = RequireFinite(first[1], "covariance[0][1]");
            var altAz = RequireFinite(second[0], "covariance[1][0]");
            var altAlt = RequireFinite(second[1], "covariance[1][1]");
            if (azAz < 0.0 || altAlt < 0.0 || azAlt != altAz
                    || azAz * altAlt - azAlt * azAlt < -1e-18) {
                throw new JsonException(
                    "Covariance floor must be symmetric positive semidefinite.");
            }
            return (azAz, azAlt, altAlt);
        }

        private static void RequireExactProperties(
                JObject value, params string[] expected) {
            var actual = value.Properties().Select(item => item.Name)
                .OrderBy(item => item, StringComparer.Ordinal);
            var required = expected.OrderBy(item => item, StringComparer.Ordinal);
            if (!actual.SequenceEqual(required, StringComparer.Ordinal)) {
                throw new JsonException(
                    "Covariance authority properties do not match the frozen schema.");
            }
        }

        private static JObject RequireObject(JObject value, string name) =>
            value[name] is JObject result
                ? result
                : throw new JsonException($"{name} must be an object.");

        private static string RequireText(JObject value, string name) {
            if (value[name]?.Type != JTokenType.String) {
                throw new JsonException($"{name} must be a string.");
            }
            var result = value[name]!.Value<string>();
            RequireText(result, name);
            return result;
        }

        private static void RequireText(string value, string name) {
            if (string.IsNullOrWhiteSpace(value)) {
                throw new JsonException($"{name} must be non-empty.");
            }
        }

        private static string RequireLowerHex(
                JObject value, string name, int length) {
            var result = RequireText(value, name);
            if (result.Length != length || result.Any(character =>
                    !((character >= '0' && character <= '9')
                        || (character >= 'a' && character <= 'f')))) {
                throw new JsonException(
                    $"{name} is not canonical lowercase hexadecimal.");
            }
            return result;
        }

        private static void RequireSha256(string value, string name) {
            if (value?.Length != 64 || value.Any(character =>
                    !((character >= '0' && character <= '9')
                        || (character >= 'a' && character <= 'f')))) {
                throw new ArgumentException(
                    $"{name} must be lowercase SHA-256.", name);
            }
        }

        private static long RequireInteger(JObject value, string name) {
            if (value[name]?.Type != JTokenType.Integer) {
                throw new JsonException($"{name} must be an integer.");
            }
            return value[name]!.Value<long>();
        }

        private static double RequireFinite(JObject value, string name) =>
            RequireFinite(value[name], name);

        private static double RequireFinite(JToken value, string name) {
            if (value?.Type is not (JTokenType.Integer or JTokenType.Float)) {
                throw new JsonException($"{name} must be numeric.");
            }
            var result = value.Value<double>();
            if (!double.IsFinite(result)
                    || (result == 0.0 && BitConverter.DoubleToInt64Bits(result) < 0)) {
                throw new JsonException(
                    $"{name} must be finite and not negative zero.");
            }
            return result;
        }

        private static DateTime RequireUtc(JObject value, string name) {
            var text = RequireText(value, name);
            if (!DateTimeOffset.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var parsed)
                    || parsed.Offset != TimeSpan.Zero) {
                throw new JsonException($"{name} must be UTC.");
            }
            return parsed.UtcDateTime;
        }

        private static void RequireUtc(DateTime value, string name) {
            if (value.Kind != DateTimeKind.Utc) {
                throw new ArgumentException($"{name} must be UTC.", name);
            }
        }

        private static void RequireTemperature(double value, string name) {
            if (!double.IsFinite(value) || value < -100.0 || value > 100.0) {
                throw new ArgumentOutOfRangeException(name);
            }
        }
    }
}