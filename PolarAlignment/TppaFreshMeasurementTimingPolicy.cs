namespace NINA.Plugins.PolarAlignment {
    internal static class TppaFreshMeasurementTimingPolicy {
        public static bool RequiresReturnField(
                bool operationalUpasProfile,
                bool manualMode,
                bool mountConnected) =>
            !operationalUpasProfile && !manualMode && mountConnected;
    }
}
