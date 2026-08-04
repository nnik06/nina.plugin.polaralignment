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
            string preregisteredCampaignId,
            CancellationToken token);
    }

    internal sealed record UpasSupervisorPhysicalZeroAdmission(
        string AdmissionSha256,
        string CampaignId,
        string ZeroReferenceId,
        string TransactionId,
        string TerminalEvidenceCoreSha256,
        string TerminalPlanSha256,
        long VerifiedStartedMonotonicNs,
        long VerifiedCompletedMonotonicNs,
        double AzimuthPositionDegrees,
        double AltitudePositionDegrees,
        double AzimuthAbsoluteBoundDegrees,
        double AltitudeAbsoluteBoundDegrees,
        double ZeroToleranceDegrees);

    internal sealed record UpasSupervisorPhysicalZeroReturnResult(
        bool IsCompleted,
        string TransactionId,
        string Reason,
        string CampaignId,
        long CampaignExpiresMonotonicNs,
        UpasSupervisorPhysicalZeroAdmission Admission);

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
                Guid preregisteredCampaignId,
                CancellationToken token) {
            if (preregisteredCampaignId == Guid.Empty) {
                throw new ArgumentException(
                    "A sealed preregistered campaign ID is required.",
                    nameof(preregisteredCampaignId));
            }
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
                preregisteredCampaignId.ToString("D"),
                token).ConfigureAwait(false);
            if (returned == null || !returned.IsCompleted
                    || string.IsNullOrWhiteSpace(returned.TransactionId)
                    || string.IsNullOrWhiteSpace(returned.CampaignId)
                    || returned.CampaignExpiresMonotonicNs <= 0
                    || returned.Admission == null) {
                throw new InvalidOperationException(
                    "UPAS supervisor did not complete a witnessed physical-zero return: "
                    + (returned?.Reason ?? "no transaction result"));
            }
            if (!string.Equals(returned.CampaignId,
                    preregisteredCampaignId.ToString("D"),
                    StringComparison.Ordinal)) {
                throw new InvalidOperationException(
                    "UPAS supervisor physical-zero response did not preserve the sealed "
                    + "preregistered campaign identity.");
            }
            if (!string.Equals(returned.Admission.CampaignId, returned.CampaignId,
                    StringComparison.Ordinal)
                    || !string.Equals(returned.Admission.TransactionId,
                        returned.TransactionId, StringComparison.Ordinal)) {
                throw new InvalidOperationException(
                    "UPAS supervisor terminal zero admission is not bound to the returned "
                    + "campaign and transaction.");
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
