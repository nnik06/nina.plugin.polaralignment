namespace NINA.Plugins.PolarAlignment {
    internal static class TppaAlignmentSystemSelectionPolicy {
        internal static string ResolvePersistedSelection(string selectedSystem, bool legacyUpasEnabled) {
            var selectorIsUnset = string.IsNullOrWhiteSpace(selectedSystem)
                                || string.Equals(selectedSystem,
                                    PolarAlignmentSystemType.None.ToString(),
                                    System.StringComparison.OrdinalIgnoreCase);
            if (!legacyUpasEnabled || !selectorIsUnset) {
                return selectedSystem;
            }

            return PolarAlignmentSystemType.UPAS.ToString();
        }
    }
}
