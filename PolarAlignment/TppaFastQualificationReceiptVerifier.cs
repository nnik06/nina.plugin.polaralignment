using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NINA.Plugins.PolarAlignment {
    internal sealed record TppaFastQualificationReceiptVerification(
        bool IsValid,
        IReadOnlyList<string> Issues,
        TppaFastQualificationInput QualificationInput);

    /// <summary>
    /// Parses a persisted fast-qualification receipt and recomputes its content
    /// and envelope digests plus its policy verdict. These checks prove internal
    /// content integrity, not signer authenticity. Source-vector provenance is
    /// independently bound only when an expected digest is supplied. Verification
    /// never grants motion authority.
    /// </summary>
    internal static class TppaFastQualificationReceiptVerifier {
        private static readonly string[] ExpectedReceiptProperties = {
            "schemaVersion",
            "campaignId",
            "createdUtc",
            "policySourceSha256",
            "policyParametersSha256",
            "sourcePolarErrorVectorDigest",
            "qualificationInputSha256",
            "qualificationInputEnvelope",
            "policyParametersEnvelope",
            "isFastTruePoleQualified",
            "issues",
            "grantsMotionAuthority",
            "receiptContentSha256"
        };

        private static readonly JsonSerializerSettings SerializerSettings = new() {
            Culture = CultureInfo.InvariantCulture,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            NullValueHandling = NullValueHandling.Include
        };

        public static TppaFastQualificationReceiptVerification Verify(
            string receiptJson,
            string expectedPolicySourceSha256 = null,
            string expectedSourcePolarErrorVectorDigest = null) {
            var issues = new List<string>();
            var receipt = ParseReceipt(receiptJson, issues);
            if (receipt == null) {
                return Invalid(issues);
            }

            RejectUnexpectedOrMissingProperties(receipt, issues);
            RequireInteger(
                receipt,
                "schemaVersion",
                TppaFastQualificationReceipt.CurrentSchemaVersion,
                issues);
            RequireNonBlankString(receipt, "campaignId", issues);
            RequireUtcDate(receipt, "createdUtc", issues);
            var policySourceSha256 =
                RequireSha256(receipt, "policySourceSha256", issues);
            var policyParametersSha256 =
                RequireSha256(receipt, "policyParametersSha256", issues);
            var sourcePolarErrorVectorDigest =
                RequireSha256(receipt, "sourcePolarErrorVectorDigest", issues);
            var qualificationInputSha256 =
                RequireSha256(receipt, "qualificationInputSha256", issues);
            var receiptContentSha256 =
                RequireSha256(receipt, "receiptContentSha256", issues);

            if (expectedPolicySourceSha256 != null
                    && !string.Equals(
                        policySourceSha256,
                        expectedPolicySourceSha256,
                        StringComparison.Ordinal)) {
                issues.Add("policy source digest does not match the expected deployed policy");
            }
            if (expectedSourcePolarErrorVectorDigest != null) {
                if (!IsSha256(expectedSourcePolarErrorVectorDigest)) {
                    issues.Add("expected source polar-error vector digest must be lowercase SHA-256");
                } else if (!string.Equals(
                        sourcePolarErrorVectorDigest,
                        expectedSourcePolarErrorVectorDigest,
                        StringComparison.Ordinal)) {
                    issues.Add(
                        "source polar-error vector digest does not match the independent expected digest");
                }
            }

            var grantsMotionAuthority = receipt["grantsMotionAuthority"];
            if (grantsMotionAuthority?.Type != JTokenType.Boolean
                    || grantsMotionAuthority.Value<bool>()) {
                issues.Add("receipt must explicitly deny motion authority");
            }

            var inputEnvelope = RequireEnvelope(
                receipt,
                "qualificationInputEnvelope",
                "input",
                issues);
            var policyEnvelope = RequireEnvelope(
                receipt,
                "policyParametersEnvelope",
                "policy",
                issues);

            if (inputEnvelope != null && qualificationInputSha256 != null) {
                var canonicalEnvelope = new JObject {
                    ["schemaVersion"] = inputEnvelope["schemaVersion"]?.DeepClone(),
                    ["input"] = SortToken(inputEnvelope["input"])
                };
                var actual = Digest(canonicalEnvelope.ToString(Formatting.None));
                if (!string.Equals(
                        actual,
                        qualificationInputSha256,
                        StringComparison.Ordinal)) {
                    issues.Add("qualification input envelope digest does not match");
                }
            }
            if (policyEnvelope != null && policyParametersSha256 != null) {
                var canonicalEnvelope = new JObject {
                    ["schemaVersion"] = policyEnvelope["schemaVersion"]?.DeepClone(),
                    ["policy"] = SortToken(policyEnvelope["policy"])
                };
                var actual = Digest(canonicalEnvelope.ToString(Formatting.None));
                if (!string.Equals(
                        actual,
                        policyParametersSha256,
                        StringComparison.Ordinal)) {
                    issues.Add("policy parameters envelope digest does not match");
                }
            }

            var serializer = JsonSerializer.Create(SerializerSettings);
            TppaFastQualificationInput input = null;
            TppaFastQualificationPolicy policy = null;
            try {
                input = inputEnvelope?["input"]?.ToObject<TppaFastQualificationInput>(serializer);
            } catch (Exception ex) when (
                    ex is JsonException
                    || ex is ArgumentException
                    || ex is InvalidOperationException
                    || ex is OverflowException) {
                issues.Add($"qualification input cannot be deserialized: {ex.Message}");
            }
            try {
                policy = policyEnvelope?["policy"]?.ToObject<TppaFastQualificationPolicy>(serializer);
            } catch (Exception ex) when (
                    ex is JsonException
                    || ex is ArgumentException
                    || ex is InvalidOperationException
                    || ex is OverflowException) {
                issues.Add($"policy parameters cannot be deserialized: {ex.Message}");
            }

            if (policy != null) {
                var expectedPolicy = SortToken(
                    JToken.FromObject(new TppaFastQualificationPolicy(), serializer));
                var suppliedPolicy = SortToken(JToken.FromObject(policy, serializer));
                if (!JToken.DeepEquals(expectedPolicy, suppliedPolicy)) {
                    issues.Add("receipt does not use the frozen default qualification policy");
                }
            }

            if (input != null && policy != null) {
                try {
                    var recomputed = TppaFastQualification.Evaluate(input, policy);
                    var storedVerdict = receipt["isFastTruePoleQualified"];
                    if (storedVerdict?.Type != JTokenType.Boolean
                            || storedVerdict.Value<bool>()
                                != recomputed.IsFastTruePoleQualified) {
                        issues.Add("stored qualification verdict does not match recomputation");
                    }

                    var storedIssues = receipt["issues"] as JArray;
                    if (storedIssues == null
                            || storedIssues.Any(issue => issue.Type != JTokenType.String)
                            || !storedIssues.Values<string>().SequenceEqual(recomputed.Issues)) {
                        issues.Add("stored qualification issues do not match recomputation");
                    }
                } catch (Exception ex) when (
                        ex is ArgumentException
                        || ex is InvalidOperationException
                        || ex is OverflowException) {
                    issues.Add($"qualification input cannot be evaluated: {ex.Message}");
                }
            }

            if (receiptContentSha256 != null) {
                var unsignedPayload = new JObject {
                    ["schemaVersion"] = receipt["schemaVersion"]?.DeepClone(),
                    ["campaignId"] = receipt["campaignId"]?.DeepClone(),
                    ["createdUtc"] = receipt["createdUtc"]?.DeepClone(),
                    ["policySourceSha256"] = receipt["policySourceSha256"]?.DeepClone(),
                    ["policyParametersSha256"] = receipt["policyParametersSha256"]?.DeepClone(),
                    ["sourcePolarErrorVectorDigest"] =
                        receipt["sourcePolarErrorVectorDigest"]?.DeepClone(),
                    ["qualificationInputSha256"] =
                        receipt["qualificationInputSha256"]?.DeepClone(),
                    ["qualificationInputEnvelope"] =
                        receipt["qualificationInputEnvelope"]?.DeepClone(),
                    ["policyParametersEnvelope"] =
                        receipt["policyParametersEnvelope"]?.DeepClone(),
                    ["isFastTruePoleQualified"] =
                        receipt["isFastTruePoleQualified"]?.DeepClone(),
                    ["issues"] = receipt["issues"]?.DeepClone(),
                    ["grantsMotionAuthority"] =
                        receipt["grantsMotionAuthority"]?.DeepClone()
                };
                var actual = Digest(unsignedPayload.ToString(Formatting.None));
                if (!string.Equals(
                        actual,
                        receiptContentSha256,
                        StringComparison.Ordinal)) {
                    issues.Add("receipt content digest does not match");
                }
            }

            return new TppaFastQualificationReceiptVerification(
                issues.Count == 0,
                issues,
                input);
        }

        private static JObject ParseReceipt(string receiptJson, ICollection<string> issues) {
            if (string.IsNullOrWhiteSpace(receiptJson)) {
                issues.Add("receipt JSON is required");
                return null;
            }

            try {
                return JObject.Parse(
                    receiptJson,
                    new JsonLoadSettings {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                    });
            } catch (JsonException ex) {
                issues.Add($"receipt JSON is invalid: {ex.Message}");
                return null;
            }
        }

        private static void RejectUnexpectedOrMissingProperties(
                JObject receipt,
                ICollection<string> issues) {
            var actual = receipt.Properties()
                .Select(property => property.Name)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var expected in ExpectedReceiptProperties) {
                if (!actual.Remove(expected)) {
                    issues.Add($"receipt property '{expected}' is missing");
                }
            }
            foreach (var unexpected in actual.OrderBy(value => value, StringComparer.Ordinal)) {
                issues.Add($"receipt property '{unexpected}' is not recognized");
            }
        }

        private static JToken RequireEnvelope(
                JObject receipt,
                string propertyName,
                string payloadName,
                ICollection<string> issues) {
            if (receipt[propertyName] is not JObject envelope) {
                issues.Add($"receipt property '{propertyName}' must be an object");
                return null;
            }
            var expected = new HashSet<string>(
                new[] { "schemaVersion", payloadName },
                StringComparer.Ordinal);
            var actual = envelope.Properties()
                .Select(property => property.Name)
                .ToHashSet(StringComparer.Ordinal);
            if (!actual.SetEquals(expected)) {
                issues.Add($"receipt property '{propertyName}' has an invalid envelope shape");
                return null;
            }
            RequireInteger(
                envelope,
                "schemaVersion",
                TppaFastQualificationReceipt.CurrentSchemaVersion,
                issues);
            if (envelope[payloadName] is not JObject) {
                issues.Add($"receipt property '{propertyName}.{payloadName}' must be an object");
                return null;
            }
            return envelope;
        }

        private static void RequireInteger(
                JObject value,
                string propertyName,
                int expected,
                ICollection<string> issues) {
            var token = value[propertyName];
            if (token?.Type != JTokenType.Integer || token.Value<int>() != expected) {
                issues.Add($"receipt property '{propertyName}' must equal {expected}");
            }
        }

        private static void RequireNonBlankString(
                JObject value,
                string propertyName,
                ICollection<string> issues) {
            var token = value[propertyName];
            if (token?.Type != JTokenType.String
                    || string.IsNullOrWhiteSpace(token.Value<string>())) {
                issues.Add($"receipt property '{propertyName}' must be a non-blank string");
            }
        }

        private static void RequireUtcDate(
                JObject value,
                string propertyName,
                ICollection<string> issues) {
            var token = value[propertyName];
            if (token?.Type != JTokenType.Date
                    || token.Value<DateTime>().Kind != DateTimeKind.Utc) {
                issues.Add($"receipt property '{propertyName}' must be a UTC timestamp");
            }
        }

        private static string RequireSha256(
                JObject value,
                string propertyName,
                ICollection<string> issues) {
            var token = value[propertyName];
            var digest = token?.Type == JTokenType.String
                ? token.Value<string>()
                : null;
            if (!IsSha256(digest)) {
                issues.Add($"receipt property '{propertyName}' must be lowercase SHA-256");
                return null;
            }
            return digest;
        }

        private static bool IsSha256(string value) =>
            value?.Length == 64
            && value.All(character =>
                (character >= '0' && character <= '9')
                || (character >= 'a' && character <= 'f'));

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

        private static TppaFastQualificationReceiptVerification Invalid(
                IReadOnlyList<string> issues) =>
            new(false, issues, null);
    }
}
