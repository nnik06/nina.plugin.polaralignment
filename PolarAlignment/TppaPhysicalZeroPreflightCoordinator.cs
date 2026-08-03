using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugins.PolarAlignment {
    internal interface IUpasSupervisorPhysicalZeroReturnExecutor {
        Task<UpasSupervisorPhysicalZeroReturnResult> ReturnAsync(
            string preEvidenceId,
            double currentTemperatureC,
            double requiredTravelDegrees,
            string currentLoadProfileId,
            CancellationToken token);
    }

    internal sealed record UpasSupervisorPhysicalZeroReturnResult(
        bool IsCompleted,
        string TransactionId,
        string Reason,
        string CampaignId,
        long CampaignExpiresMonotonicNs);

    internal sealed record TppaPhysicalZeroPreflightResult(
        bool ReturnWasRequired,
        string TransactionId,
        string CampaignId,
        long CampaignExpiresMonotonicNs,
        TppaPhysicalZeroAdmissionDecision Admission);

    internal sealed class TppaPhysicalZeroPreflightCoordinator {
        private readonly IUpasSupervisorCoarseEvidenceSource evidenceSource;
        private readonly IUpasSupervisorPhysicalZeroReturnExecutor returnExecutor;

        public TppaPhysicalZeroPreflightCoordinator(
                IUpasSupervisorCoarseEvidenceSource evidenceSource,
                IUpasSupervisorPhysicalZeroReturnExecutor returnExecutor) {
            this.evidenceSource = evidenceSource
                ?? throw new ArgumentNullException(nameof(evidenceSource));
            this.returnExecutor = returnExecutor
                ?? throw new ArgumentNullException(nameof(returnExecutor));
        }

        public async Task<TppaPhysicalZeroPreflightResult> RunAsync(
                Guid? expectedCallerLeaseId,
                double currentTemperatureC,
                string currentLoadProfileId,
                CancellationToken token) {
            var before = await evidenceSource.GetAsync(
                expectedCallerLeaseId,
                currentTemperatureC,
                currentLoadProfileId,
                token).ConfigureAwait(false);
            var initialAdmission = TppaPhysicalZeroAdmissionPolicy.Evaluate(before);
            var returnWasRequired = !initialAdmission.IsEligible;
            var requiredTravelDegrees = returnWasRequired
                ? initialAdmission.AzimuthAbsoluteBoundDegrees
                    + initialAdmission.AltitudeAbsoluteBoundDegrees
                    + 2.0 * TppaPhysicalZeroAdmissionPolicy.ZeroToleranceDegrees
                : 2.0 * TppaPhysicalZeroAdmissionPolicy.ZeroToleranceDegrees;
            var returned = await returnExecutor.ReturnAsync(
                initialAdmission.EvidenceId,
                currentTemperatureC,
                requiredTravelDegrees,
                currentLoadProfileId,
                token).ConfigureAwait(false);
            if (returned == null || !returned.IsCompleted
                    || string.IsNullOrWhiteSpace(returned.TransactionId)
                    || string.IsNullOrWhiteSpace(returned.CampaignId)
                    || returned.CampaignExpiresMonotonicNs <= 0) {
                throw new InvalidOperationException(
                    "UPAS supervisor did not complete a witnessed physical-zero return: "
                    + (returned?.Reason ?? "no transaction result"));
            }

            var after = await evidenceSource.GetAsync(
                expectedCallerLeaseId,
                currentTemperatureC,
                currentLoadProfileId,
                token).ConfigureAwait(false);
            if (string.Equals(before.Envelope.EvidenceId, after.Envelope.EvidenceId,
                    StringComparison.Ordinal)) {
                throw new InvalidOperationException(
                    "UPAS supervisor reused the pre-return evidence identity; fresh physical-zero "
                    + "re-verification was not proven.");
            }

            var finalAdmission = TppaPhysicalZeroAdmissionPolicy.Evaluate(after);
            if (!finalAdmission.IsEligible) {
                throw new InvalidOperationException(
                    "UPAS supervisor completed its return transaction, but fresh physical evidence "
                    + "still rejects TPPA admission: " + finalAdmission.Reason);
            }
            return new(returnWasRequired, returned.TransactionId, returned.CampaignId,
                returned.CampaignExpiresMonotonicNs, finalAdmission);
        }
    }
}
