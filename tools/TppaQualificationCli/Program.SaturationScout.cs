using Newtonsoft.Json;

namespace NINA.Plugins.PolarAlignment;

internal static partial class Program {
    private static int AnalyzeSaturationScout(
            IReadOnlyDictionary<string, string> options) {
        var shortPath = RequireOption(options, "short-fits");
        var longPath = RequireOption(options, "long-fits");
        var policyPath = RequireOption(options, "policy");
        var receiptPath = RequireOption(options, "receipt-out");
        var nowText = RequireOption(options, "now-utc");
        if (!DateTime.TryParse(nowText, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal
                    | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var nowUtc)) {
            throw new InvalidDataException("--now-utc is not a valid UTC timestamp");
        }
        var settings = new JsonSerializerSettings {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            DateParseHandling = DateParseHandling.DateTime
        };
        var policy = JsonConvert.DeserializeObject<TppaActualExposureScoutPolicy>(
            File.ReadAllText(policyPath), settings)
            ?? throw new InvalidDataException("saturation scout policy is empty");
        var shortHash = Sha256(File.ReadAllBytes(shortPath));
        var longHash = Sha256(File.ReadAllBytes(longPath));
        var receipt = TppaActualExposureSaturationScoutAnalyzer.Analyze(
            shortPath, longPath, shortHash, longHash, policy, nowUtc);
        using (var stream = new FileStream(receiptPath, FileMode.CreateNew,
                   FileAccess.Write, FileShare.Read))
        using (var writer = new StreamWriter(stream,
                   new System.Text.UTF8Encoding(false))) {
            writer.Write(JsonConvert.SerializeObject(receipt, Formatting.Indented));
            writer.Write("\r\n");
        }
        var exitCode = receipt.Verdict switch {
            TppaSaturationScoutVerdicts.Pass => 0,
            TppaSaturationScoutVerdicts.Fail => 1,
            _ => 2
        };
        WriteStatus(new {
            status = receipt.Verdict.ToLowerInvariant(),
            qualified = receipt.Verdict == TppaSaturationScoutVerdicts.Pass,
            receiptPath = Path.GetFullPath(receiptPath),
            receiptSha256 = Sha256(File.ReadAllBytes(receiptPath)),
            issues = receipt.Issues,
            grantsSequenceStartAuthority = false,
            grantsMotionAuthority = false,
            predictsGuidingOrStarShapeSuccess = false
        });
        return exitCode;
    }
}
