namespace NINA.Plugins.PolarAlignment {
    internal sealed class JogCancellationConfirmationTracker {
        private readonly int requiredConfirmations;
        private (float X, float Y, float Z)? previousIdlePosition;

        public JogCancellationConfirmationTracker(int requiredConfirmations) {
            if (requiredConfirmations < 1) {
                throw new System.ArgumentOutOfRangeException(nameof(requiredConfirmations));
            }
            this.requiredConfirmations = requiredConfirmations;
        }

        public int Confirmations { get; private set; }

        public bool Observe(
                string status,
                (float X, float Y, float Z) currentPosition) {
            if (!UniversalPolarAlignmentBase.IsJogCancellationTerminalStatus(status)) {
                Confirmations = 0;
                previousIdlePosition = null;
                return false;
            }

            Confirmations = previousIdlePosition.HasValue
                    && UniversalPolarAlignmentBase.AreControllerPositionsStable(
                        previousIdlePosition.Value,
                        currentPosition)
                ? Confirmations + 1
                : 1;
            previousIdlePosition = currentPosition;
            return Confirmations >= requiredConfirmations;
        }
    }
}
