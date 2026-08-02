using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace NINA.Plugins.PolarAlignment {
    internal static class TppaLoadedAssemblyEvidenceFactory {
        public static TppaLoadedAssemblyEvidence Capture(Assembly assembly) {
            ArgumentNullException.ThrowIfNull(assembly);
            var location = assembly.Location;
            if (string.IsNullOrWhiteSpace(location)) {
                throw new InvalidOperationException(
                    $"Loaded assembly '{assembly.FullName}' has no file location.");
            }
            location = Path.GetFullPath(location);
            if (!File.Exists(location)) {
                throw new FileNotFoundException(
                    "Loaded assembly file does not exist.",
                    location);
            }

            var name = assembly.GetName();
            var assemblyVersion = name.Version?.ToString(4);
            var informationalVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(name.Name)
                    || string.IsNullOrWhiteSpace(assemblyVersion)
                    || string.IsNullOrWhiteSpace(informationalVersion)) {
                throw new InvalidOperationException(
                    $"Loaded assembly '{assembly.FullName}' has incomplete version identity.");
            }

            using var stream = File.OpenRead(location);
            var sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            return new(
                name.Name,
                assemblyVersion,
                informationalVersion,
                location,
                sha256,
                assembly.ManifestModule.ModuleVersionId.ToString("D"));
        }
    }
}
