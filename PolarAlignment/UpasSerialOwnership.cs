using System;
using System.IO;

namespace NINA.Plugins.PolarAlignment {
    /// <summary>Same byte-range lock as Hall; held before discovery until port disposal.</summary>
    internal sealed class UpasSerialOwnership : IDisposable {
        private FileStream stream;
        public static string DefaultPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "UPAS", "grbl-owner.lock");

        public UpasSerialOwnership(string path) {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var candidate = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
            try {
                // Lock beyond EOF is valid on Windows; never truncate/delete the shared file.
                candidate.Lock(0, 1);
                stream = candidate;
            } catch {
                candidate.Dispose();
                throw new IOException("UPAS_SERIAL_OWNER_BUSY: no serial discovery or command was attempted");
            }
        }

        public void Dispose() {
            var owned = stream;
            stream = null;
            if (owned == null) return;
            try { owned.Unlock(0, 1); } finally { owned.Dispose(); }
        }
    }
}
