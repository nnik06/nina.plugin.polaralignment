using System;
using System.Threading.Tasks;

namespace NINA.Plugins.PolarAlignment {
    internal static class TppaFastActuatorAdmissionGate {
        public static void EnsureAuthorized(
                bool operationalMotionProtocolActive,
                bool initialAdmissionGranted,
                string operation) {
            if (operationalMotionProtocolActive && !initialAdmissionGranted) {
                throw new InvalidOperationException(
                    $"Fast-alignment actuator {operation} was denied because the fresh initial determination has not granted admission.");
            }
        }

        public static async Task ExecuteIfAuthorizedAsync(
                bool operationalMotionProtocolActive,
                bool initialAdmissionGranted,
                string operation,
                Func<Task> action) {
            EnsureAuthorized(operationalMotionProtocolActive, initialAdmissionGranted, operation);
            await action().ConfigureAwait(false);
        }

        public static async Task<T> ExecuteIfAuthorizedAsync<T>(
                bool operationalMotionProtocolActive,
                bool initialAdmissionGranted,
                string operation,
                Func<Task<T>> action) {
            EnsureAuthorized(operationalMotionProtocolActive, initialAdmissionGranted, operation);
            return await action().ConfigureAwait(false);
        }
    }
}
