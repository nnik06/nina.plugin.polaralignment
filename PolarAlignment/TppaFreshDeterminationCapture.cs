using System.Collections.Generic;
using NINA.PlateSolving;

namespace NINA.Plugins.PolarAlignment {
    internal sealed record TppaFreshDeterminationCapture(
        PolarErrorDetermination Determination,
        IReadOnlyList<PlateSolveResult> SourceSolves);

    internal sealed record TppaObservedPolarErrorDetermination(
        PolarErrorDetermination Determination,
        TppaCoarseDeterminationEvidence Evidence);
}