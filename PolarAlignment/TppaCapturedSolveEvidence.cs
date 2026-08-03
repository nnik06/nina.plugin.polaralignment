using System;
using NINA.Core.Enum;

namespace NINA.Plugins.PolarAlignment {
    internal sealed record TppaCapturedSolveEvidence(
        Guid SolveId,
        string SourceImageSha256,
        string SolverOutputSha256,
        DateTime ExposureStartedUtc,
        int ExposureDurationMilliseconds,
        DateTime ObservationMidpointUtc,
        DateTime MountStateObservedUtc,
        bool MountConnected,
        bool TrackingEnabled,
        bool Slewing,
        double SolvedRightAscensionDegrees,
        double SolvedDeclinationDegrees,
        PierSide PierSide,
        string SolverIdentity);
}
