using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugins.PolarAlignment {
    internal sealed class TppaCoarseCorrectionCampaignCoordinator {
        private readonly ITppaObservedDeterminationCoordinator observations;
        private readonly IUpasSupervisorCoarseTppaExecutor executor;

        public TppaCoarseCorrectionCampaignCoordinator(
                ITppaObservedDeterminationCoordinator observations,
                IUpasSupervisorCoarseTppaExecutor executor) {
            this.observations = observations
                ?? throw new ArgumentNullException(nameof(observations));
            this.executor = executor
                ?? throw new ArgumentNullException(nameof(executor));
        }

        public async Task<UpasSupervisorCoarseTppaResult> AcquirePairAndExecuteAsync(
                Guid campaignId,
                Func<int, UpasSupervisorTppaObservationLease, CancellationToken,
                    Task<TppaCoarseDeterminationDraft>> acquireDraft,
                double currentTemperatureC,
                double requiredTravelDegrees,
                string currentLoadProfileId,
                CancellationToken token) {
            if (campaignId == Guid.Empty) {
                throw new ArgumentException(
                    "Campaign identity is required.", nameof(campaignId));
            }
            if (acquireDraft == null) {
                throw new ArgumentNullException(nameof(acquireDraft));
            }

            var first = await observations.AcquireAsync(
                campaignId,
                (lease, innerToken) => acquireDraft(1, lease, innerToken),
                token).ConfigureAwait(false);
            var second = await observations.AcquireAsync(
                campaignId,
                (lease, innerToken) => acquireDraft(2, lease, innerToken),
                token).ConfigureAwait(false);
            TppaCoarseDeterminationReceiptBuilder.ValidateIndependent(first, second);
            return await executor.ExecuteAsync(
                first,
                second,
                currentTemperatureC,
                requiredTravelDegrees,
                currentLoadProfileId,
                token).ConfigureAwait(false);
        }
    }
}