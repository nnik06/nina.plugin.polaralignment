using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NINA.Plugins.PolarAlignment {
    internal sealed class TppaFastQualificationReceipt {
        private readonly JToken qualificationInputEnvelope;
        private readonly JToken policyParametersEnvelope;

        public const int CurrentSchemaVersion = 3;

        private static readonly JsonSerializerSettings SerializerSettings = new() {
            Culture = CultureInfo.InvariantCulture,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            NullValueHandling = NullValueHandling.Include
        };

        private TppaFastQualificationReceipt(
            string campaignId,
            DateTime createdUtc,
            string policySourceSha256,
            string sourcePolarErrorVectorDigest,
            string qualificationInputSha256,
            string policyParametersSha256,
            JToken qualificationInputEnvelope,
            JToken policyParametersEnvelope,
            TppaFastQualificationResult result) {
            CampaignId = campaignId;
            CreatedUtc = createdUtc;
            PolicySourceSha256 = policySourceSha256;
            SourcePolarErrorVectorDigest = sourcePolarErrorVectorDigest;
            QualificationInputSha256 = qualificationInputSha256;
            PolicyParametersSha256 = policyParametersSha256;
            this.qualificationInputEnvelope = qualificationInputEnvelope.DeepClone();
            this.policyParametersEnvelope = policyParametersEnvelope.DeepClone();
            IsFastTruePoleQualified = result.IsFastTruePoleQualified;
            Issues = result.Issues.ToArray();
            ReceiptContentSha256 = Digest(BuildUnsignedPayload().ToString(Formatting.None));
        }

        [JsonProperty("schemaVersion")]
        public int SchemaVersion => CurrentSchemaVersion;

        [JsonProperty("campaignId")]
        public string CampaignId { get; }

        [JsonProperty("createdUtc")]
        public DateTime CreatedUtc { get; }

        [JsonProperty("policySourceSha256")]
        public string PolicySourceSha256 { get; }

        [JsonProperty("sourcePolarErrorVectorDigest")]
        public string SourcePolarErrorVectorDigest { get; }

        [JsonProperty("qualificationInputSha256")]
        public string QualificationInputSha256 { get; }

        [JsonProperty("policyParametersSha256")]
        public string PolicyParametersSha256 { get; }

        [JsonProperty("receiptContentSha256")]
        public string ReceiptContentSha256 { get; }

        [JsonProperty("isFastTruePoleQualified")]
        public bool IsFastTruePoleQualified { get; }

        [JsonProperty("issues")]
        public IReadOnlyList<string> Issues { get; }

        [JsonProperty("grantsMotionAuthority")]
        public bool GrantsMotionAuthority => false;

        public static TppaFastQualificationReceipt Create(
            string campaignId,
            DateTime createdUtc,
            string policySourceSha256,
            string sourcePolarErrorVectorDigest,
            TppaFastQualificationInput input) {
            if (string.IsNullOrWhiteSpace(campaignId)) {
                throw new ArgumentException("Campaign ID is required.", nameof(campaignId));
            }
            if (createdUtc.Kind != DateTimeKind.Utc) {
                throw new ArgumentException("Receipt time must be UTC.", nameof(createdUtc));
            }
            RequireSha256(policySourceSha256, nameof(policySourceSha256));
            RequireSha256(sourcePolarErrorVectorDigest, nameof(sourcePolarErrorVectorDigest));

            var serializer = JsonSerializer.Create(SerializerSettings);
            var canonicalInputEnvelope = new JObject {
                ["schemaVersion"] = CurrentSchemaVersion,
                ["input"] = SortToken(JToken.FromObject(input, serializer))
            };
            var activePolicy = new TppaFastQualificationPolicy();
            var canonicalPolicyEnvelope = new JObject {
                ["schemaVersion"] = CurrentSchemaVersion,
                ["policy"] = SortToken(JToken.FromObject(activePolicy, serializer))
            };

            return new TppaFastQualificationReceipt(
                campaignId,
                createdUtc,
                policySourceSha256,
                sourcePolarErrorVectorDigest,
                Digest(canonicalInputEnvelope.ToString(Formatting.None)),
                Digest(canonicalPolicyEnvelope.ToString(Formatting.None)),
                canonicalInputEnvelope,
                canonicalPolicyEnvelope,
                TppaFastQualification.Evaluate(input, activePolicy));
        }

        public string ToJson() {
            var payload = BuildUnsignedPayload();
            payload["receiptContentSha256"] = ReceiptContentSha256;
            return payload.ToString(Formatting.None);
        }

        public bool HasValidContentDigest() =>
            ReceiptContentSha256 == Digest(BuildUnsignedPayload().ToString(Formatting.None));

        private JObject BuildUnsignedPayload() =>
            new() {
                ["schemaVersion"] = SchemaVersion,
                ["campaignId"] = CampaignId,
                ["createdUtc"] = CreatedUtc,
                ["policySourceSha256"] = PolicySourceSha256,
                ["policyParametersSha256"] = PolicyParametersSha256,
                ["sourcePolarErrorVectorDigest"] = SourcePolarErrorVectorDigest,
                ["qualificationInputSha256"] = QualificationInputSha256,
                ["qualificationInputEnvelope"] = qualificationInputEnvelope.DeepClone(),
                ["policyParametersEnvelope"] = policyParametersEnvelope.DeepClone(),
                ["isFastTruePoleQualified"] = IsFastTruePoleQualified,
                ["issues"] = new JArray(Issues),
                ["grantsMotionAuthority"] = GrantsMotionAuthority
            };

        private static JToken SortToken(JToken token) {
            if (token is JObject valueObject) {
                var sorted = new JObject();
                foreach (var property in valueObject.Properties()
                             .OrderBy(property => property.Name, StringComparer.Ordinal)) {
                    sorted.Add(property.Name, SortToken(property.Value));
                }
                return sorted;
            }
            if (token is JArray valueArray) {
                return new JArray(valueArray.Select(SortToken));
            }
            return token.DeepClone();
        }

        private static string Digest(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
                .ToLowerInvariant();

        private static void RequireSha256(string value, string name) {
            if (value?.Length != 64 || value.Any(character =>
                    (character < '0' || character > '9')
                    && (character < 'a' || character > 'f'))) {
                throw new ArgumentException("Value must be lowercase SHA-256.", name);
            }
        }
    }
}
