using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NINA.Plugins.PolarAlignment {
    internal sealed record TppaQualificationDeterminationInput(
        string DeterminationId,
        DateTime StartedUtc,
        DateTime CompletedUtc,
        IReadOnlyList<TppaVerificationPointReceipt> SourcePoints,
        Vector3 MountAxisVector,
        TppaThreePointGeometry Geometry);

    internal sealed record TppaQualificationRunProductionMetadata(
        string RunId,
        string SessionId,
        string ProducerId,
        string PipelineDigest,
        int CorrectionSequenceNumber,
        TppaRuntimeIdentityEvidence Identity,
        TppaRuntimeSiteEvidence Site,
        TppaRuntimeAtmosphereEvidence Atmosphere,
        bool RefractionAdjustmentEnabled);

    internal sealed record TppaQualificationRunEvidenceProductionResult(
        bool Produced,
        string EvidenceJson,
        string OutputPath,
        IReadOnlyList<string> QualificationIssues,
        IReadOnlyList<string> ProductionIssues);

    /// <summary>
    /// Converts the immutable VerificationOnly point receipts and fitted axes
    /// into the strict absolute-evidence run schema. It creates evidence only;
    /// it never grants motion, completion, or absolute-accuracy authority.
    /// </summary>
    internal static class TppaQualificationRunEvidenceProducer {
        private static readonly JsonSerializer EvidenceSerializer = JsonSerializer.Create(
            new JsonSerializerSettings {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Culture = CultureInfo.InvariantCulture,
                DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind,
                NullValueHandling = NullValueHandling.Include
            });

        public static TppaQualificationRunEvidenceProductionResult Produce(
                TppaQualificationRunProductionMetadata metadata,
                IReadOnlyList<TppaQualificationDeterminationInput> determinations,
                string outputDirectory) {
            var productionIssues = Validate(metadata, determinations, outputDirectory);
            if (productionIssues.Count > 0) {
                return new(false, null, null, Array.Empty<string>(), productionIssues);
            }

            try {
                var runtimeDeterminations = determinations.Select(ToRuntimeEvidence).ToArray();
                var runtimeEvidence = new TppaFastQualificationRuntimeEvidence(
                    runtimeDeterminations,
                    metadata.Identity,
                    metadata.Site,
                    metadata.Atmosphere,
                    IndependentWitness: null,
                    metadata.RefractionAdjustmentEnabled,
                    PhysicalAdjustmentCommandCount: 0);
                var derived = TppaFastQualificationRuntimeAdapter.Derive(runtimeEvidence);
                var input = derived.QualificationInput;
                var targetPole = TppaFastQualificationRuntimeAdapter.TruePoleVector(
                    metadata.Site.LatitudeDegrees);
                var evidenceDeterminations = runtimeDeterminations
                    .Select((determination, index) =>
                        new TppaQualificationDeterminationEvidence(
                            determinations[index].DeterminationId,
                            determination.StartedUtc,
                            determination.CompletedUtc,
                            metadata.Identity.MechanicalStateId,
                            metadata.CorrectionSequenceNumber,
                            input.FreshSolvesUncached,
                            determination.Geometry.IsFinite
                                && !determination.Geometry.IsDegenerate,
                            determination.Geometry.IsFinite
                                && !determination.Geometry.IsDegenerate
                                && determination.Geometry.MinimumPairwiseSeparationDegrees
                                    >= TppaVerificationSettlePolicy
                                        .MinimumQualifiedTargetDistanceDegrees,
                            input.ClosureQualified,
                            determination.Solves
                                .Select(solve => solve.ContentSha256)
                                .ToArray(),
                            determinations[index].SourcePoints
                                .Select(ToQualificationSolve)
                                .ToArray(),
                            ToQualificationVector(determination.MountAxisVector)))
                    .ToArray();
                var evidence = new TppaQualificationRunEvidence(
                    TppaAbsoluteEvidenceBinder.CurrentEvidenceSchemaVersion,
                    EvidenceDigest: string.Empty,
                    metadata.RunId,
                    metadata.SessionId,
                    metadata.ProducerId,
                    TppaAbsoluteEvidenceBinder.TppaProducerKind,
                    metadata.PipelineDigest,
                    metadata.Identity.HardwareConfigurationId,
                    metadata.Identity.MechanicalStateId,
                    metadata.Identity.ClockDomainId,
                    metadata.Identity.ClockUncertaintyMilliseconds,
                    metadata.Identity.TppaInstrumentId,
                    metadata.Identity.SolverIdentity,
                    metadata.Site.SourceId,
                    metadata.Site.LatitudeDegrees,
                    metadata.Site.LongitudeDegrees,
                    metadata.Site.ElevationMeters,
                    metadata.Identity.CoordinateFrame,
                    metadata.Identity.MountAxisVectorFrame,
                    input.PoleTarget,
                    input.AtmosphereSource,
                    metadata.Atmosphere.ObservationUtc,
                    metadata.Atmosphere.PressureHPa,
                    metadata.Atmosphere.TemperatureCelsius,
                    metadata.Atmosphere.RelativeHumidityPercent,
                    metadata.RefractionAdjustmentEnabled,
                    input.AtmosphereQualified,
                    input.AtmosphereFresh,
                    input.StationPressureQualified,
                    input.AtmosphereTemperatureQualified,
                    input.AtmosphereHumidityQualified,
                    input.SiteTimeProvenanceQualified,
                    input.CoordinateFrameQualified,
                    evidenceDeterminations,
                    ToQualificationVector(targetPole));
                var json = SerializeEvidence(evidence);
                Directory.CreateDirectory(outputDirectory);
                var outputPath = Path.Combine(
                    outputDirectory,
                    SanitizeFileName(metadata.RunId) + "-tppa-evidence.json");
                WriteCreateNew(outputPath, Encoding.UTF8.GetBytes(json));
                return new(
                    true,
                    json,
                    outputPath,
                    derived.DerivationIssues,
                    Array.Empty<string>());
            } catch (Exception exception) when (
                    exception is ArgumentException
                    || exception is ArithmeticException
                    || exception is IOException
                    || exception is InvalidOperationException
                    || exception is UnauthorizedAccessException) {
                return new(
                    false,
                    null,
                    null,
                    Array.Empty<string>(),
                    new[] {
                        $"TPPA run evidence production failed closed ({exception.GetType().Name}): {exception.Message}"
                    });
            }
        }

        internal static string SerializeEvidence(object evidence) {
            if (evidence == null) { throw new ArgumentNullException(nameof(evidence)); }
            var token = JObject.FromObject(evidence, EvidenceSerializer);
            token["evidenceDigest"] = new string('0', 64);
            token["evidenceDigest"] =
                TppaAbsoluteEvidenceBinder.ComputeCanonicalEvidenceDigest(
                    token.ToString(Formatting.None));
            return token.ToString(Formatting.None);
        }

        internal static string Sha256Utf8(string value) =>
            Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(value ?? string.Empty)))
                .ToLowerInvariant();

        internal static string Sha256File(string path) =>
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
                .ToLowerInvariant();

        private static TppaRuntimeDeterminationEvidence ToRuntimeEvidence(
                TppaQualificationDeterminationInput determination) {
            var solves = determination.SourcePoints.Select(point =>
                new TppaRuntimeSolveEvidence(
                    point.ObservationUtc,
                    Sha256Utf8(point.ToJson()),
                    point.SolvedRightAscensionDegrees,
                    point.SolvedDeclinationDegrees,
                    point.SideOfPier)).ToArray();
            return new(
                determination.StartedUtc,
                determination.CompletedUtc,
                solves,
                determination.MountAxisVector,
                determination.Geometry);
        }

        private static TppaQualificationVector ToQualificationVector(Vector3 value) =>
            value == null ? null : new(value.X, value.Y, value.Z);

        private static TppaQualificationSolveEvidence ToQualificationSolve(
                TppaVerificationPointReceipt point) =>
            new(
                point.ObservationUtc,
                Sha256Utf8(point.ToJson()),
                point.SolvedRightAscensionDegrees,
                point.SolvedDeclinationDegrees,
                point.SideOfPier.ToString(),
                new TppaQualificationVector(
                    point.VectorX,
                    point.VectorY,
                    point.VectorZ));

        private static List<string> Validate(
                TppaQualificationRunProductionMetadata metadata,
                IReadOnlyList<TppaQualificationDeterminationInput> determinations,
                string outputDirectory) {
            var issues = new List<string>();
            if (metadata == null) {
                issues.Add("production metadata is missing");
                return issues;
            }
            if (string.IsNullOrWhiteSpace(metadata.RunId)
                    || string.IsNullOrWhiteSpace(metadata.SessionId)
                    || string.IsNullOrWhiteSpace(metadata.ProducerId)) {
                issues.Add("run, session, or producer identity is missing");
            }
            if (!IsSha256(metadata.PipelineDigest)
                    || !IsSha256(metadata.Identity?.MechanicalStateId)) {
                issues.Add("pipeline or mechanical-state digest is invalid");
            }
            if (metadata.Identity == null || metadata.Site == null
                    || metadata.Atmosphere == null) {
                issues.Add("runtime identity, site, or atmosphere evidence is missing");
            }
            if (metadata.CorrectionSequenceNumber < 0) {
                issues.Add("correction sequence number cannot be negative");
            }
            if (string.IsNullOrWhiteSpace(outputDirectory)) {
                issues.Add("evidence output directory is missing");
            }
            var values = determinations?.Where(value => value != null).ToArray()
                ?? Array.Empty<TppaQualificationDeterminationInput>();
            if (values.Length != 3 || values.Length != (determinations?.Count ?? 0)) {
                issues.Add("exactly three non-null determinations are required");
            }
            foreach (var determination in values) {
                if (string.IsNullOrWhiteSpace(determination.DeterminationId)
                        || determination.StartedUtc.Kind != DateTimeKind.Utc
                        || determination.CompletedUtc.Kind != DateTimeKind.Utc
                        || determination.CompletedUtc <= determination.StartedUtc
                        || determination.SourcePoints?.Count != 3
                        || determination.SourcePoints.Any(point => point == null)) {
                    issues.Add("a determination is incomplete or has invalid UTC bounds");
                }
            }
            return issues;
        }

        private static bool IsSha256(string value) =>
            value?.Length == 64 && value.All(character =>
                (character >= '0' && character <= '9')
                || (character >= 'a' && character <= 'f')
                || (character >= 'A' && character <= 'F'));

        private static string SanitizeFileName(string value) {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(value.Select(character =>
                invalid.Contains(character) ? '_' : character).ToArray());
        }

        private static void WriteCreateNew(string path, byte[] content) {
            using var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read);
            stream.Write(content, 0, content.Length);
            stream.Flush(true);
        }
    }
}
