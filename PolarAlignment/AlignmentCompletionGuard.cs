using System;

namespace NINA.Plugins.PolarAlignment {
    internal sealed class AlignmentCompletionGuard {
        private readonly int requiredConsecutiveMeasurements;

        public AlignmentCompletionGuard(int requiredConsecutiveMeasurements = 2) {
            if (requiredConsecutiveMeasurements < 1) {
                throw new ArgumentOutOfRangeException(nameof(requiredConsecutiveMeasurements));
            }

            this.requiredConsecutiveMeasurements = requiredConsecutiveMeasurements;
        }

        public int ConsecutiveMeasurementsWithinTolerance { get; private set; }

        public bool AwaitingConfirmation => ConsecutiveMeasurementsWithinTolerance > 0
                                            && ConsecutiveMeasurementsWithinTolerance < requiredConsecutiveMeasurements;

        public bool Observe(double totalErrorMinutes, double toleranceMinutes) {
            var validTolerance = IsFinite(toleranceMinutes) && toleranceMinutes > 0;
            var validTotalError = IsFinite(totalErrorMinutes) && totalErrorMinutes >= 0;
            return ObserveQualifyingMeasurement(validTolerance
                                                 && validTotalError
                                                 && totalErrorMinutes <= toleranceMinutes);
        }

        private bool ObserveQualifyingMeasurement(bool qualifies) {
            if (!qualifies) {
                ConsecutiveMeasurementsWithinTolerance = 0;
                return false;
            }

            ConsecutiveMeasurementsWithinTolerance++;
            return ConsecutiveMeasurementsWithinTolerance >= requiredConsecutiveMeasurements;
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}