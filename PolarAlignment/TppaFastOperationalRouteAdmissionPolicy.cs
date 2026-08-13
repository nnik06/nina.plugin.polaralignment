namespace NINA.Plugins.PolarAlignment {
    internal readonly record struct TppaFastOperationalRouteAdmissionDecision(
        bool IsEligible,
        string Reason);

    /// <summary>
    /// Keeps the time-bounded route from computing a correction through an
    /// unmeasured UPAS axis. A session may start from a measured 2x2 response,
    /// or from a measured X response with a physically bounded Y probe.
    /// </summary>
    internal static class TppaFastOperationalRouteAdmissionPolicy {
        public static TppaFastOperationalRouteAdmissionDecision Evaluate(
                bool fastMotionRequested,
                bool qualifiedDirectFullTravelRoute,
                bool qualifiedDirectBootstrapRoute) {
            if (!fastMotionRequested) {
                return new(true, "The five-minute direct-motion route is not active.");
            }

            if (qualifiedDirectFullTravelRoute) {
                return new(true, "The measured 2x2 direct UPAS response is qualified.");
            }

            if (qualifiedDirectBootstrapRoute) {
                return new(true, "The measured X response and bounded Y bootstrap are qualified.");
            }

            return new(false,
                "five-minute UPAS motion requires either a measured 2x2 response or a measured X response with a physically bounded Y bootstrap; no correction was sized from an unmeasured Y axis");
        }
    }
}
