namespace NINA.Plugins.PolarAlignment {
    internal static class BacklashCompensationPlanner {
        public static float CreatePreloadMove(float compensation, LastDirection targetDirection) {
            var directionSign = targetDirection == LastDirection.Positive ? 1f : -1f;
            return directionSign * compensation;
        }
    }
}