using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugins.PolarAlignment {
    internal sealed record TppaCoarseDeterminationDraft(
        Guid DeterminationId,
        DateTime StartedUtc,
        DateTime CompletedUtc,
        string TargetSkyArcId,
        bool TruePoleRefractionEnabled,
        string RepositoryHead,
        string PluginAssemblySha256,
        string HardwareConfigurationId,
        string MechanicalStateSha256,
        string SolverIdentity,
        string CatalogIdentity,
        IReadOnlyList<TppaCoarseSourceSolveEvidence> SourceSolves,
        double AzimuthErrorMinutes,
        double AltitudeErrorMinutes,
        double CovarianceAzAzSquareMinutes,
        double CovarianceAzAltSquareMinutes,
        double CovarianceAltAltSquareMinutes);

    internal sealed class TppaObservedDeterminationCoordinator {
        private readonly IUpasSupervisorTppaObservationClient observationClient;
        private readonly TimeSpan leaseDuration;

        public TppaObservedDeterminationCoordinator(
                IUpasSupervisorTppaObservationClient observationClient,
                TimeSpan leaseDuration) {
            this.observationClient = observationClient
                ?? throw new ArgumentNullException(nameof(observationClient));
            if (leaseDuration <= TimeSpan.Zero) {
                throw new ArgumentOutOfRangeException(nameof(leaseDuration));
            }
            this.leaseDuration = leaseDuration;
        }

        private static TppaCoarseDeterminationEvidence ToEvidence(
                TppaCoarseDeterminationDraft draft,
                string observationLeaseNonce,
                string observationAttestationSha256) => new(
            draft.DeterminationId,
            observationLeaseNonce,
            observationAttestationSha256,
            draft.StartedUtc,
            draft.CompletedUtc,
            draft.TargetSkyArcId,
            draft.TruePoleRefractionEnabled,
            draft.RepositoryHead,
            draft.PluginAssemblySha256,
            draft.HardwareConfigurationId,
            draft.MechanicalStateSha256,
            draft.SolverIdentity,
            draft.CatalogIdentity,
            draft.SourceSolves,
            draft.AzimuthErrorMinutes,
            draft.AltitudeErrorMinutes,
            draft.CovarianceAzAzSquareMinutes,
            draft.CovarianceAzAltSquareMinutes,
            draft.CovarianceAltAltSquareMinutes);
        public async Task<TppaCoarseDeterminationEvidence> AcquireAsync(
                Guid campaignId,
                Func<UpasSupervisorTppaObservationLease, CancellationToken,
                    Task<TppaCoarseDeterminationDraft>> acquireDraft,
                CancellationToken token) {
            if (campaignId == Guid.Empty) {
                throw new ArgumentException("Campaign identity is required.", nameof(campaignId));
            }
            if (acquireDraft == null) {
                throw new ArgumentNullException(nameof(acquireDraft));
            }

            UpasSupervisorTppaObservationLease lease = null;
            var closed = false;
            try {
                lease = await observationClient.OpenAsync(
                    campaignId, leaseDuration, token).ConfigureAwait(false);
                var draft = await acquireDraft(lease, token).ConfigureAwait(false)
                    ?? throw new InvalidOperationException(
                        "TPPA observation acquisition returned no determination draft.");
                var candidate = ToEvidence(
                    draft, lease.Nonce, new string('0', 64));
                _ = TppaCoarseDeterminationReceiptBuilder.Build(
                    candidate, DateTime.UtcNow);
                var captureDigest = TppaCoarseDeterminationReceiptBuilder
                    .BuildObservationCaptureDigest(
                        draft.DeterminationId, lease.Nonce, draft.SourceSolves);
                var attestation = await observationClient.CloseAsync(
                    lease, captureDigest, token).ConfigureAwait(false);
                closed = true;
                return candidate with {
                    ObservationAttestationSha256 = attestation.AttestationSha256
                };
            } catch (Exception acquisitionFailure) {
                if (lease == null || closed) {
                    throw;
                }
                try {
                    await observationClient.AbortAsync(
                        lease, CancellationToken.None).ConfigureAwait(false);
                } catch (Exception abortFailure) {
                    throw new AggregateException(
                        "TPPA determination failed and its supervisor observation lease could not be aborted.",
                        acquisitionFailure,
                        abortFailure);
                }
                throw;
            }
        }
    }
}