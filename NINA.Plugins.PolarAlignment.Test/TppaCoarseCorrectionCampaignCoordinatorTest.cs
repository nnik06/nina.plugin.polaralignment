using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaCoarseCorrectionCampaignCoordinatorTest {
        private static readonly Guid CampaignId =
            Guid.Parse("50000000-0000-4000-8000-000000000001");

        [Test]
        public async Task AcquiresTwoSequentialIndependentObservationsBeforeExecution() {
            var observed = new RecordingObservedCoordinator(
                Evidence(1), Evidence(2));
            var executor = new RecordingExecutor();
            var coordinator = new TppaCoarseCorrectionCampaignCoordinator(
                observed, executor);
            var requested = new List<int>();

            var result = await coordinator.AcquirePairAndExecuteAsync(
                CampaignId,
                (index, _, _) => {
                    requested.Add(index);
                    return Task.FromResult(Draft(index));
                },
                35.0,
                2.0,
                "hae29c-ec-full-rig-v1",
                CancellationToken.None);

            requested.Should().Equal(1, 2);
            observed.Calls.Should().Be(2);
            executor.Calls.Should().Be(1);
            executor.First!.DeterminationId.Should().Be(Evidence(1).DeterminationId);
            executor.Second!.DeterminationId.Should().Be(Evidence(2).DeterminationId);
            result.Completed.Should().BeTrue();
        }

        [Test]
        public async Task SecondObservationFailureNeverInvokesMotionExecutor() {
            var observed = new RecordingObservedCoordinator(
                Evidence(1), new InvalidOperationException("second solve failed"));
            var executor = new RecordingExecutor();
            var coordinator = new TppaCoarseCorrectionCampaignCoordinator(
                observed, executor);

            var action = () => coordinator.AcquirePairAndExecuteAsync(
                CampaignId,
                (index, _, _) => Task.FromResult(Draft(index)),
                35.0,
                2.0,
                "hae29c-ec-full-rig-v1",
                CancellationToken.None);

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("second solve failed");
            observed.Calls.Should().Be(2);
            executor.Calls.Should().Be(0);
        }

        [Test]
        public async Task AliasedObservationEvidenceNeverInvokesMotionExecutor() {
            var first = Evidence(1);
            var observed = new RecordingObservedCoordinator(
                first,
                Evidence(2) with { ObservationLeaseNonce = first.ObservationLeaseNonce });
            var executor = new RecordingExecutor();
            var coordinator = new TppaCoarseCorrectionCampaignCoordinator(
                observed, executor);

            var action = () => coordinator.AcquirePairAndExecuteAsync(
                CampaignId,
                (index, _, _) => Task.FromResult(Draft(index)),
                35.0,
                2.0,
                "hae29c-ec-full-rig-v1",
                CancellationToken.None);

            await action.Should().ThrowAsync<ArgumentException>()
                .WithMessage("*attestations must be independent*");
            executor.Calls.Should().Be(0);
        }

        private static TppaCoarseDeterminationDraft Draft(int index) {
            var evidence = Evidence(index);
            return new(
                evidence.DeterminationId,
                evidence.StartedUtc,
                evidence.CompletedUtc,
                evidence.TargetSkyArcId,
                evidence.TruePoleRefractionEnabled,
                evidence.RepositoryHead,
                evidence.PluginAssemblySha256,
                evidence.HardwareConfigurationId,
                evidence.MechanicalStateSha256,
                evidence.SolverIdentity,
                evidence.CatalogIdentity,
                evidence.SourceSolves,
                evidence.AzimuthErrorMinutes,
                evidence.AltitudeErrorMinutes,
                evidence.CovarianceAzAzSquareMinutes,
                evidence.CovarianceAzAltSquareMinutes,
                evidence.CovarianceAltAltSquareMinutes);
        }

        private static TppaCoarseDeterminationEvidence Evidence(int index) {
            var baseTime = new DateTime(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc)
                .AddSeconds(index * 20);
            var source = Enumerable.Range(0, 3).Select(solveIndex => {
                var character = (char)('1' + (index - 1) * 6 + solveIndex);
                var outputCharacter = (char)('a' + (index - 1) * 3 + solveIndex);
                var exposure = baseTime.AddSeconds(1 + solveIndex * 2);
                return new TppaCoarseSourceSolveEvidence(
                    Guid.Parse($"60000000-0000-4000-8000-{index:D6}{solveIndex:D6}"),
                    new string(character, 64),
                    new string(outputCharacter, 64),
                    exposure,
                    1000,
                    exposure.AddMilliseconds(500),
                    exposure.AddMilliseconds(-100),
                    true,
                    false,
                    10.0 + solveIndex,
                    20.0,
                    "pierEast");
            }).ToArray();
            return new(
                Guid.Parse($"50000000-0000-4000-8000-{index:D12}"),
                new string((char)('a' + index - 1), 64),
                new string((char)('c' + index - 1), 64),
                baseTime,
                baseTime.AddSeconds(8),
                "safe-arc-a",
                true,
                new string('1', 40),
                new string('2', 64),
                "hae29c-ec-full-rig-v1",
                new string('3', 64),
                "astap-test",
                "gaia-dr3",
                source,
                12.0,
                -8.0,
                0.04,
                0.0,
                0.04);
        }

        private sealed class RecordingObservedCoordinator
                : ITppaObservedDeterminationCoordinator {
            private readonly Queue<object> outcomes;

            public RecordingObservedCoordinator(params object[] outcomes) {
                this.outcomes = new Queue<object>(outcomes);
            }

            public int Calls { get; private set; }

            public async Task<TppaCoarseDeterminationEvidence> AcquireAsync(
                    Guid campaignId,
                    Func<UpasSupervisorTppaObservationLease, CancellationToken,
                        Task<TppaCoarseDeterminationDraft>> acquireDraft,
                    CancellationToken token) {
                Calls++;
                var lease = new UpasSupervisorTppaObservationLease(
                    Guid.NewGuid().ToString("D"),
                    new string((char)('a' + Calls - 1), 64),
                    campaignId,
                    Guid.NewGuid().ToString("D"),
                    1,
                    1,
                    1000,
                    76000);
                _ = await acquireDraft(lease, token);
                var outcome = outcomes.Dequeue();
                if (outcome is Exception failure) {
                    throw failure;
                }
                return (TppaCoarseDeterminationEvidence)outcome;
            }
        }

        private sealed class RecordingExecutor : IUpasSupervisorCoarseTppaExecutor {
            public int Calls { get; private set; }
            public TppaCoarseDeterminationEvidence? First { get; private set; }
            public TppaCoarseDeterminationEvidence? Second { get; private set; }

            public Task<UpasSupervisorCoarseTppaResult> ExecuteAsync(
                    TppaCoarseDeterminationEvidence first,
                    TppaCoarseDeterminationEvidence second,
                    double currentTemperatureC,
                    double requiredTravelDegrees,
                    string currentLoadProfileId,
                    CancellationToken token) {
                Calls++;
                First = first;
                Second = second;
                return Task.FromResult(new UpasSupervisorCoarseTppaResult(
                    true, true, "transaction", "COMPLETED"));
            }
        }
    }
}