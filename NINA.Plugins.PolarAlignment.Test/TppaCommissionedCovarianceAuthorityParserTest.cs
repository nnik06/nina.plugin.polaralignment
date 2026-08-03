using System.Text;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaCommissionedCovarianceAuthorityParserTest {
        private static readonly DateTime Now =
            new(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);
        private static readonly string Plugin = new('b', 64);

        [Test]
        public void ParsesExactCommissionedArtifactAndConvertsCovarianceUnits() {
            var bytes = Bytes(Authority());

            var result = TppaCommissionedCovarianceAuthorityParser.Parse(
                bytes, Now, Plugin, "hae29c-ec-full-rig-v1", 35.0);

            result.RepositoryHead.Should().Be(new string('c', 40));
            result.PluginAssemblySha256.Should().Be(Plugin);
            result.CovarianceAzAzSquareMinutes.Should().BeApproximately(0.36, 1e-12);
            result.CovarianceAzAltSquareMinutes.Should().Be(0.0);
            result.CovarianceAltAltSquareMinutes.Should().BeApproximately(0.72, 1e-12);
            result.ArtifactSha256.Should().MatchRegex("^[0-9a-f]{64}$");
        }

        [Test]
        public void RejectsLoadedDllMismatchBeforeRuntimeUse() {
            var action = () => TppaCommissionedCovarianceAuthorityParser.Parse(
                Bytes(Authority()), Now, new string('a', 64),
                "hae29c-ec-full-rig-v1", 35.0);

            action.Should().Throw<JsonException>()
                .WithMessage("*loaded DLL*");
        }

        [TestCase("extra")]
        [TestCase("stale")]
        [TestCase("load")]
        [TestCase("temperature")]
        [TestCase("covariance")]
        [TestCase("shared")]
        [TestCase("attempts")]
        public void RejectsUnqualifiedAuthorityVariants(string mutation) {
            var value = Authority();
            switch (mutation) {
                case "extra":
                    value["unexpected"] = true;
                    break;
                case "stale":
                    value["validUntilUtc"] = "2026-08-14T00:00:00Z";
                    break;
                case "load":
                    value["loadProfileId"] = "other";
                    break;
                case "temperature":
                    value["temperatureC"]!["maximum"] = 30.0;
                    break;
                case "covariance":
                    value["covarianceFloorSquareDegrees"] =
                        new JArray(new JArray(0.0001, 0.01),
                                   new JArray(0.01, 0.0002));
                    break;
                case "shared":
                    value["sharedSystematicIncluded"] = true;
                    break;
                case "attempts":
                    value["sourceAttemptCount"] = 19;
                    break;
            }

            var action = () => TppaCommissionedCovarianceAuthorityParser.Parse(
                Bytes(value), Now, Plugin, "hae29c-ec-full-rig-v1", 35.0);

            action.Should().Throw<JsonException>();
        }

        [Test]
        public void RejectsDuplicateJsonProperties() {
            var json = Encoding.UTF8.GetString(Bytes(Authority()));
            json = json.Replace(
                "\"schemaVersion\":1",
                "\"schemaVersion\":1,\"schemaVersion\":1");

            var action = () => TppaCommissionedCovarianceAuthorityParser.Parse(
                Encoding.UTF8.GetBytes(json), Now, Plugin,
                "hae29c-ec-full-rig-v1", 35.0);

            action.Should().Throw<JsonException>();
        }

        private static JObject Authority() => new() {
            ["schemaVersion"] = 1,
            ["authorityId"] = "10000000-0000-4000-8000-000000000001",
            ["commissionedUtc"] = "2026-08-01T00:00:00Z",
            ["validUntilUtc"] = "2026-09-01T00:00:00Z",
            ["repositoryHead"] = new string('c', 40),
            ["pluginAssemblySha256"] = Plugin,
            ["hardwareConfigurationId"] = "hae29c-ec",
            ["mechanicalStateId"] = new string('d', 64),
            ["loadProfileId"] = "hae29c-ec-full-rig-v1",
            ["solverIdentity"] = "astap-2026.1",
            ["catalogIdentity"] = "d50-v17",
            ["targetSkyArcId"] = "safe-arc-a",
            ["temperatureC"] = new JObject {
                ["minimum"] = 25.0,
                ["maximum"] = 45.0
            },
            ["covarianceFloorSquareDegrees"] = new JArray(
                new JArray(0.0001, 0.0),
                new JArray(0.0, 0.0002)),
            ["sharedSystematicIncluded"] = false,
            ["sourceCampaignManifestSha256"] = new string('e', 64),
            ["sourceAttemptCount"] = 20,
            ["sourcePassCount"] = 18,
            ["confidenceLevel"] = 0.95
        };

        private static byte[] Bytes(JObject value) =>
            Encoding.UTF8.GetBytes(value.ToString(Formatting.None));
    }
}