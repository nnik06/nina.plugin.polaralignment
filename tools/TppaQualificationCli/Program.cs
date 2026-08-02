using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NINA.Plugins.PolarAlignment;

internal static class Program {
    private static int Main(string[] args) {
        try {
            if (args.Length == 0) {
                return Usage("A bind or verify command is required.");
            }

            var command = args[0].ToLowerInvariant();
            var options = ParseOptions(args.Skip(1).ToArray());
            return command switch {
                "bind" => Bind(options),
                "verify" => Verify(options),
                "produce-witness" => ProduceWitness(options),
                "create-witness-request" => CreateWitnessRequest(options),
                "validate-witness-request" => ValidateWitnessRequest(options),
                "create-witness-outcome" => CreateWitnessOutcome(options),
                "validate-witness-outcome" => ValidateWitnessOutcome(options),
                "analyze-actual-exposure" => AnalyzeActualExposure(options),
                "verify-actual-exposure" => VerifyActualExposure(options),
                _ => Usage($"Unknown command '{args[0]}'.")
            };
        } catch (Exception ex) {
            WriteStatus(new {
                status = "internal-error",
                qualified = false,
                issues = new[] { ex.Message },
                grantsMotionAuthority = false
            });
            return 3;
        }
    }

    private static int Bind(IReadOnlyDictionary<string, string> options) {
        var tppaPath = RequireOption(options, "tppa");
        var witnessPath = RequireOption(options, "witness");
        var receiptPath = RequireOption(options, "receipt-out");
        var policySha256 = EmbeddedPolicySourceSha256();
        var binding = TppaAbsoluteEvidenceBinder.Bind(
            File.ReadAllBytes(tppaPath),
            File.ReadAllBytes(witnessPath),
            policySha256,
            DateTime.UtcNow);

        if (!binding.EvidenceValid || binding.ReceiptJson == null) {
            WriteStatus(new {
                status = "evidence-invalid",
                qualified = false,
                tppaEvidenceSha256 = binding.TppaEvidenceSha256,
                witnessEvidenceSha256 = binding.WitnessEvidenceSha256,
                issues = binding.Issues,
                grantsMotionAuthority = false
            });
            return 2;
        }

        using (var stream = new FileStream(
                   receiptPath,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.Read))
        using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false))) {
            writer.Write(binding.ReceiptJson);
        }

        WriteStatus(new {
            status = binding.IsQualified == true ? "qualified" : "not-qualified",
            qualified = binding.IsQualified == true,
            receiptPath = Path.GetFullPath(receiptPath),
            receiptSha256 = Sha256(File.ReadAllBytes(receiptPath)),
            tppaEvidenceSha256 = binding.TppaEvidenceSha256,
            witnessEvidenceSha256 = binding.WitnessEvidenceSha256,
            sourcePolarErrorVectorDigest = binding.SourcePolarErrorVectorDigest,
            issues = binding.Issues,
            grantsMotionAuthority = false
        });
        return binding.IsQualified == true ? 0 : 1;
    }

    private static int Verify(IReadOnlyDictionary<string, string> options) {
        var tppaPath = RequireOption(options, "tppa");
        var witnessPath = RequireOption(options, "witness");
        var receiptPath = RequireOption(options, "receipt");
        var policySha256 = EmbeddedPolicySourceSha256();
        var binding = TppaAbsoluteEvidenceBinder.Bind(
            File.ReadAllBytes(tppaPath),
            File.ReadAllBytes(witnessPath),
            policySha256,
            DateTime.UtcNow);
        var issues = new List<string>(binding.Issues);
        if (!binding.EvidenceValid || binding.SourcePolarErrorVectorDigest == null) {
            issues.Add("current evidence pair is invalid");
        }

        var receiptJson = File.ReadAllText(receiptPath);
        var verification = TppaFastQualificationReceiptVerifier.Verify(
            receiptJson,
            policySha256,
            binding.SourcePolarErrorVectorDigest);
        issues.AddRange(verification.Issues);
        if (verification.QualificationInput != null) {
            if (verification.QualificationInput.TppaInputPathDigest
                    != binding.TppaEvidenceSha256) {
                issues.Add("receipt is not bound to the supplied TPPA evidence bytes");
            }
            if (verification.QualificationInput.IndependentWitnessInputPathDigest
                    != binding.WitnessEvidenceSha256) {
                issues.Add("receipt is not bound to the supplied witness evidence bytes");
            }
        }

        var valid = binding.EvidenceValid && verification.IsValid && issues.Count == 0;
        WriteStatus(new {
            status = valid ? "verified" : "verify-failed",
            valid,
            qualified = valid
                ? JObject.Parse(receiptJson)["isFastTruePoleQualified"]?.Value<bool>()
                : false,
            issues,
            grantsMotionAuthority = false
        });
        return valid ? 0 : 4;
    }

    private static int ProduceWitness(
            IReadOnlyDictionary<string, string> options) {
        var metadataPath = RequireOption(options, "metadata");
        var pointsPath = RequireOption(options, "points");
        var outputDirectory = RequireOption(options, "output-dir");
        TppaRaRotationWitnessProductionMetadata metadata;
        IReadOnlyList<TppaRaRotationWitnessPointReceipt> points;
        try {
            metadata = DeserializeStrict<TppaRaRotationWitnessProductionMetadata>(
                File.ReadAllText(metadataPath));
            points = DeserializeStrict<TppaRaRotationWitnessPointReceipt[]>(
                File.ReadAllText(pointsPath));
        } catch (Exception exception) when (
                exception is JsonException
                || exception is IOException
                || exception is UnauthorizedAccessException) {
            WriteStatus(new {
                status = "evidence-invalid",
                qualified = false,
                issues = new[] {
                    $"witness production input cannot be parsed strictly: {exception.Message}"
                },
                grantsMotionAuthority = false
            });
            return 2;
        }

        var result = TppaRaRotationWitnessEvidenceProducer.Produce(
            metadata,
            points,
            outputDirectory);
        if (!result.Produced) {
            WriteStatus(new {
                status = "evidence-invalid",
                qualified = false,
                issues = result.ProductionIssues.Concat(result.QualificationIssues),
                grantsMotionAuthority = false
            });
            return 2;
        }

        var qualified = result.QualificationIssues.Count == 0;
        WriteStatus(new {
            status = qualified ? "witness-produced" : "witness-produced-not-qualified",
            qualified,
            witnessPath = Path.GetFullPath(result.OutputPath),
            witnessSha256 = Sha256(File.ReadAllBytes(result.OutputPath)),
            issues = result.QualificationIssues,
            grantsMotionAuthority = false
        });
        return qualified ? 0 : 1;
    }

    private static int ValidateWitnessRequest(
            IReadOnlyDictionary<string, string> options) {
        var requestPath = RequireOption(options, "request");
        var nowText = RequireOption(options, "now-utc");
        var request = DeserializeStrict<TppaRaRotationWitnessCaptureRequest>(
            File.ReadAllText(requestPath));
        var nowUtc = DateTime.Parse(
            nowText,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);
        var result = TppaRaRotationWitnessHandshake.ValidateRequest(
            request, nowUtc);
        WriteStatus(new {
            status = result.IsValid ? "witness-request-valid" : "witness-request-invalid",
            valid = result.IsValid,
            requestDigest = request.RequestDigest,
            issues = result.Issues,
            grantsMotionAuthority = false
        });
        return result.IsValid ? 0 : 2;
    }

    private static int CreateWitnessRequest(
            IReadOnlyDictionary<string, string> options) {
        var specPath = RequireOption(options, "spec");
        var requestPath = RequireOption(options, "request-out");
        if (!requestPath.EndsWith(
                ".witness-request.ready.json",
                StringComparison.OrdinalIgnoreCase)) {
            throw new ArgumentException(
                "--request-out must end with .witness-request.ready.json.");
        }
        var spec = DeserializeStrict<TppaRaRotationWitnessCaptureRequestSpec>(
            File.ReadAllText(specPath));
        var request = TppaRaRotationWitnessHandshake.CreateRequest(spec);
        var validation = TppaRaRotationWitnessHandshake.ValidateRequest(
            request,
            request.RequestedUtc);
        if (!validation.IsValid) {
            WriteStatus(new {
                status = "witness-request-invalid",
                valid = false,
                issues = validation.Issues,
                grantsMotionAuthority = false
            });
            return 2;
        }

        var temporaryPath = requestPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.Read))
            using (var writer = new StreamWriter(
                       stream,
                       new System.Text.UTF8Encoding(false))) {
                writer.Write(TppaRaRotationWitnessHandshake.SerializeRequest(request));
            }
            File.Move(temporaryPath, requestPath);
        } finally {
            if (File.Exists(temporaryPath)) {
                File.Delete(temporaryPath);
            }
        }
        WriteStatus(new {
            status = "witness-request-created",
            valid = true,
            requestDigest = request.RequestDigest,
            requestPath = Path.GetFullPath(requestPath),
            requestSha256 = Sha256(File.ReadAllBytes(requestPath)),
            issues = Array.Empty<string>(),
            grantsMotionAuthority = false
        });
        return 0;
    }

    private static int ValidateWitnessOutcome(
            IReadOnlyDictionary<string, string> options) {
        var requestPath = RequireOption(options, "request");
        var outcomePath = RequireOption(options, "outcome");
        var request = DeserializeStrict<TppaRaRotationWitnessCaptureRequest>(
            File.ReadAllText(requestPath));
        var outcome = DeserializeStrict<TppaRaRotationWitnessCaptureOutcome>(
            File.ReadAllText(outcomePath));
        var result = TppaRaRotationWitnessHandshake.ValidateOutcome(
            request, outcome);
        WriteStatus(new {
            status = result.IsValid ? "witness-outcome-valid" : "witness-outcome-invalid",
            valid = result.IsValid,
            requestDigest = request.RequestDigest,
            pointReceiptSha256 = outcome.PointReceiptSha256,
            issues = result.Issues,
            grantsMotionAuthority = false
        });
        return result.IsValid ? 0 : 2;
    }

    private static int CreateWitnessOutcome(
            IReadOnlyDictionary<string, string> options) {
        var requestPath = RequireOption(options, "request");
        var pointPath = RequireOption(options, "point");
        var outcomePath = RequireOption(options, "outcome-out");
        var startedUtc = ParseUtc(RequireOption(options, "started-utc"));
        var completedUtc = ParseUtc(RequireOption(options, "completed-utc"));
        var observerDigest = RequireOption(options, "observer-pipeline-digest")
            .ToLowerInvariant();
        var request = DeserializeStrict<TppaRaRotationWitnessCaptureRequest>(
            File.ReadAllText(requestPath));
        var point = DeserializeStrict<TppaRaRotationWitnessPointReceipt>(
            File.ReadAllText(pointPath));
        var outcome = new TppaRaRotationWitnessCaptureOutcome(
            TppaRaRotationWitnessCaptureOutcome.CurrentSchemaVersion,
            request.RequestDigest,
            request.RunId,
            request.PositionId,
            request.SequenceIndex,
            request.Nonce,
            TppaRaRotationWitnessCaptureOutcome.CapturedStatus,
            AttemptNumber: 1,
            startedUtc,
            completedUtc,
            observerDigest,
            TppaRaRotationWitnessHandshake.Sha256Utf8(point.ToJson()),
            point,
            Array.Empty<string>());
        var result = TppaRaRotationWitnessHandshake.ValidateOutcome(
            request, outcome);
        if (!result.IsValid) {
            WriteStatus(new {
                status = "witness-outcome-invalid",
                valid = false,
                requestDigest = request.RequestDigest,
                issues = result.Issues,
                grantsMotionAuthority = false
            });
            return 2;
        }

        var json = TppaRaRotationWitnessHandshake.SerializeOutcome(outcome);
        using (var stream = new FileStream(
                   outcomePath,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.Read))
        using (var writer = new StreamWriter(
                   stream,
                   new System.Text.UTF8Encoding(false))) {
            writer.Write(json);
        }
        WriteStatus(new {
            status = "witness-outcome-created",
            valid = true,
            requestDigest = request.RequestDigest,
            outcomePath = Path.GetFullPath(outcomePath),
            outcomeSha256 = Sha256(File.ReadAllBytes(outcomePath)),
            pointReceiptSha256 = outcome.PointReceiptSha256,
            issues = Array.Empty<string>(),
            grantsMotionAuthority = false
        });
        return 0;
    }

    private static int AnalyzeActualExposure(
            IReadOnlyDictionary<string, string> options) {
        var manifestPath = RequireOption(options, "manifest");
        var receiptPath = RequireOption(options, "receipt-out");
        TppaActualExposureEvidenceManifest manifest;
        try {
            manifest = DeserializeStrict<TppaActualExposureEvidenceManifest>(
                File.ReadAllText(manifestPath));
        } catch (Exception exception) when (exception is JsonException
                or IOException or UnauthorizedAccessException) {
            WriteStatus(new {
                status = "evidence-invalid",
                qualified = false,
                issues = new[] { $"actual-exposure manifest cannot be parsed strictly: {exception.Message}" },
                polarAlignmentInferenceQualified = false,
                grantsAbsoluteAccuracyClaim = false,
                grantsMotionAuthority = false,
                grantsUpasAuthority = false
            });
            return 2;
        }
        var result = TppaActualExposureEvidenceProducer.Produce(manifest);
        if (!result.Produced || result.ReceiptJson == null) {
            WriteStatus(new {
                status = "evidence-invalid",
                qualified = false,
                issues = result.Issues,
                polarAlignmentInferenceQualified = false,
                grantsAbsoluteAccuracyClaim = false,
                grantsMotionAuthority = false,
                grantsUpasAuthority = false
            });
            return 2;
        }
        using (var stream = new FileStream(receiptPath, FileMode.CreateNew,
                   FileAccess.Write, FileShare.Read))
        using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false))) {
            writer.Write(result.ReceiptJson);
        }
        var qualified = result.Receipt.SeeingInclusiveDelivered900SecondStarShapeQualified;
        WriteStatus(new {
            status = qualified ? "delivered-900s-qualified" : "delivered-900s-not-qualified",
            qualified,
            receiptPath = Path.GetFullPath(receiptPath),
            receiptSha256 = Sha256(File.ReadAllBytes(receiptPath)),
            issues = result.Issues,
            polarAlignmentInferenceQualified = false,
            grantsAbsoluteAccuracyClaim = false,
            grantsMotionAuthority = false,
            grantsUpasAuthority = false
        });
        return qualified ? 0 : 1;
    }

    private static int VerifyActualExposure(
            IReadOnlyDictionary<string, string> options) {
        var manifestPath = RequireOption(options, "manifest");
        var receiptPath = RequireOption(options, "receipt");
        TppaActualExposureEvidenceManifest manifest;
        string receiptJson;
        try {
            manifest = DeserializeStrict<TppaActualExposureEvidenceManifest>(
                File.ReadAllText(manifestPath));
            receiptJson = File.ReadAllText(receiptPath);
        } catch (Exception exception) when (exception is JsonException
                or IOException or UnauthorizedAccessException) {
            WriteStatus(new {
                status = "evidence-invalid",
                verified = false,
                qualified = false,
                issues = new[] { $"actual-exposure manifest or receipt cannot be read strictly: {exception.Message}" },
                polarAlignmentInferenceQualified = false,
                grantsAbsoluteAccuracyClaim = false,
                grantsMotionAuthority = false,
                grantsUpasAuthority = false
            });
            return 2;
        }
        var verification = TppaActualExposureEvidenceReceiptVerifier.Verify(
            manifest, receiptJson);
        WriteStatus(new {
            status = verification.Verified ? "actual-exposure-receipt-verified" : "evidence-invalid",
            verified = verification.Verified,
            qualified = verification.SeeingInclusiveDelivered900SecondStarShapeQualified,
            receiptPath = Path.GetFullPath(receiptPath),
            receiptSha256 = Sha256(File.ReadAllBytes(receiptPath)),
            issues = verification.Issues,
            polarAlignmentInferenceQualified = false,
            grantsAbsoluteAccuracyClaim = false,
            grantsMotionAuthority = false,
            grantsUpasAuthority = false
        });
        if (!verification.Verified) {
            return 2;
        }
        return verification.SeeingInclusiveDelivered900SecondStarShapeQualified ? 0 : 1;
    }

    private static DateTime ParseUtc(string value) {
        var parsed = DateTime.Parse(
            value,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);
        if (parsed.Kind != DateTimeKind.Utc) {
            throw new ArgumentException("Timestamp must declare UTC with a Z suffix.");
        }
        return parsed;
    }

    private static T DeserializeStrict<T>(string json) {
        var settings = new JsonSerializerSettings {
            MissingMemberHandling = MissingMemberHandling.Error,
            DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind
        };
        return JsonConvert.DeserializeObject<T>(json, settings)
            ?? throw new JsonSerializationException(
                $"JSON did not contain a {typeof(T).Name} value.");
    }

    private static Dictionary<string, string> ParseOptions(string[] args) {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2) {
            if (index + 1 >= args.Length || !args[index].StartsWith("--")) {
                throw new ArgumentException("Options must use --name value pairs.");
            }
            if (!options.TryAdd(args[index][2..], args[index + 1])) {
                throw new ArgumentException($"Duplicate option '{args[index]}'.");
            }
        }
        return options;
    }

    private static string RequireOption(
            IReadOnlyDictionary<string, string> options,
            string name) =>
        options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"--{name} is required.");

    private static string EmbeddedPolicySourceSha256() {
        using var stream = typeof(TppaFastQualification).Assembly
            .GetManifestResourceStream("TppaFastQualificationPolicySource.cs")
            ?? throw new InvalidOperationException("Embedded policy source is missing.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return Sha256(memory.ToArray());
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void WriteStatus(object value) =>
        Console.Out.WriteLine(JsonConvert.SerializeObject(value, Formatting.None));

    private static int Usage(string issue) {
        WriteStatus(new {
            status = "usage-error",
            qualified = false,
            issues = new[] { issue },
            usage = "tppa-qualify bind --tppa run.json --witness witness.json --receipt-out receipt.json | verify --tppa run.json --witness witness.json --receipt receipt.json | produce-witness --metadata metadata.json --points points.json --output-dir directory | create-witness-request --spec spec.json --request-out point.witness-request.ready.json | validate-witness-request --request request.json --now-utc timestamp | create-witness-outcome --request request.json --point point.json --outcome-out outcome.json --started-utc timestamp --completed-utc timestamp --observer-pipeline-digest sha256 | validate-witness-outcome --request request.json --outcome outcome.json | analyze-actual-exposure --manifest manifest.json --receipt-out receipt.json | verify-actual-exposure --manifest manifest.json --receipt receipt.json",
            grantsMotionAuthority = false
        });
        return 3;
    }
}
