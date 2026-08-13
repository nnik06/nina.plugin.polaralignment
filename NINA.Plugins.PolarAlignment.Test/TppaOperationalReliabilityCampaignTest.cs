using System.Collections.Generic;
using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaOperationalReliabilityCampaignTest {
        private static TppaOperationalReliabilityAttempt Attempt(
                string id,
                bool qualified = true,
                bool safe = true,
                bool noWorse = true) => new(id, qualified, safe, noWorse, 48.0);

        private static TppaOperationalOutcomeEvidence Drift(double rate = 0.79) => new(
            TppaOperationalOutcomeCheck.UnguidedDeclinationDrift,
            Passed: true,
            UnguidedDeclinationDriftArcSecondsPerMinute: rate,
            Description: "fresh unguided DEC drift");

        [Test]
        public void FourOfFiveWithPassingDriftIsReliable() {
            var result = TppaOperationalReliabilityCampaign.Evaluate(
                new List<TppaOperationalReliabilityAttempt> {
                    Attempt("1"), Attempt("2"), Attempt("3"), Attempt("4"),
                    Attempt("5", qualified: false, noWorse: true)
                },
                Drift());

            result.IsReliable.Should().BeTrue();
            result.QualifiedAttemptCount.Should().Be(4);
        }

        [Test]
        public void FewerThanFourQualifiedAttemptsCannotClaimReliability() {
            var result = TppaOperationalReliabilityCampaign.Evaluate(
                new List<TppaOperationalReliabilityAttempt> {
                    Attempt("1"), Attempt("2"), Attempt("3"),
                    Attempt("4", qualified: false), Attempt("5", qualified: false)
                }, Drift());

            result.IsReliable.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("only 3"));
        }

        [Test]
        public void UnsafeOrWorseLeftBehindAttemptFailsTheCampaign() {
            var result = TppaOperationalReliabilityCampaign.Evaluate(
                new List<TppaOperationalReliabilityAttempt> {
                    Attempt("1"), Attempt("2"), Attempt("3"), Attempt("4"),
                    Attempt("5", qualified: false, safe: false, noWorse: false)
                }, Drift());

            result.IsReliable.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("safety-gate"));
            result.Issues.Should().Contain(issue => issue.Contains("no-worse"));
        }

        [Test]
        public void RequiresPhotonBackedOutcomeRatherThanAlignmentOnly() {
            var result = TppaOperationalReliabilityCampaign.Evaluate(
                new List<TppaOperationalReliabilityAttempt> {
                    Attempt("1"), Attempt("2"), Attempt("3"), Attempt("4"), Attempt("5")
                }, new TppaOperationalOutcomeEvidence(
                    TppaOperationalOutcomeCheck.NotRun, false, null, "none"));

            result.IsReliable.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("photon-backed"));
        }
    }
}
