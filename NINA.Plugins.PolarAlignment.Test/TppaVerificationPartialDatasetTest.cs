using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System;
using System.Threading.Tasks;

namespace NINA.Plugins.PolarAlignment.Test {
    [TestFixture]
    public class TppaVerificationPartialDatasetTest {
        [Test]
        public void InterruptedRunPreservesOrderedSamplesWithoutAuthority() {
            var runId = Guid.NewGuid();
            var ledger = new TppaVerificationSampleLedger(runId, 8);

            var first = ledger.Append(Sample(runId, "East", 1, 3, 1.0));
            var second = ledger.Append(Sample(runId, "East", 2, 3, 2.0));
            var receipt = ledger.Snapshot(measurementCompleted: false);

            first.SequenceIndex.Should().Be(1);
            second.SequenceIndex.Should().Be(2);
            receipt.IsComplete.Should().BeFalse();
            receipt.CollectedSampleCount.Should().Be(2);
            receipt.ExpectedSampleCount.Should().Be(8);
            receipt.Issues.Should().ContainSingle(issue => issue.Contains("2 of 8"));
            receipt.GrantsMotionAuthority.Should().BeFalse();
            receipt.GrantsCompletionAuthority.Should().BeFalse();

            var json = JObject.Parse(receipt.ToJson());
            json["samples"]!.Count().Should().Be(2);
            json["grantsMotionAuthority"]!.Value<bool>().Should().BeFalse();
        }

        [Test]
        public void CompleteRunRequiresEveryExpectedSample() {
            var runId = Guid.NewGuid();
            var ledger = new TppaVerificationSampleLedger(runId, 2);
            ledger.Append(Sample(runId, "West", 1, 2, 1.0));
            ledger.Append(Sample(runId, "West", 2, 2, 2.0));

            var receipt = ledger.Snapshot(measurementCompleted: true);

            receipt.IsComplete.Should().BeTrue();
            receipt.Issues.Should().BeEmpty();
        }

        [Test]
        public void ClaimedCompletionWithMissingSamplesFailsClosed() {
            var runId = Guid.NewGuid();
            var ledger = new TppaVerificationSampleLedger(runId, 2);
            ledger.Append(Sample(runId, "West", 1, 2, 1.0));

            var receipt = ledger.Snapshot(measurementCompleted: true);

            receipt.IsComplete.Should().BeFalse();
            receipt.Issues.Should().Contain(issue => issue.Contains("completion was claimed"));
        }

        [Test]
        public void RejectsMismatchedRunAndNonUtcObservation() {
            var runId = Guid.NewGuid();
            var ledger = new TppaVerificationSampleLedger(runId, 2);

            Action wrongRun = () => ledger.Append(Sample(Guid.NewGuid(), "East", 1, 2, 1.0));
            Action localTime = () => ledger.Append(
                Sample(runId, "East", 1, 2, 1.0) with { ObservationUtc = DateTime.Now });

            wrongRun.Should().Throw<ArgumentException>();
            localTime.Should().Throw<ArgumentException>();
        }

        [Test]
        public async Task CancellationPersistsPartialReceiptBeforePropagating() {
            var runId = Guid.NewGuid();
            var ledger = new TppaVerificationSampleLedger(runId, 8);
            ledger.Append(Sample(runId, "East", 1, 3, 1.0));
            var cancellation = new OperationCanceledException("induced timeout");
            TppaVerificationPartialDatasetReceipt? preserved = null;

            Func<Task> run = async () => await TppaVerificationReceiptRunner.Run(
                () => Task.FromException<string>(cancellation),
                ledger,
                receipt => preserved = receipt);

            var thrown = await run.Should().ThrowAsync<OperationCanceledException>();
            thrown.Which.Should().BeSameAs(cancellation);
            preserved.Should().NotBeNull();
            preserved!.IsComplete.Should().BeFalse();
            preserved.CollectedSampleCount.Should().Be(1);
            preserved.Issues.Should().ContainSingle(issue => issue.Contains("1 of 8"));
        }

