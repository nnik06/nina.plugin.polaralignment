using FluentAssertions;
using Newtonsoft.Json;
using NINA.Plugins.PolarAlignment;
using NUnit.Framework;
using System;
using System.Configuration;
using System.Linq;

namespace NINA.Plugins.PolarAlignment.Test {
    [TestFixture]
    public class UpasSupervisorCoarseEvidenceSchemaTest {
        [Test]
        public void ExactIntegerV2IsTheOnlyAdmittedTopLevelSchema() {
            var root = UpasSupervisorCoarseEvidenceSchemaGate.ParseSupportedRoot(
                "{\"schemaVersion\":2,\"nested\":{\"schemaVersion\":1}}");

            root["schemaVersion"]!.ToObject<int>().Should().Be(2);
        }

        [TestCase("{}")]
        [TestCase("{\"schemaVersion\":1}")]
        [TestCase("{\"schemaVersion\":3}")]
        [TestCase("{\"schemaVersion\":\"2\"}")]
        [TestCase("{\"schemaVersion\":\"V2\"}")]
        [TestCase("{\"schemaVersion\":2.0}")]
        [TestCase("{\"schemaVersion\":null}")]
        public void MissingLegacyFutureAndCoercedVersionsFailBeforeNestedParsing(string json) {
            Action parse = () => UpasSupervisorCoarseEvidenceSchemaGate.ParseSupportedRoot(json);

            parse.Should().Throw<JsonException>()
                .WithMessage("*exact integer 2 is required*");
        }

        [TestCase(5.4, 5.4, 1.0)]
        [TestCase(-5.4, 5.3, 1.0)]
        [TestCase(-5.4, 5.4, 0.9)]
        [TestCase(-5.4, 5.4, -0.0)]
        [TestCase(double.NaN, 5.4, 1.0)]
        public void PayloadCannotAlterCompiledHardLimitOrReserve(
                double negativeLimit,
                double positiveLimit,
                double reserve) {
            Action validate = () =>
                UpasSupervisorCoarseEvidenceSchemaGate.ValidateCompiledSafetyCrossChecks(
                    negativeLimit, positiveLimit, reserve);

            validate.Should().Throw<JsonException>();
        }

        [Test]
        public void ExactCompiledSafetyCrossChecksAreAccepted() {
            Action validate = () =>
                UpasSupervisorCoarseEvidenceSchemaGate.ValidateCompiledSafetyCrossChecks(
                    -5.4, 5.4, 1.0);

            validate.Should().NotThrow();
        }

        [Test]
        public void HardLimitAndReserveAreNotApplicationSettings() {
            var settingNames = typeof(NINA.Plugins.PolarAlignment.Properties.Settings)
                .GetProperties()
                .Select(property => property.Name)
                .ToArray();

            settingNames.Should().NotContain(nameof(UpasCoarsePlanningSafetyPolicy.PhysicalHardLimitDegrees));
            settingNames.Should().NotContain(nameof(UpasCoarsePlanningSafetyPolicy.MinimumReservedTravelDegrees));
            UpasCoarsePlanningSafetyPolicy.PhysicalHardLimitDegrees.Should().Be(5.4);
            UpasCoarsePlanningSafetyPolicy.MinimumReservedTravelDegrees.Should().Be(1.0);
        }

        [Test]
        public void ExistingPlannerConsumesTheSameCompiledSafetyPolicy() {
            TppaCoarseCorrectionPlanner.PhysicalHardLimitDegrees.Should()
                .Be(UpasCoarsePlanningSafetyPolicy.PhysicalHardLimitDegrees);
            TppaCoarseCorrectionPlanner.MinimumReservedTravelDegrees.Should()
                .Be(UpasCoarsePlanningSafetyPolicy.MinimumReservedTravelDegrees);
            TppaCoarseCorrectionPlanner.RequiredCoordinateConvention.Should()
                .Be(UpasCoarsePlanningSafetyPolicy.RequiredCoordinateConvention);
        }
    }
}
