using FluentAssertions;
using NUnit.Framework;
using System;

namespace NINA.Plugins.PolarAlignment.Test {
    [TestFixture]
    public class TppaWitnessUncertaintyTest {
        [Test]
        public void MeasuredTermsProduceConservativeNinetyFivePercentBound() {
            var result = TppaWitnessUncertainty.Evaluate(
                QualifiedEvidence(),
                maximumUpperBound95ArcSeconds: 30,
                minimumCalibrationSamples: 3,
                minimumClosureSamples: 2);

            result.IsQualified.Should().BeTrue();
            result.UpperBound95ArcSeconds.Should().Be(9);
            result.Issues.Should().BeEmpty();
        }

        [TestCase(0.0)]
        [TestCase(-1.0)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void MissingOrNonPhysicalNumericEvidenceFailsClosed(double value) {
            var result = TppaWitnessUncertainty.Evaluate(
                QualifiedEvidence() with {
                    CalibrationResidualArcSeconds = value
                },
                maximumUpperBound95ArcSeconds: 30,
                minimumCalibrationSamples: 3,
                minimumClosureSamples: 2);

            result.IsQualified.Should().BeFalse();
            result.UpperBound95ArcSeconds.Should().BeNull();
            result.Issues.Should().Contain(
                issue => issue.Contains("finite and positive"));
        }

        [Test]
        public void EveryUncertaintyTermIsMandatory() {
            var evidence = QualifiedEvidence();
            var incompleteEvidence = new[] {
                evidence with { MeasurementStandardUncertaintyArcSeconds = 0 },
                evidence with { CalibrationResidualArcSeconds = 0 },
                evidence with { OrientationModelResidualArcSeconds = 0 },
                evidence with { ClosureResidualArcSeconds = 0 },
                evidence with { FrameSystematicBoundArcSeconds = 0 },
                evidence with { DistortionSystematicBoundArcSeconds = 0 },
                evidence with { MechanicalSystematicBoundArcSeconds = 0 }
            };

            foreach (var incomplete in incompleteEvidence) {
                var result = TppaWitnessUncertainty.Evaluate(
                    incomplete,
                    maximumUpperBound95ArcSeconds: 30,
                    minimumCalibrationSamples: 3,
                    minimumClosureSamples: 2);

                result.IsQualified.Should().BeFalse();
                result.UpperBound95ArcSeconds.Should().BeNull();
                result.Issues.Should().Contain(
                    issue => issue.Contains("finite and positive"));
            }
        }

        [Test]
        public void IncreasingAnySystematicCannotReduceTheBound() {
            var baseline = TppaWitnessUncertainty.Evaluate(
                QualifiedEvidence(), 30, 3, 2);
            var increased = TppaWitnessUncertainty.Evaluate(
                QualifiedEvidence() with {
                    MechanicalSystematicBoundArcSeconds = 4
                },
                30,
                3,
                2);

            increased.UpperBound95ArcSeconds!.Value.Should()
                .BeGreaterThan(baseline.UpperBound95ArcSeconds!.Value);
        }

        [Test]
        public void DigestAndMeasuredSampleFloorsAreMandatory() {
            var result = TppaWitnessUncertainty.Evaluate(
                QualifiedEvidence() with {
                    InputDigest = "not-a-digest",
                    CalibrationSampleCount = 2,
                    ClosureSampleCount = 1
                },
                30,
                3,
                2);

            result.IsQualified.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("digest"));
            result.Issues.Should().Contain(issue => issue.Contains("calibration has only"));
            result.Issues.Should().Contain(issue => issue.Contains("closure has only"));
        }

        [Test]
        public void NumericMutationAfterDigestingFailsClosed() {
            var tampered = QualifiedEvidence() with {
                MechanicalSystematicBoundArcSeconds = 0.5
            };

            var result = TppaWitnessUncertainty.Evaluate(
                tampered, 30, 3, 2);

            result.IsQualified.Should().BeFalse();
            result.Issues.Should().Contain(
                issue => issue.Contains("evidence digest"));
        }

        [Test]
        public void MultiArcResidualAboveThirtyArcsecondsCannotQualify() {
            var result = TppaWitnessUncertainty.Evaluate(
                QualifiedEvidence() with {
                    ClosureResidualArcSeconds = 5 * 60
                },
                maximumUpperBound95ArcSeconds: 30,
                minimumCalibrationSamples: 3,
                minimumClosureSamples: 2);

            result.IsQualified.Should().BeFalse();
            result.UpperBound95ArcSeconds.Should().BeGreaterThan(300);
            result.Issues.Should().Contain(issue => issue.Contains("exceeds"));
        }

        private static TppaWitnessUncertaintyEvidence QualifiedEvidence() {
            var evidence = new TppaWitnessUncertaintyEvidence(
                ModelId: "qualified-independent-pole-camera-v1",
                InputDigest: new string('e', 64),
                EvidenceDigest: new string('0', 64),
                CalibrationSampleCount: 6,
                ClosureSampleCount: 3,
                MeasurementStandardUncertaintyArcSeconds: 1,
                CalibrationResidualArcSeconds: 2,
                OrientationModelResidualArcSeconds: 1,
                ClosureResidualArcSeconds: 1,
                FrameSystematicBoundArcSeconds: 1,
                DistortionSystematicBoundArcSeconds: 1,
                MechanicalSystematicBoundArcSeconds: 1);
            return evidence with {
                EvidenceDigest = TppaWitnessUncertainty.ComputeEvidenceDigest(evidence)
            };
        }
    }
}