        [Test]
        public void RunSummaryBindsTimingVerdictAndAccuracyDisclaimer() {
            var runId = Guid.NewGuid();
            var started = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
            var summary = TppaVerificationRunSummary.Create(
                runId,
                started,
                started.AddSeconds(287.5),
                repeatabilityPassed: true,
                reciprocityPassed: true,
                expectedSampleCount: 9,
                refractionAdjustmentEnabled: true,
                overdeterminedShadowModelCheck: false,
                effectivePointSettleSeconds: 30.0,
                initialAzimuthMinutes: 0.3,
                initialAltitudeMinutes: -0.6,
                initialTotalMinutes: Math.Sqrt(0.45),
                reciprocalAzimuthMinutes: 0.4,
                reciprocalAltitudeMinutes: -0.7,
                reciprocalTotalMinutes: Math.Sqrt(0.65),
                repeatedForwardAzimuthMinutes: 0.2,
                repeatedForwardAltitudeMinutes: -0.5,
                repeatedForwardTotalMinutes: Math.Sqrt(0.29),
                repeatabilityVectorSeparationMinutes: 0.2,
                reciprocityVectorSeparationMinutes: 0.3);

            summary.ComponentTotalsConsistent.Should().BeTrue();
            summary.DiagnosticPassed.Should().BeTrue();
            summary.ElapsedSeconds.Should().Be(287.5);
            summary.GrantsMotionAuthority.Should().BeFalse();
            summary.GrantsAbsoluteAccuracyClaim.Should().BeFalse();
            var json = JObject.Parse(summary.ToJson());
            json["schemaVersion"]!.Value<int>().Should().Be(2);
            json["effectivePointSettleSeconds"]!.Value<double>().Should().Be(30.0);
            json["initialAzimuthMinutes"]!.Value<double>().Should().Be(0.3);
            json["repeatedForwardAltitudeMinutes"]!.Value<double>().Should().Be(-0.5);
            Guid.Parse(json["runId"]!.Value<string>()!).Should().Be(runId);
            json["grantsAbsoluteAccuracyClaim"]!.Value<bool>().Should().BeFalse();
        }

        [Test]
        public void RunSummaryPreservesButFlagsComponentsThatDoNotReproduceTotal() {
            var utc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
            var summary = TppaVerificationRunSummary.Create(
                Guid.NewGuid(), utc, utc.AddSeconds(120), true, true, 9, true, false,
                30, 3, 4, 6, 3, 4, 5, 3, 4, 5, 0, 0);

            summary.ComponentTotalsConsistent.Should().BeFalse();
            JObject.Parse(summary.ToJson())["componentTotalsConsistent"]!.Value<bool>()
                .Should().BeFalse();
        }

        [Test]
        public void RunSummaryRejectsNonUtcOrBackwardsTiming() {
            var runId = Guid.NewGuid();
            var utc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);

            Action nonUtc = () => TppaVerificationRunSummary.Create(
                runId, DateTime.Now, utc, true, true, 9, true, false,
                30, 1, 1, Math.Sqrt(2), 1, 1, Math.Sqrt(2), 1, 1, Math.Sqrt(2), 0, 0);
            Action backwards = () => TppaVerificationRunSummary.Create(
                runId, utc, utc.AddSeconds(-1), true, true, 9, true, false,
                30, 1, 1, Math.Sqrt(2), 1, 1, Math.Sqrt(2), 1, 1, Math.Sqrt(2), 0, 0);

            nonUtc.Should().Throw<ArgumentException>();
            backwards.Should().Throw<ArgumentException>();
        }

        private static TppaVerificationPointReceipt Sample(
                Guid runId,
                string direction,
                int point,
                int count,
                double seed) => new(
            TppaVerificationPointReceipt.CurrentSchemaVersion,
            runId,
            SequenceIndex: 0,
            direction,
            point,
            count,
            new DateTime(2026, 8, 1, 0, 0, point, DateTimeKind.Utc),
            330 + seed,
            35 + seed,
            300 + seed,
            60 + seed,
            45 + seed,
            0.7 + seed / 100,
            0.4 + seed / 100,
            0.5 + seed / 100);
    }
}
