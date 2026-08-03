using System;
using System.Collections.Generic;
using System.Linq;
using NINA.Core.Enum;

namespace NINA.Plugins.PolarAlignment {
    internal static class TppaCoarseDeterminationDraftFactory {
        internal static TppaCoarseDeterminationDraft Create(
                Guid determinationId,
                DateTime startedUtc,
                DateTime completedUtc,
                bool truePoleRefractionEnabled,
                TppaCommissionedCovarianceAuthority authority,
                IReadOnlyList<TppaCapturedSolveEvidence> capturedSolves,
                double azimuthErrorMinutes,
                double altitudeErrorMinutes) {
            if (authority == null) {
                throw new ArgumentNullException(nameof(authority));
            }
            if (!truePoleRefractionEnabled) {
                throw new InvalidOperationException(
                    "Coarse TPPA observations require true-pole refraction adjustment.");
            }
            if (capturedSolves?.Count != 3 || capturedSolves.Any(item => item == null)) {
                throw new InvalidOperationException(
                    "Exactly three captured TPPA solves are required.");
            }

            var solverIdentities = capturedSolves
                .Select(item => item.SolverIdentity)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (solverIdentities.Length != 1
                    || solverIdentities[0] != authority.SolverIdentity) {
                throw new InvalidOperationException(
                    "Captured solve identity does not match the commissioned authority.");
            }

            var sourceSolves = capturedSolves.Select(ToSourceEvidence).ToArray();
            return new TppaCoarseDeterminationDraft(
                determinationId,
                startedUtc,
                completedUtc,
                authority.TargetSkyArcId,
                truePoleRefractionEnabled,
                authority.RepositoryHead,
                authority.PluginAssemblySha256,
                authority.HardwareConfigurationId,
                authority.MechanicalStateSha256,
                authority.SolverIdentity,
                authority.CatalogIdentity,
                sourceSolves,
                azimuthErrorMinutes,
                altitudeErrorMinutes,
                authority.CovarianceAzAzSquareMinutes,
                authority.CovarianceAzAltSquareMinutes,
                authority.CovarianceAltAltSquareMinutes);
        }

        private static TppaCoarseSourceSolveEvidence ToSourceEvidence(
                TppaCapturedSolveEvidence value) {
            if (!value.MountConnected || !value.TrackingEnabled || value.Slewing) {
                throw new InvalidOperationException(
                    "Captured TPPA solve lacks stationary tracked mount evidence.");
            }
            var pierSide = value.PierSide switch {
                PierSide.pierEast => "pierEast",
                PierSide.pierWest => "pierWest",
                _ => throw new InvalidOperationException(
                    "Captured TPPA solve has unknown pier side.")
            };
            return new TppaCoarseSourceSolveEvidence(
                value.SolveId,
                value.SourceImageSha256,
                value.SolverOutputSha256,
                value.ExposureStartedUtc,
                value.ExposureDurationMilliseconds,
                value.ObservationMidpointUtc,
                value.MountStateObservedUtc,
                value.TrackingEnabled,
                value.Slewing,
                value.SolvedRightAscensionDegrees,
                value.SolvedDeclinationDegrees,
                pierSide);
        }
    }
}
