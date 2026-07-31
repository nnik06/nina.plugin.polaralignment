using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NINA.Plugins.PolarAlignment {
    internal sealed class TppaFastQualificationReceipt {
        public const int CurrentSchemaVersion = 1;

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
            TppaFastQualificationResult result) {
            CampaignId = campaignId;
            CreatedUtc = createdUtc;
            PolicySourceSha256 = policySourceSha256;
            SourcePolarErrorVectorDigest = sourcePolarErrorVectorDigest;
            QualificationInputSha256 = qualificationInputSha256;
            IsFastTruePoleQualified = result.IsFastTruePoleQualified;
            Issues = result.Issues.ToArray();
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
            TppaFastQualificationInput input,
            TppaFastQualificationPolicy policy = null) {
            if (string.IsNullOrWhiteSpace(campaignId)) {
                throw new ArgumentException("Campaign ID is required.", nameof(campaignId));
            }
            if (createdUtc.Kind != DateTimeKind.Utc) {
                throw new ArgumentException("Receipt time must be UTC.", nameof(createdUtc));
            }
            RequireSha256(policySourceSha256, nameof(policySourceSha256));
            RequireSha256(sourcePolarErrorVectorDigest, nameof(sourcePolarErrorVectorDigest));

            var inputJson = JsonConvert.SerializeObject(
                input,
                Formatting.None,
                SerializerSettings);
            var inputDigest = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(inputJson)))
                .ToLowerInvariant();

            return new TppaFastQualificationReceipt(
                campaignId,
                createdUtc,
                policySourceSha256,
                sourcePolarErrorVectorDigest,
                inputDigest,
                TppaFastQualification.Evaluate(input, policy));
        }

        public string ToJson() =>
            JsonConvert.SerializeObject(this, Formatting.None, SerializerSettings);

        private static void RequireSha256(string value, string name) {
            if (value?.Length != 64 || value.Any(character =>
                    (character < '0' || character > '9')
                    && (character < 'a' || character > 'f'))) {
                throw new ArgumentException("Value must be lowercase SHA-256.", name);
            }
        }
    }
}
