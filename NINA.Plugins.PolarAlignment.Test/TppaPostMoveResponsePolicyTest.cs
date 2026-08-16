using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaPostMoveResponsePolicyTest {
        [Test]
        public void BelowTargetRequiresStationaryConfirmationEvenInsideEnvelope() {
            var result = Evaluate(Vector(4.0, 0.0), Vector(2.9, 0.0));

            result.Classification.Should().Be(TppaPostMoveResponseClassification.ConvergedCandidate);
            result.ShouldRunStationaryConfirmation.Should().BeTrue();
            result.CouldAuthorizeAnotherMove.Should().BeFalse();
        }

        [Test]
        public void IdenticalResponseIsInconclusive() {
            var result = Evaluate(Vector(10.0, 2.0), Vector(10.0, 2.0));

            result.Classification.Should().Be(TppaPostMoveResponseClassification.Inconclusive);
            result.CouldAuthorizeAnotherMove.Should().BeFalse();
        }

        [Test]
        public void ImprovementMustBeatEnvelopeAndFractionalFloor() {
            Evaluate(Vector(30.0, 0.0), Vector(26.0, 0.0)).Classification
                .Should().Be(TppaPostMoveResponseClassification.Inconclusive);
            Evaluate(Vector(30.0, 0.0), Vector(25.4, 0.0)).Classification
                .Should().Be(TppaPostMoveResponseClassification.Improved);
        }

        [Test]
        public void MaterialTotalWorseningRegresses() {
            var result = Evaluate(Vector(10.0, 0.0), Vector(11.5, 0.0));

            result.Classification.Should().Be(TppaPostMoveResponseClassification.Regressed);
            result.CouldAuthorizeAnotherMove.Should().BeFalse();
        }

        [Test]
        public void SevereComponentWorseningRegressesDespiteTotalImprovement() {
            var result = Evaluate(Vector(20.0, 10.0), Vector(12.0, 15.0));

            result.Classification.Should().Be(TppaPostMoveResponseClassification.Regressed);
        }

        [Test]
        public void ModerateComponentWorseningVetoesImprovement() {
            var result = Evaluate(Vector(20.0, 5.0), Vector(15.0, 6.5));

            result.Classification.Should().Be(TppaPostMoveResponseClassification.Inconclusive);
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(-1.0)]
        public void InvalidTargetIsInconclusive(double target) {
            var result = TppaPostMoveResponsePolicy.Evaluate(
                Vector(10.0, 0.0),
                Vector(5.0, 0.0),
                target);

            result.Classification.Should().Be(TppaPostMoveResponseClassification.Inconclusive);
            result.CouldAuthorizeAnotherMove.Should().BeFalse();
        }

        [Test]
        public void InconsistentTotalIsInconclusive() {
            var invalid = TppaPolarErrorVector.FromMinutes(10.0, 0.0, 1.0);

            Evaluate(Vector(20.0, 0.0), invalid).Classification
                .Should().Be(TppaPostMoveResponseClassification.Inconclusive);
        }

        [Test]
        public void RecordedFreshTppaTriplesUseTheEuclideanComponentConvention() {
            var recordedBefore = TppaPolarErrorVector.FromMinutes(
                -(31.0 + 25.0 / 60.0),
                -(9.0 + 39.0 / 60.0),
                32.0 + 51.0 / 60.0);
            var recordedAfter = TppaPolarErrorVector.FromMinutes(
                -(31.0 + 11.0 / 60.0),
                -(9.0 + 42.0 / 60.0),
                32.0 + 40.0 / 60.0);

            var result = TppaPostMoveResponsePolicy.Evaluate(recordedBefore, recordedAfter, 3.0);

            double.IsNaN(result.RequiredImprovementMinutes).Should().BeFalse();
            result.Reason.Should().NotContain("invalid or internally inconsistent");
        }

        [Test]
        public void NegativeReportedTotalFailsClosedInsteadOfBeingNormalized() {
            var invalid = TppaPolarErrorVector.FromMinutes(10.0, 0.0, -10.0);

            Evaluate(Vector(20.0, 0.0), invalid).Reason
                .Should().Contain("invalid or internally inconsistent");
        }

        [Test]
        public void ReversingTheEvidenceCannotImproveBothDirections() {
            var before = Vector(12.0, 0.0);
            var after = Vector(8.0, 0.0);

            Evaluate(before, after).Classification.Should().Be(TppaPostMoveResponseClassification.Improved);
            Evaluate(after, before).Classification.Should().Be(TppaPostMoveResponseClassification.Regressed);
        }

        [TestCase((int)TppaPostMoveResponseClassification.Inconclusive)]
        [TestCase((int)TppaPostMoveResponseClassification.Regressed)]
        public void FastModeStopsUnqualifiedResponsesWithoutUpdatingController(int classificationValue) {
            var classification = (TppaPostMoveResponseClassification)classificationValue;
            var disposition = TppaPostMoveResponsePolicy.DispositionForMode(
                Decision(classification));

            disposition.UpdateController.Should().BeFalse();
            disposition.ContinueToStationaryConfirmation.Should().BeFalse();
            disposition.FailureMessage.Should().NotBeNullOrWhiteSpace();
        }

        [Test]
        public void FastModeMeaningfulImprovementUpdatesControllerForBudgetedSecondMove() {
            var disposition = TppaPostMoveResponsePolicy.DispositionForMode(
                Decision(TppaPostMoveResponseClassification.Improved));

            disposition.UpdateController.Should().BeTrue();
            disposition.ContinueToStationaryConfirmation.Should().BeFalse();
            disposition.FailureMessage.Should().BeNull();
        }

        [Test]
        public void FastModeConvergedCandidateUpdatesThenEntersStationaryConfirmation() {
            var disposition = TppaPostMoveResponsePolicy.DispositionForMode(
                Decision(TppaPostMoveResponseClassification.ConvergedCandidate));

            disposition.UpdateController.Should().BeTrue();
            disposition.ContinueToStationaryConfirmation.Should().BeTrue();
            disposition.FailureMessage.Should().BeNull();
        }

        [TestCase((int)TppaPostMoveResponseClassification.Inconclusive)]
        [TestCase((int)TppaPostMoveResponseClassification.Regressed)]
        public void ExtendedModeStillStopsUnqualifiedResponses(int classificationValue) {
            var classification = (TppaPostMoveResponseClassification)classificationValue;
            var disposition = TppaPostMoveResponsePolicy.DispositionForMode(
                Decision(classification));

            disposition.UpdateController.Should().BeFalse();
            disposition.ContinueToStationaryConfirmation.Should().BeFalse();
            disposition.FailureMessage.Should().NotBeNullOrWhiteSpace();
        }

        [Test]
        public void LogIsInvariantAndOwnsItsSchemaVersion() {
            Evaluate(Vector(12.0, 0.0), Vector(8.0, 0.0)).ToLogString().Should().Be(
                "schemaVersion=1; classification=Improved; preTotal=12.000'; postTotal=8.000'; improvement=+4.000'; requiredImprovement=1.800'; maxComponentWorsening=0.000'; confirm=False; couldAuthorizeAnotherMove=True; reason=fresh post-move evidence shows meaningful improvement outside the preregistered envelope");
        }

        private static TppaPostMoveResponseDecision Decision(
                TppaPostMoveResponseClassification classification) => new(
                    classification,
                    10.0,
                    5.0,
                    5.0,
                    1.5,
                    0.0,
                    ShouldRunStationaryConfirmation: classification == TppaPostMoveResponseClassification.ConvergedCandidate,
                    CouldAuthorizeAnotherMove: classification == TppaPostMoveResponseClassification.Improved,
                    "test");

        private static TppaPostMoveResponseDecision Evaluate(
            TppaPolarErrorVector before,
            TppaPolarErrorVector after) =>
            TppaPostMoveResponsePolicy.Evaluate(before, after, 3.0);

        private static TppaPolarErrorVector Vector(double azimuth, double altitude) =>
            TppaPolarErrorVector.FromMinutes(
                azimuth,
                altitude,
                System.Math.Sqrt(azimuth * azimuth + altitude * altitude));
    }
}
