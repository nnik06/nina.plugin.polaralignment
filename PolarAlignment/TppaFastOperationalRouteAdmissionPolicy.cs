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
                bool qualifiedDirectBootstrapRoute,
                bool qualifiedFirstRunBootstrapRoute = false) {
            if (!fastMotionRequested) {
                return new(true, "The five-minute direct-motion route is not active.");
            }

            if (qualifiedDirectFullTravelRoute) {
                return new(true, "The measured 2x2 direct UPAS response is qualified.");
            }

            if (qualifiedDirectBootstrapRoute) {
                return new(true, "The measured X response and bounded Y bootstrap are qualified.");
            }

            if (qualifiedFirstRunBootstrapRoute) {
                return new(true, "The attended first-run route may measure bounded X and Y responses before calculating a correction.");
            }

            return new(false,
                "five-minute UPAS motion requires a measured 2x2 response, a measured X response with bounded Y bootstrap, or an attended bounded first-run X/Y identification route; no correction was sized from an unmeasured Y axis");
        }
    }
}
