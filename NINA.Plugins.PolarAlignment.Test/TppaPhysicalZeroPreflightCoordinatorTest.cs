using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaPhysicalZeroPreflightCoordinatorTest {
        [Test]
        public async Task AlreadyZeroDoesNotRequestMotion() {
            var evidence = new QueueEvidenceSource(Parse(0.02, -0.02, "b"));
            var executor = new RecordingReturnExecutor();

            var result = await new TppaPhysicalZeroPreflightCoordinator(evidence, executor)
                .RunAsync(null, 35.0, "hae29c-ec-full-rig-v1", CancellationToken.None);

            result.ReturnWasRequired.Should().BeFalse();
            result.Admission.IsEligible.Should().BeTrue();
            executor.Calls.Should().Be(0);
            evidence.Calls.Should().Be(1);
        }

        [Test]
        public async Task NonZeroReturnsOnceAndRequiresFreshZeroEvidence() {
            var evidence = new QueueEvidenceSource(
                Parse(0.4, -0.3, "b"),
                Parse(0.02, -0.02, "c"));
            var executor = new RecordingReturnExecutor();

            var result = await new TppaPhysicalZeroPreflightCoordinator(evidence, executor)
                .RunAsync(null, 35.0, "hae29c-ec-full-rig-v1", CancellationToken.None);

            result.ReturnWasRequired.Should().BeTrue();
            result.TransactionId.Should().Be("zero-transaction");
            result.Admission.IsEligible.Should().BeTrue();
            executor.Calls.Should().Be(1);
            executor.PreEvidenceId.Should().Be(new string('b', 64));
            evidence.Calls.Should().Be(2);
        }

        [Test]
        public async Task RejectsIncompleteReturnWithoutSecondEvidenceQuery() {
            var evidence = new QueueEvidenceSource(Parse(0.4, 0.0, "b"));
            var executor = new RecordingReturnExecutor(completed: false);

            var action = () => new TppaPhysicalZeroPreflightCoordinator(evidence, executor)
                .RunAsync(null, 35.0, "hae29c-ec-full-rig-v1", CancellationToken.None);

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*did not complete*");
            evidence.Calls.Should().Be(1);
        }

        [Test]
        public async Task RejectsReusedEvidenceIdentity() {
            var evidence = new QueueEvidenceSource(
                Parse(0.4, 0.0, "b"),
                Parse(0.0, 0.0, "b"));

            var action = () => new TppaPhysicalZeroPreflightCoordinator(
                    evidence, new RecordingReturnExecutor())
                .RunAsync(null, 35.0, "hae29c-ec-full-rig-v1", CancellationToken.None);

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*reused*evidence identity*");
        }

        [Test]
        public async Task RejectsFreshEvidenceThatRemainsOutsideZero() {
            var evidence = new QueueEvidenceSource(
                Parse(0.4, 0.0, "b"),
                Parse(0.2, 0.0, "c"));

            var action = () => new TppaPhysicalZeroPreflightCoordinator(
                    evidence, new RecordingReturnExecutor())
                .RunAsync(null, 35.0, "hae29c-ec-full-rig-v1", CancellationToken.None);

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*still rejects TPPA admission*");
        }

        private static UpasSupervisorCoarsePlanningEvidence Parse(
                double az, double alt, string evidenceCharacter) {
            var uncertainty = 0.01;
            var json = UpasSupervisorCoarsePlanningEvidenceTest.ValidJson()
                .Replace(new string('b', 64), new string(evidenceCharacter[0], 64))
                .Replace("\"positionDegrees\": 0.25", $"\"positionDegrees\": {az:R}")
                .Replace("\"positionDegrees\": -0.25", $"\"positionDegrees\": {alt:R}")
                .Replace("[0.0001, 0.0]", $"[{uncertainty * uncertainty:R}, 0.0]")
                .Replace("[0.0, 0.0001]", $"[0.0, {uncertainty * uncertainty:R}]");
            return UpasSupervisorCoarsePlanningEvidenceParser.ParseAuthenticated(
                json,
                new string('a', 64),
                100.0,
                null,
                35.0,
                "hae29c-ec-full-rig-v1",
                UpasSupervisorCoarsePlanningEvidenceParser.Digest(json),
                new string(evidenceCharacter[0], 64),
                UpasSupervisorCoarsePlanningEvidenceParser.RequiredAuthenticationMethod);
        }

        private sealed class QueueEvidenceSource : IUpasSupervisorCoarseEvidenceSource {
            private readonly Queue<UpasSupervisorCoarsePlanningEvidence> values;
            public QueueEvidenceSource(params UpasSupervisorCoarsePlanningEvidence[] values) =>
                this.values = new(values);
            public int Calls { get; private set; }
            public Task<UpasSupervisorCoarsePlanningEvidence> GetAsync(
                    Guid? expectedCallerLeaseId, double currentTemperatureC,
                    string currentLoadProfileId, CancellationToken token) {
                Calls++;
                return Task.FromResult(values.Dequeue());
            }
        }

        private sealed class RecordingReturnExecutor : IUpasSupervisorPhysicalZeroReturnExecutor {
            private readonly bool completed;
            public RecordingReturnExecutor(bool completed = true) => this.completed = completed;
            public int Calls { get; private set; }
            public string PreEvidenceId { get; private set; } = string.Empty;
            public Task<UpasSupervisorPhysicalZeroReturnResult> ReturnAsync(
                    string preEvidenceId, double currentTemperatureC,
                    string currentLoadProfileId, CancellationToken token) {
                Calls++;
                PreEvidenceId = preEvidenceId;
                return Task.FromResult(new UpasSupervisorPhysicalZeroReturnResult(
                    completed,
                    completed ? "zero-transaction" : null,
                    completed ? "completed" : "denied"));
            }
        }
    }
}
