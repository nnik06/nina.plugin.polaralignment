using System.Text;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaCommissionedCadenceAuthorityParserTest {
        private const string PluginSha =
            "1111111111111111111111111111111111111111111111111111111111111111";
        private const string RuntimeManifestSha =
            "abababababababababababababababababababababababababababababababab";
        private const string HardwareId =
            "2222222222222222222222222222222222222222222222222222222222222222";
        private const string MechanicalId =
            "3333333333333333333333333333333333333333333333333333333333333333";
        private static readonly DateTime NowUtc =
            new(2026, 8, 4, 0, 0, 0, DateTimeKind.Utc);

        [Test]
        public void AcceptsExactBuildRigAndQualifiedShortCadence() {
            var result = Parse(Valid());

            result.QualifiedSettleSeconds.Should().Be(15.0);
            result.MaximumFreshDeterminationSeconds.Should().Be(50.0);
            result.SourceTransitionCount.Should().Be(20);
            result.SourceNightCount.Should().Be(2);
            result.MaximumVectorSeparationMinutes.Should().Be(0.5);
            result.ZeroFalseStableExits.Should().BeTrue();
            result.ArtifactSha256.Should().MatchRegex("^[0-9a-f]{64}$");
        }

        [Test]
        public void RejectsDifferentLoadedDll() {
            var action = () => TppaCommissionedCadenceAuthorityParser.Parse(
                Bytes(Valid()), NowUtc, new string('9', 64), RuntimeManifestSha, HardwareId,
                MechanicalId, "hae29c-ec-full-rig-v1", 35.0);

            action.Should().Throw<JsonException>().WithMessage("*loaded DLL*");
        }

        [Test]
        public void RejectsDifferentMechanicalEpoch() {
            var action = () => TppaCommissionedCadenceAuthorityParser.Parse(
                Bytes(Valid()), NowUtc, PluginSha, RuntimeManifestSha, HardwareId,
                new string('9', 64), "hae29c-ec-full-rig-v1", 35.0);

            action.Should().Throw<JsonException>().WithMessage("*mechanical state*");
        }

        [TestCase(4.999)]
        [TestCase(30.0)]
        [TestCase(31.0)]
        public void RejectsUnqualifiedSettle(double settleSeconds) {
            var value = Valid();
            value["qualifiedSettleSeconds"] = settleSeconds;

            var action = () => Parse(value);

            action.Should().Throw<JsonException>().WithMessage("*settle*");
        }

        [TestCase(19, 2, 0.5, true)]
        [TestCase(20, 1, 0.5, true)]
        [TestCase(20, 2, 0.501, true)]
        [TestCase(20, 2, 0.5, false)]
        public void RejectsInsufficientSourceEvidence(
                int transitions, int nights, double separation, bool zeroFalseStableExits) {
            var value = Valid();
            value["sourceTransitionCount"] = transitions;
            value["sourceNightCount"] = nights;
            value["maximumVectorSeparationMinutes"] = separation;
            value["zeroFalseStableExits"] = zeroFalseStableExits;

            var action = () => Parse(value);

            action.Should().Throw<JsonException>();
        }

        [Test]
        public void RejectsDifferentRuntimeManifest() {
            var action = () => TppaCommissionedCadenceAuthorityParser.Parse(
                Bytes(Valid()), NowUtc, PluginSha, new string('9', 64), HardwareId,
                MechanicalId, "hae29c-ec-full-rig-v1", 35.0);

            action.Should().Throw<JsonException>().WithMessage("*runtime manifest*");
        }

        [Test]
        public void RejectsDifferentCommissioningPolicy() {
            var value = Valid();
            value["commissioningPolicySha256"] = new string('9', 64);

            var action = () => Parse(value);

            action.Should().Throw<JsonException>().WithMessage("*commissioning policy*");
        }

        [TestCase("sourceNullPairCount", 9)]
        [TestCase("sourceTimingDeterminationCount", 58)]
        [TestCase("sourceNightCount", 1)]
        [TestCase("timingExcludedSampleCount", 1)]
        public void RejectsIncompleteCommissioningEvidence(string property, int value) {
            var artifact = Valid();
            artifact[property] = value;

            var action = () => Parse(artifact);

            action.Should().Throw<JsonException>();
        }

        [TestCase("nullP95SeparationMinutes", 0.251)]
        [TestCase("candidateP95SeparationMinutes", 0.501)]
        [TestCase("candidateToNullP95Ratio", 1.501)]
        [TestCase("timingObservedMaximumSeconds", 50.1)]
        [TestCase("timingUpperToleranceSeconds", 75.1)]
        public void RejectsOutOfPolicyCommissioningStatistic(string property, double value) {
            var artifact = Valid();
            artifact[property] = value;

            var action = () => Parse(artifact);

            action.Should().Throw<JsonException>();
        }

        [TestCase("directionOrderCoveragePassed")]
        [TestCase("noSingleNightDominance")]
        public void RejectsMissingCommissioningCoverage(string property) {
            var artifact = Valid();
            artifact[property] = false;

            var action = () => Parse(artifact);

            action.Should().Throw<JsonException>().WithMessage("*coverage*");
        }
        [Test]
        public void RejectsUnknownProperties() {
            var value = Valid();
            value["unexpected"] = true;

            var action = () => Parse(value);

            action.Should().Throw<JsonException>().WithMessage("*frozen schema*");
        }

        private static TppaCommissionedCadenceAuthority Parse(JObject value) =>
            TppaCommissionedCadenceAuthorityParser.Parse(
                Bytes(value), NowUtc, PluginSha, RuntimeManifestSha, HardwareId, MechanicalId,
                "hae29c-ec-full-rig-v1", 35.0);

        private static byte[] Bytes(JObject value) =>
            Encoding.UTF8.GetBytes(value.ToString(Formatting.None));

        private static JObject Valid() => new() {
            ["schemaVersion"] = 2,
            ["authorityId"] = "10000000-0000-4000-8000-000000000001",
            ["commissionedUtc"] = "2026-08-03T00:00:00.0000000Z",
            ["validUntilUtc"] = "2026-08-05T00:00:00.0000000Z",
            ["repositoryHead"] = new string('a', 40),
            ["pluginAssemblySha256"] = PluginSha,
            ["runtimeManifestSha256"] = RuntimeManifestSha,
            ["commissioningPolicySha256"] = TppaCommissionedCadenceAuthorityParser.CommissioningPolicySha256,
            ["hardwareConfigurationId"] = HardwareId,
            ["mechanicalStateId"] = MechanicalId,
            ["loadProfileId"] = "hae29c-ec-full-rig-v1",
            ["temperatureC"] = new JObject {
                ["minimum"] = 30.0,
                ["maximum"] = 45.0
            },
            ["qualifiedSettleSeconds"] = 15.0,
            ["maximumFreshDeterminationSeconds"] = 50.0,
            ["sourceNominationSha256"] = new string('b', 64),
            ["sourceVectorPairCampaignSha256"] = new string('c', 64),
            ["sourceNullPairCampaignSha256"] = new string('d', 64),
            ["sourceTimingCampaignSha256"] = new string('e', 64),
            ["sourceTransitionCount"] = 20,
            ["sourceNullPairCount"] = 10,
            ["sourceTimingDeterminationCount"] = 59,
            ["sourceNightCount"] = 2,
            ["maximumVectorSeparationMinutes"] = 0.5,
            ["nullP95SeparationMinutes"] = 0.2,
            ["candidateP95SeparationMinutes"] = 0.25,
            ["candidateToNullP95Ratio"] = 1.25,
            ["timingObservedMaximumSeconds"] = 42.0,
            ["timingUpperToleranceSeconds"] = 47.0,
            ["timingExcludedSampleCount"] = 0,
            ["directionOrderCoveragePassed"] = true,
            ["noSingleNightDominance"] = true,
            ["zeroFalseStableExits"] = true
        };
    }
}
