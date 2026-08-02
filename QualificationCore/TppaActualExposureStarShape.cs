using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace NINA.Plugins.PolarAlignment;

internal static class TppaActualExposureRoles {
    public const string PreControl = "PreControl";
    public const string LongExposure = "LongExposure";
    public const string PostControl = "PostControl";
}

internal sealed class TppaActualExposureStarShapePolicy {
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string OpticalTrainId { get; set; } = string.Empty;
    public double PixelScaleArcsecondsPerPixel { get; set; }
    public double GainElectronsPerAdu { get; set; }
    public double ApertureRadiusArcseconds { get; set; } = 8.0;
    public double BackgroundInnerRadiusArcseconds { get; set; } = 10.0;
    public double BackgroundOuterRadiusArcseconds { get; set; } = 14.0;
    public double BlendExclusionRadiusArcseconds { get; set; } = 24.0;
    public double LinearityCeilingAdu { get; set; } = 50000.0;
    public double MinimumApertureSnr { get; set; } = 20.0;
    public double MaximumBackgroundFluxNoiseFraction { get; set; } = 0.10;
    public int MinimumPreControlFrames { get; set; } = 5;
    public int MinimumPostControlFrames { get; set; } = 5;
    public int MinimumMatchedStars { get; set; } = 25;
    public int MinimumStarsPerZone { get; set; } = 3;
    public double MaximumCatalogMatchArcseconds { get; set; } = 1.5;
    public double MinimumControlExposureSeconds { get; set; } = 10.0;
    public double MaximumControlExposureSeconds { get; set; } = 30.0;
    public double ExpectedLongExposureSeconds { get; set; } = 900.0;
    public double LongExposureToleranceSeconds { get; set; } = 5.0;
    public double MaximumControlToLongGapSeconds { get; set; } = 45.0;
    public double MaximumBracketSpanMinutes { get; set; } = 20.0;
    public double MaximumMedianDifferentialEllipticity { get; set; } = 0.15;
    public double MaximumMedianMajorSigmaGrowthFraction { get; set; } = 0.20;
    public double MaximumOuterMedianMajorSigmaGrowthFraction { get; set; } = 0.25;
    public double MaximumPrePostMajorSigmaDriftFraction { get; set; } = 0.10;
    public double MaximumLinearityRejectionFraction { get; set; } = 0.50;
    public double MaximumLongFrameAttritionFraction { get; set; } = 0.15;
    public double MaximumPixelScaleRelativeError { get; set; } = 0.02;
    public double MaximumPixelScaleAnisotropyFraction { get; set; } = 0.02;
    public double MinimumQualifiedFwhmPixels { get; set; } = 2.0;
    public double MaximumControlFrameMajorSigmaScatterFraction { get; set; } = 0.10;
    public bool AbsoluteEccentricityQualified { get; set; }
    public double MaximumLongMedianEccentricity { get; set; } = 0.55;
    public string[] RequiredEqualHeaderKeywords { get; set; } = new[] {
        "INSTRUME", "FILTER", "XBINNING", "YBINNING", "GAIN", "OFFSET"
    };
}

internal sealed record TppaActualExposureFrameSource(
    string Role,
    string FitsPath,
    string AstapCatalogPath);

internal sealed record TppaAstapStar(
    double X,
    double Y,
    double HfdPixels,
    double AstapSnr,
    double Flux,
    double RightAscensionDegrees,
    double DeclinationDegrees);

internal sealed record TppaAdaptiveStarMoment(
    double X,
    double Y,
    double MxxPixelsSquared,
    double MyyPixelsSquared,
    double MxyPixelsSquared,
    double MajorSigmaPixels,
    double MinorSigmaPixels,
    double Ellipticity1,
    double Ellipticity2,
    double Eccentricity,
    double MajorAxisDegrees,
    double ApertureSnr,
    double BackgroundNoiseFraction,
    double PeakAdu,
    double ApertureFluxAdu,
    int Iterations,
    bool Converged);

internal sealed record TppaActualExposureZoneSummary(
    string Zone,
    int StarCount,
    double MedianMajorSigmaGrowthFraction,
    double MedianEllipticity1Delta,
    double MedianEllipticity2Delta,
    double DifferentialEllipticityMagnitude,
    double MedianLongEccentricity);

internal sealed record TppaActualExposureStarShapeResult(
    bool EvidenceValid,
    bool ActualLongExposureArtifactPresent,
    bool SeeingInclusiveDelivered900SecondStarShapeQualified,
    string OpticalTrainId,
    bool AbsoluteEccentricityQualified,
    int PreControlFrameCount,
    int PostControlFrameCount,
    int ControlQualifiedCandidateStars,
    int LongFrameRejectedStars,
    double LongFrameAttritionFraction,
    int MatchedUsableStars,
    int LinearityRejectedStars,
    double LinearityRejectionFraction,
    double MeasuredPixelScaleArcsecondsPerPixel,
    double MedianControlFwhmPixels,
    double ControlFrameMajorSigmaScatterFraction,
    double MedianLongMajorSigmaArcseconds,
    double MedianMajorSigmaGrowthFraction,
    double MedianDifferentialEllipticity,
    double PrePostMajorSigmaDriftFraction,
    double MedianLongEccentricity,
    IReadOnlyList<TppaActualExposureZoneSummary> Zones,
    IReadOnlyList<string> Issues) {
    public int CandidateLongStars => ControlQualifiedCandidateStars;
    public bool PolarAlignmentInferenceQualified => false;
    public bool GrantsAbsoluteAccuracyClaim => false;
    public bool GrantsMountMotionAuthority => false;
    public bool GrantsUpasAuthority => false;
}

internal static class TppaAstapCatalogReader {
    private static readonly string[] ExpectedColumns = {
        "x", "y", "hfd", "snr", "flux", "ra[0..360]", "dec[0..360]"
    };

    public static IReadOnlyList<TppaAstapStar> Read(string path) {
        var lines = File.ReadAllLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        if (lines.Length < 2) {
            throw new InvalidDataException("ASTAP catalog has no star rows.");
        }
        var columns = lines[0].Split(',').Select(value => value.Trim()).ToArray();
        if (!columns.SequenceEqual(ExpectedColumns, StringComparer.OrdinalIgnoreCase)) {
            throw new InvalidDataException(
                "ASTAP catalog header is not the pinned extract2 schema.");
        }
        var stars = new List<TppaAstapStar>(lines.Length - 1);
        foreach (var line in lines.Skip(1)) {
            var values = line.Split(',');
            if (values.Length != ExpectedColumns.Length) {
                throw new InvalidDataException("ASTAP catalog row width is invalid.");
            }
            var parsed = values.Select(value => double.Parse(
                value, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
            if (parsed.Any(value => !double.IsFinite(value))
                    || parsed[0] < 1 || parsed[1] < 1
                    || parsed[2] <= 0 || parsed[3] <= 0 || parsed[4] <= 0
                    || parsed[5] < 0 || parsed[5] >= 360
                    || parsed[6] < -90 || parsed[6] > 90) {
                throw new InvalidDataException("ASTAP catalog row is non-finite or out of range.");
            }
            stars.Add(new(parsed[0], parsed[1], parsed[2], parsed[3], parsed[4],
                parsed[5], parsed[6]));
        }
        return stars;
    }
}

internal sealed class TppaFitsImage : IDisposable {
    private readonly FileStream stream;
    private readonly Dictionary<string, string> header;
    private readonly long dataOffset;
    private readonly int bytesPerPixel;
    private readonly double bscale;
    private readonly double bzero;
    private readonly long? blank;

    private TppaFitsImage(string path) {
        stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        header = ReadHeader(stream, out dataOffset);
        Bitpix = GetRequiredInt("BITPIX");
        var naxis = GetRequiredInt("NAXIS");
        if (naxis != 2) {
            throw new InvalidDataException("Only two-dimensional FITS primary arrays are supported.");
        }
        Width = GetRequiredInt("NAXIS1");
        Height = GetRequiredInt("NAXIS2");
        if (Width <= 0 || Height <= 0) {
            throw new InvalidDataException("FITS dimensions are invalid.");
        }
        bytesPerPixel = Math.Abs(Bitpix) / 8;
        if (Bitpix is not (8 or 16 or 32 or -32 or -64)) {
            throw new InvalidDataException($"Unsupported FITS BITPIX={Bitpix}.");
        }
        bscale = GetOptionalDouble("BSCALE", 1.0);
        bzero = GetOptionalDouble("BZERO", 0.0);
        blank = Bitpix > 0 ? GetOptionalLong("BLANK") : null;
        var requiredLength = checked(dataOffset + (long)Width * Height * bytesPerPixel);
        if (stream.Length < requiredLength) {
            throw new InvalidDataException("FITS primary array is truncated.");
        }
        ExposureSeconds = GetOptionalDouble("EXPTIME",
            GetOptionalDouble("EXPOSURE", double.NaN));
        if (!double.IsFinite(ExposureSeconds) || ExposureSeconds <= 0) {
            throw new InvalidDataException("FITS exposure time is missing or invalid.");
        }
        var dateText = GetRequiredString("DATE-OBS");
        if (!DateTimeOffset.TryParse(dateText, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var date)) {
            throw new InvalidDataException("FITS DATE-OBS cannot be parsed as UTC.");
        }
        DateObsUtc = date.ToUniversalTime();
        MidpointUtc = DateObsUtc.AddSeconds(ExposureSeconds / 2.0);
    }

    public int Width { get; }
    public int Height { get; }
    public int Bitpix { get; }
    public double ExposureSeconds { get; }
    public DateTimeOffset DateObsUtc { get; }
    public DateTimeOffset MidpointUtc { get; }
    public static TppaFitsImage Open(string path) => new(path);

    public string GetRequiredString(string key) {
        if (!header.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)) {
            throw new InvalidDataException($"FITS keyword {key} is missing.");
        }
        return value;
    }

    public bool TryGetString(string key, out string value) =>
        header.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value);

    public bool TryGetDouble(string key, out double value) {
        value = double.NaN;
        return header.TryGetValue(key, out var text)
            && double.TryParse(text, NumberStyles.Float,
                CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value);
    }

    public bool TryGetPixelScaleArcsecondsPerPixel(
            out double scale, out double anisotropyFraction) {
        scale = anisotropyFraction = double.NaN;
        double xScale;
        double yScale;
        if (TryGetDouble("CD1_1", out var cd11)
                && TryGetDouble("CD1_2", out var cd12)
                && TryGetDouble("CD2_1", out var cd21)
                && TryGetDouble("CD2_2", out var cd22)) {
            xScale = 3600.0 * Math.Sqrt(cd11 * cd11 + cd21 * cd21);
            yScale = 3600.0 * Math.Sqrt(cd12 * cd12 + cd22 * cd22);
        } else if (TryGetDouble("CDELT1", out var cdelt1)
                && TryGetDouble("CDELT2", out var cdelt2)) {
            xScale = 3600.0 * Math.Abs(cdelt1);
            yScale = 3600.0 * Math.Abs(cdelt2);
        } else {
            return false;
        }
        scale = 0.5 * (xScale + yScale);
        anisotropyFraction = Math.Abs(xScale - yScale) / scale;
        return double.IsFinite(scale) && scale > 0
            && double.IsFinite(anisotropyFraction);
    }

    public double ReadPixel(int x, int y) {
        if (x < 0 || x >= Width || y < 0 || y >= Height) {
            throw new ArgumentOutOfRangeException();
        }
        var offset = checked(dataOffset + ((long)y * Width + x) * bytesPerPixel);
        Span<byte> bytes = stackalloc byte[8];
        stream.Position = offset;
        stream.ReadExactly(bytes[..bytesPerPixel]);
        double raw = Bitpix switch {
            8 => bytes[0],
            16 => BinaryPrimitives.ReadInt16BigEndian(bytes),
            32 => BinaryPrimitives.ReadInt32BigEndian(bytes),
            -32 => BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32BigEndian(bytes)),
            -64 => BitConverter.Int64BitsToDouble(
                BinaryPrimitives.ReadInt64BigEndian(bytes)),
            _ => throw new InvalidOperationException()
        };
        if (blank.HasValue && raw == blank.Value) { return double.NaN; }
        return bzero + bscale * raw;
    }

    public void Dispose() => stream.Dispose();

    private int GetRequiredInt(string key) {
        var value = GetRequiredString(key);
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var parsed)) {
            throw new InvalidDataException($"FITS keyword {key} is not an integer.");
        }
        return parsed;
    }

    private double GetOptionalDouble(string key, double fallback) =>
        header.TryGetValue(key, out var value)
            && double.TryParse(value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var parsed)
            ? parsed : fallback;

    private long? GetOptionalLong(string key) =>
        header.TryGetValue(key, out var value)
            && long.TryParse(value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var parsed)
            ? parsed : null;

    private static Dictionary<string, string> ReadHeader(
            FileStream stream, out long dataOffset) {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var card = new byte[80];
        var cards = 0;
        var ended = false;
        while (!ended) {
            stream.ReadExactly(card);
            cards++;
            var text = Encoding.ASCII.GetString(card);
            var key = text[..8].Trim();
            if (key == "END") {
                ended = true;
                break;
            }
            if (text.Length >= 10 && text[8] == '=') {
                var raw = StripComment(text[10..]).Trim();
                if (raw.Length >= 2 && raw[0] == '\'' && raw[^1] == '\'') {
                    raw = raw[1..^1].Replace("''", "'").TrimEnd();
                }
                result[key] = raw;
            }
            if (cards > 10000) {
                throw new InvalidDataException("FITS END card was not found.");
            }
        }
        var headerBytes = checked(cards * 80L);
        dataOffset = ((headerBytes + 2879) / 2880) * 2880;
        stream.Position = dataOffset;
        return result;
    }

    private static string StripComment(string value) {
        var quoted = false;
        for (var index = 0; index < value.Length; index++) {
            if (value[index] == '\'') {
                if (quoted && index + 1 < value.Length && value[index + 1] == '\'') {
                    index++;
                } else {
                    quoted = !quoted;
                }
            } else if (value[index] == '/' && !quoted) {
                return value[..index];
            }
        }
        return value;
    }
}

internal static class TppaActualExposureStarShapeAnalyzer {
    private sealed record OpenFrame(
        TppaActualExposureFrameSource Source,
        TppaFitsImage Fits,
        IReadOnlyList<TppaAstapStar> Stars);

    private sealed record MatchedStar(
        TppaAstapStar LongStar,
        TppaAdaptiveStarMoment LongMoment,
        IReadOnlyList<TppaAdaptiveStarMoment> PreMoments,
        IReadOnlyList<TppaAdaptiveStarMoment> PostMoments,
        string Zone,
        double MajorGrowth,
        double DeltaE1,
        double DeltaE2,
        double LongEccentricity);

    public static TppaActualExposureStarShapeResult Analyze(
            TppaActualExposureStarShapePolicy policy,
            IReadOnlyList<TppaActualExposureFrameSource> sources) {
        var issues = ValidatePolicy(policy);
        if (issues.Count > 0) {
            return Invalid(policy?.OpticalTrainId, issues);
        }
        var frames = new List<OpenFrame>();
        try {
            foreach (var source in sources ?? Array.Empty<TppaActualExposureFrameSource>()) {
                frames.Add(new(source, TppaFitsImage.Open(source.FitsPath),
                    TppaAstapCatalogReader.Read(source.AstapCatalogPath)));
            }
            return AnalyzeOpen(policy, frames);
        } catch (Exception exception) when (exception is IOException
                or InvalidDataException or UnauthorizedAccessException
                or ArgumentException or ArithmeticException) {
            issues.Add($"actual-exposure evidence failed closed ({exception.GetType().Name}): {exception.Message}");
            return Invalid(policy.OpticalTrainId, issues);
        } finally {
            foreach (var frame in frames) { frame.Fits.Dispose(); }
        }
    }

    private static TppaActualExposureStarShapeResult AnalyzeOpen(
            TppaActualExposureStarShapePolicy policy,
            IReadOnlyList<OpenFrame> frames) {
        var issues = new List<string>();
        var pre = frames.Where(frame => frame.Source.Role == TppaActualExposureRoles.PreControl)
            .OrderBy(frame => frame.Fits.DateObsUtc).ToArray();
        var post = frames.Where(frame => frame.Source.Role == TppaActualExposureRoles.PostControl)
            .OrderBy(frame => frame.Fits.DateObsUtc).ToArray();
        var longs = frames.Where(frame => frame.Source.Role == TppaActualExposureRoles.LongExposure)
            .ToArray();
        if (pre.Length < policy.MinimumPreControlFrames
                || post.Length < policy.MinimumPostControlFrames || longs.Length != 1
                || frames.Count != pre.Length + post.Length + 1) {
            issues.Add("one long exposure and the required split pre/post control bracket are mandatory");
            return Invalid(policy.OpticalTrainId, issues, pre.Length, post.Length);
        }
        var longFrame = longs[0];
        var all = pre.Concat(new[] { longFrame }).Concat(post).ToArray();
        if (all.Any(frame => frame.Fits.Width != longFrame.Fits.Width
                || frame.Fits.Height != longFrame.Fits.Height)) {
            issues.Add("FITS dimensions changed within the exposure bracket");
        }
        foreach (var key in policy.RequiredEqualHeaderKeywords ?? Array.Empty<string>()) {
            var values = new List<string>();
            foreach (var frame in all) {
                if (!frame.Fits.TryGetString(key, out var value)) {
                    issues.Add($"required FITS state keyword {key} is missing");
                    break;
                }
                values.Add(value);
            }
            if (values.Count == all.Length
                    && values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1) {
                issues.Add($"FITS state keyword {key} changed within the bracket");
            }
        }
        var measuredScales = new List<double>();
        foreach (var frame in all) {
            if (!frame.Fits.TryGetPixelScaleArcsecondsPerPixel(
                    out var measuredScale, out var anisotropy)) {
                issues.Add("FITS WCS pixel scale is missing or invalid");
                continue;
            }
            measuredScales.Add(measuredScale);
            if (anisotropy > policy.MaximumPixelScaleAnisotropyFraction) {
                issues.Add("FITS WCS pixel-scale anisotropy exceeds the preregistered limit");
            }
            var relativeScaleError = Math.Abs(measuredScale
                - policy.PixelScaleArcsecondsPerPixel)
                / policy.PixelScaleArcsecondsPerPixel;
            if (relativeScaleError > policy.MaximumPixelScaleRelativeError) {
                issues.Add("FITS WCS pixel scale disagrees with the optical-train policy");
            }
        }
        var bracketPixelScale = Median(measuredScales);
        if (pre.Any(frame => frame.Fits.ExposureSeconds < policy.MinimumControlExposureSeconds
                    || frame.Fits.ExposureSeconds > policy.MaximumControlExposureSeconds)
                || post.Any(frame => frame.Fits.ExposureSeconds < policy.MinimumControlExposureSeconds
                    || frame.Fits.ExposureSeconds > policy.MaximumControlExposureSeconds)) {
            issues.Add("control exposure duration is outside the preregistered range");
        }
        if (Math.Abs(longFrame.Fits.ExposureSeconds - policy.ExpectedLongExposureSeconds)
                > policy.LongExposureToleranceSeconds) {
            issues.Add("long exposure duration is outside the preregistered tolerance");
        }
        var longEnd = longFrame.Fits.DateObsUtc.AddSeconds(longFrame.Fits.ExposureSeconds);
        var preEnd = pre[^1].Fits.DateObsUtc.AddSeconds(pre[^1].Fits.ExposureSeconds);
        if (longFrame.Fits.DateObsUtc < preEnd
                || (longFrame.Fits.DateObsUtc - preEnd).TotalSeconds
                    > policy.MaximumControlToLongGapSeconds
                || post[0].Fits.DateObsUtc < longEnd
                || (post[0].Fits.DateObsUtc - longEnd).TotalSeconds
                    > policy.MaximumControlToLongGapSeconds) {
            issues.Add("control bracket does not tightly bound the long exposure");
        }
        if ((post[^1].Fits.DateObsUtc.AddSeconds(post[^1].Fits.ExposureSeconds)
                - pre[0].Fits.DateObsUtc).TotalMinutes > policy.MaximumBracketSpanMinutes) {
            issues.Add("exposure bracket exceeds its preregistered total span");
        }
        if (issues.Count > 0) {
            return Invalid(policy.OpticalTrainId, issues, pre.Length, post.Length,
                longFrame.Stars.Count);
        }

        var controlFrames = pre.Concat(post).ToArray();
        var matched = new List<MatchedStar>();
        var controlQualifiedCandidates = 0;
        var longFrameRejected = 0;
        var linearityRejected = 0;
        foreach (var anchorStar in pre[0].Stars) {
            if (IsBlended(anchorStar, pre[0].Stars,
                    policy.BlendExclusionRadiusArcseconds)) {
                continue;
            }
            var controlStars = new List<(OpenFrame Frame, TppaAstapStar Star)>();
            var rejected = false;
            foreach (var frame in controlFrames) {
                var candidate = FindUniqueMatch(anchorStar, frame.Stars,
                    policy.MaximumCatalogMatchArcseconds);
                if (candidate == null || IsBlended(candidate, frame.Stars,
                        policy.BlendExclusionRadiusArcseconds)) {
                    rejected = true;
                    break;
                }
                controlStars.Add((frame, candidate));
            }
            if (rejected) { continue; }
            var moments = new List<TppaAdaptiveStarMoment>();
            foreach (var pair in controlStars) {
                var moment = Measure(pair.Frame.Fits, pair.Star, policy, out _);
                if (moment == null) {
                    rejected = true;
                    break;
                }
                moments.Add(moment);
            }
            if (rejected) { continue; }
            controlQualifiedCandidates++;
            var longStar = FindUniqueMatch(anchorStar, longFrame.Stars,
                policy.MaximumCatalogMatchArcseconds);
            if (longStar == null || IsBlended(longStar, longFrame.Stars,
                    policy.BlendExclusionRadiusArcseconds)) {
                longFrameRejected++;
                continue;
            }
            var longMoment = Measure(longFrame.Fits, longStar, policy, out var longReason);
            if (longMoment == null) {
                longFrameRejected++;
                if (longReason == "linearity") { linearityRejected++; }
                continue;
            }
            var preMoments = moments.Take(pre.Length).ToArray();
            var postMoments = moments.Skip(pre.Length).ToArray();
            var controls = preMoments.Concat(postMoments).ToArray();
            var baseMajor = Median(controls.Select(value => value.MajorSigmaPixels));
            var baseE1 = Median(controls.Select(value => value.Ellipticity1));
            var baseE2 = Median(controls.Select(value => value.Ellipticity2));
            matched.Add(new(longStar, longMoment, preMoments, postMoments,
                Zone(longStar, longFrame.Fits.Width, longFrame.Fits.Height),
                (longMoment.MajorSigmaPixels - baseMajor) / baseMajor,
                longMoment.Ellipticity1 - baseE1,
                longMoment.Ellipticity2 - baseE2,
                longMoment.Eccentricity));
        }

        var candidateCount = controlQualifiedCandidates;
        var attritionFraction = candidateCount == 0 ? 1.0
            : (double)longFrameRejected / candidateCount;
        var linearityFraction = candidateCount == 0 ? 1.0
            : (double)linearityRejected / candidateCount;
        if (matched.Count < policy.MinimumMatchedStars) {
            issues.Add($"matched usable star floor is unmet ({matched.Count}/{policy.MinimumMatchedStars})");
        }
        if (linearityFraction > policy.MaximumLinearityRejectionFraction) {
            issues.Add("linearity/saturation rejection fraction exceeds the preregistered limit");
        }
        if (attritionFraction > policy.MaximumLongFrameAttritionFraction) {
            issues.Add("long-frame star attrition exceeds the preregistered limit");
        }
        var zoneNames = new[] { "Center", "OuterNW", "OuterNE", "OuterSW", "OuterSE" };
        var zones = new List<TppaActualExposureZoneSummary>();
        foreach (var name in zoneNames) {
            var values = matched.Where(value => value.Zone == name).ToArray();
            if (values.Length < policy.MinimumStarsPerZone) {
                issues.Add($"zone {name} star floor is unmet ({values.Length}/{policy.MinimumStarsPerZone})");
            }
            zones.Add(SummarizeZone(name, values));
        }
        if (matched.Count == 0) {
            return Invalid(policy.OpticalTrainId, issues, pre.Length, post.Length,
                candidateCount, longFrameRejected, attritionFraction, 0,
                linearityRejected, linearityFraction, bracketPixelScale, zones);
        }
        var medianControlFwhm = 2.354820045 * Median(matched.SelectMany(value =>
            value.PreMoments.Concat(value.PostMoments))
            .Select(moment => moment.MajorSigmaPixels));
        var controlFrameMedians = Enumerable.Range(0, controlFrames.Length)
            .Select(index => Median(matched.Select(value => index < pre.Length
                ? value.PreMoments[index].MajorSigmaPixels
                : value.PostMoments[index - pre.Length].MajorSigmaPixels)))
            .ToArray();
        var controlFrameCenter = Median(controlFrameMedians);
        var controlFrameScatter = controlFrameMedians.Max(value =>
            Math.Abs(value - controlFrameCenter))
            / Math.Max(controlFrameCenter, 1e-12);
        var medianGrowth = Median(matched.Select(value => value.MajorGrowth));
        var medianDeltaE1 = Median(matched.Select(value => value.DeltaE1));
        var medianDeltaE2 = Median(matched.Select(value => value.DeltaE2));
        var differentialE = Math.Sqrt(medianDeltaE1 * medianDeltaE1
            + medianDeltaE2 * medianDeltaE2);
        var preMajor = Median(matched.Select(value => Median(
            value.PreMoments.Select(moment => moment.MajorSigmaPixels))));
        var postMajor = Median(matched.Select(value => Median(
            value.PostMoments.Select(moment => moment.MajorSigmaPixels))));
        var prePostDrift = Math.Abs(postMajor - preMajor) / Median(new[] { preMajor, postMajor });
        var longMedianEccentricity = Median(matched.Select(value => value.LongEccentricity));
        var outerGrowth = zones.Where(zone => zone.Zone.StartsWith("Outer", StringComparison.Ordinal))
            .Select(zone => zone.MedianMajorSigmaGrowthFraction).DefaultIfEmpty(double.PositiveInfinity).Max();
        if (medianGrowth > policy.MaximumMedianMajorSigmaGrowthFraction) {
            issues.Add("median long-exposure major-axis growth exceeds the preregistered limit");
        }
        if (outerGrowth > policy.MaximumOuterMedianMajorSigmaGrowthFraction) {
            issues.Add("an outer-zone major-axis growth exceeds the preregistered limit");
        }
        if (medianControlFwhm < policy.MinimumQualifiedFwhmPixels) {
            issues.Add("measured control PSF is undersampled for qualified adaptive-moment evidence");
        }
        if (controlFrameScatter > policy.MaximumControlFrameMajorSigmaScatterFraction) {
            issues.Add("control-frame star-shape scatter exceeds the preregistered limit");
        }
        if (differentialE > policy.MaximumMedianDifferentialEllipticity) {
            issues.Add("signed median differential ellipticity exceeds the preregistered limit");
        }
        if (prePostDrift > policy.MaximumPrePostMajorSigmaDriftFraction) {
            issues.Add("pre/post control bracket indicates focus or seeing nonstationarity");
        }
        if (policy.AbsoluteEccentricityQualified
                && longMedianEccentricity > policy.MaximumLongMedianEccentricity) {
            issues.Add("long-exposure median eccentricity exceeds the train-qualified limit");
        }
        var qualified = issues.Count == 0;
        return new(true, true, qualified, policy.OpticalTrainId,
            policy.AbsoluteEccentricityQualified, pre.Length, post.Length,
            candidateCount, longFrameRejected, attritionFraction,
            matched.Count, linearityRejected, linearityFraction,
            bracketPixelScale, medianControlFwhm, controlFrameScatter,
            Median(matched.Select(value => value.LongMoment.MajorSigmaPixels))
                * policy.PixelScaleArcsecondsPerPixel,
            medianGrowth, differentialE, prePostDrift, longMedianEccentricity,
            zones, issues);
    }

    private static TppaAdaptiveStarMoment Measure(
            TppaFitsImage image, TppaAstapStar star,
            TppaActualExposureStarShapePolicy policy, out string reason) {
        reason = string.Empty;
        var cx = star.X - 1.0;
        var cy = star.Y - 1.0;
        var aperture = policy.ApertureRadiusArcseconds / policy.PixelScaleArcsecondsPerPixel;
        var inner = policy.BackgroundInnerRadiusArcseconds / policy.PixelScaleArcsecondsPerPixel;
        var outer = policy.BackgroundOuterRadiusArcseconds / policy.PixelScaleArcsecondsPerPixel;
        var margin = (int)Math.Ceiling(outer) + 2;
        if (cx < margin || cy < margin || cx >= image.Width - margin
                || cy >= image.Height - margin) {
            reason = "edge";
            return null;
        }
        for (var y = (int)Math.Floor(cy - outer); y <= Math.Ceiling(cy + outer); y++) {
            for (var x = (int)Math.Floor(cx - outer); x <= Math.Ceiling(cx + outer); x++) {
                var dx = x - cx; var dy = y - cy;
                if (dx * dx + dy * dy <= outer * outer
                        && !double.IsFinite(image.ReadPixel(x, y))) {
                    reason = "nonfinite";
                    return null;
                }
            }
        }
        var plane = FitBackgroundPlane(image, cx, cy, inner, outer);
        if (plane == null) { reason = "background"; return null; }
        var peak = double.NegativeInfinity;
        var lit = 0;
        for (var y = (int)Math.Floor(cy - aperture); y <= Math.Ceiling(cy + aperture); y++) {
            for (var x = (int)Math.Floor(cx - aperture); x <= Math.Ceiling(cx + aperture); x++) {
                var dx = x - cx; var dy = y - cy;
                if (dx * dx + dy * dy > aperture * aperture) { continue; }
                var value = image.ReadPixel(x, y);
                peak = Math.Max(peak, value);
                if (value - plane.ValueAt(x, y) > 4 * plane.Noise) { lit++; }
            }
        }
        if (peak >= policy.LinearityCeilingAdu) { reason = "linearity"; return null; }
        if (lit < 3) { reason = "hot-pixel"; return null; }

        var initialSigma = Math.Max(0.8, star.HfdPixels / 2.3548);
        var mxx = initialSigma * initialSigma;
        var myy = mxx;
        var mxy = 0.0;
        var converged = false;
        var flux = 0.0;
        var usedPixels = 0;
        var iterations = 0;
        for (; iterations < 12; iterations++) {
            var wxx = Math.Max(0.5, 2.0 * mxx);
            var wyy = Math.Max(0.5, 2.0 * myy);
            var wxy = 2.0 * mxy;
            if (!Invert2(wxx, wyy, wxy, out var iwxx, out var iwyy, out var iwxy)) {
                reason = "weight"; return null;
            }
            double sum = 0, sx = 0, sy = 0;
            usedPixels = 0;
            for (var y = (int)Math.Floor(cy - aperture); y <= Math.Ceiling(cy + aperture); y++) {
                for (var x = (int)Math.Floor(cx - aperture); x <= Math.Ceiling(cx + aperture); x++) {
                    var dx = x - cx; var dy = y - cy;
                    if (dx * dx + dy * dy > aperture * aperture) { continue; }
                    var signal = image.ReadPixel(x, y) - plane.ValueAt(x, y);
                    if (signal <= 0) { continue; }
                    var exponent = -0.5 * (iwxx * dx * dx + 2 * iwxy * dx * dy + iwyy * dy * dy);
                    if (exponent < -20) { continue; }
                    var weight = signal * Math.Exp(exponent);
                    sum += weight; sx += weight * x; sy += weight * y; usedPixels++;
                }
            }
            if (sum <= 0 || usedPixels < 6) { reason = "flux"; return null; }
            var nx = sx / sum; var ny = sy / sum;
            double cxx = 0, cyy = 0, cxy = 0;
            for (var y = (int)Math.Floor(cy - aperture); y <= Math.Ceiling(cy + aperture); y++) {
                for (var x = (int)Math.Floor(cx - aperture); x <= Math.Ceiling(cx + aperture); x++) {
                    var dx0 = x - cx; var dy0 = y - cy;
                    if (dx0 * dx0 + dy0 * dy0 > aperture * aperture) { continue; }
                    var signal = image.ReadPixel(x, y) - plane.ValueAt(x, y);
                    if (signal <= 0) { continue; }
                    var exponent = -0.5 * (iwxx * dx0 * dx0 + 2 * iwxy * dx0 * dy0 + iwyy * dy0 * dy0);
                    if (exponent < -20) { continue; }
                    var weight = signal * Math.Exp(exponent);
                    var dx = x - nx; var dy = y - ny;
                    cxx += weight * dx * dx; cyy += weight * dy * dy; cxy += weight * dx * dy;
                }
            }
            cxx /= sum; cyy /= sum; cxy /= sum;
            if (!Invert2(cxx, cyy, cxy, out var icxx, out var icyy, out var icxy)) {
                reason = "moment"; return null;
            }
            var tx = icxx - iwxx; var ty = icyy - iwyy; var txy = icxy - iwxy;
            if (!Invert2(tx, ty, txy, out var nmxx, out var nmyy, out var nmxy)) {
                reason = "deconvolution"; return null;
            }
            nmxx -= 1.0 / 12.0; nmyy -= 1.0 / 12.0;
            if (nmxx <= 0 || nmyy <= 0 || nmxx * nmyy <= nmxy * nmxy) {
                reason = "pixelization"; return null;
            }
            var centerShift = Math.Sqrt((nx - cx) * (nx - cx) + (ny - cy) * (ny - cy));
            var tensorShift = Math.Max(Math.Abs(nmxx - mxx), Math.Abs(nmyy - myy))
                / Math.Max(1e-9, Math.Max(mxx, myy));
            cx = nx; cy = ny; mxx = nmxx; myy = nmyy; mxy = nmxy;
            if (centerShift < 0.05 && tensorShift < 0.02) { converged = true; break; }
        }
        if (!converged) { reason = "nonconvergent"; return null; }
        flux = 0;
        for (var y = (int)Math.Floor(cy - aperture); y <= Math.Ceiling(cy + aperture); y++) {
            for (var x = (int)Math.Floor(cx - aperture); x <= Math.Ceiling(cx + aperture); x++) {
                var dx = x - cx; var dy = y - cy;
                if (dx * dx + dy * dy <= aperture * aperture) {
                    flux += Math.Max(0, image.ReadPixel(x, y) - plane.ValueAt(x, y));
                }
            }
        }
        var electrons = flux * policy.GainElectronsPerAdu;
        var noiseElectrons = plane.Noise * policy.GainElectronsPerAdu;
        var apertureNoise = Math.Sqrt(Math.Max(1e-12,
            electrons + usedPixels * noiseElectrons * noiseElectrons));
        var snr = electrons / apertureNoise;
        var backgroundFraction = plane.Noise * Math.Sqrt(usedPixels) / Math.Max(flux, 1e-12);
        if (snr < policy.MinimumApertureSnr) { reason = "snr"; return null; }
        if (backgroundFraction > policy.MaximumBackgroundFluxNoiseFraction) {
            reason = "background-noise"; return null;
        }
        var trace = mxx + myy;
        var root = Math.Sqrt((mxx - myy) * (mxx - myy) + 4 * mxy * mxy);
        var lambdaMajor = 0.5 * (trace + root);
        var lambdaMinor = 0.5 * (trace - root);
        if (lambdaMinor <= 0) { reason = "moment"; return null; }
        var e1 = (mxx - myy) / trace;
        var e2 = 2 * mxy / trace;
        return new(cx, cy, mxx, myy, mxy, Math.Sqrt(lambdaMajor),
            Math.Sqrt(lambdaMinor), e1, e2,
            Math.Sqrt(Math.Max(0, 1 - lambdaMinor / lambdaMajor)),
            0.5 * Math.Atan2(2 * mxy, mxx - myy) * 180 / Math.PI,
            snr, backgroundFraction, peak, flux, iterations + 1, true);
    }

    private sealed record BackgroundPlane(double A, double B, double C, double Noise) {
        public double ValueAt(double x, double y) => A + B * x + C * y;
    }

    private static BackgroundPlane FitBackgroundPlane(
            TppaFitsImage image, double cx, double cy, double inner, double outer) {
        var samples = new List<(double X, double Y, double Z)>();
        for (var y = (int)Math.Floor(cy - outer); y <= Math.Ceiling(cy + outer); y++) {
            for (var x = (int)Math.Floor(cx - outer); x <= Math.Ceiling(cx + outer); x++) {
                var dx = x - cx; var dy = y - cy; var r2 = dx * dx + dy * dy;
                if (r2 >= inner * inner && r2 <= outer * outer) {
                    samples.Add((x, y, image.ReadPixel(x, y)));
                }
            }
        }
        if (samples.Count < 30) { return null; }
        BackgroundPlane plane = null;
        var active = samples;
        for (var iteration = 0; iteration < 4; iteration++) {
            if (!FitPlane(active, out var a, out var b, out var c)) { return null; }
            var residuals = active.Select(value => value.Z - (a + b * value.X + c * value.Y)).ToArray();
            var center = Median(residuals);
            var noise = 1.4826 * Median(residuals.Select(value => Math.Abs(value - center)));
            if (!double.IsFinite(noise) || noise <= 0) { noise = 1.0; }
            plane = new(a + center, b, c, noise);
            var clipped = samples.Where(value => Math.Abs(value.Z - plane.ValueAt(value.X, value.Y))
                <= 3.5 * noise).ToList();
            if (clipped.Count < 30) { return null; }
            if (clipped.Count == active.Count) { break; }
            active = clipped;
        }
        return plane;
    }

    private static bool FitPlane(IReadOnlyList<(double X, double Y, double Z)> values,
            out double a, out double b, out double c) {
        double n = values.Count, sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0;
        double sz = 0, sxz = 0, syz = 0;
        foreach (var v in values) {
            sx += v.X; sy += v.Y; sxx += v.X * v.X; syy += v.Y * v.Y;
            sxy += v.X * v.Y; sz += v.Z; sxz += v.X * v.Z; syz += v.Y * v.Z;
        }
        var matrix = new[,] { { n, sx, sy }, { sx, sxx, sxy }, { sy, sxy, syy } };
        var rhs = new[] { sz, sxz, syz };
        return Solve3(matrix, rhs, out a, out b, out c);
    }

    private static bool Solve3(double[,] m, double[] r,
            out double x0, out double x1, out double x2) {
        var a = new double[3, 4];
        for (var i = 0; i < 3; i++) { for (var j = 0; j < 3; j++) a[i, j] = m[i, j]; a[i, 3] = r[i]; }
        for (var col = 0; col < 3; col++) {
            var pivot = col;
            for (var row = col + 1; row < 3; row++) if (Math.Abs(a[row, col]) > Math.Abs(a[pivot, col])) pivot = row;
            if (Math.Abs(a[pivot, col]) < 1e-12) { x0 = x1 = x2 = 0; return false; }
            if (pivot != col) for (var j = col; j < 4; j++) (a[col, j], a[pivot, j]) = (a[pivot, j], a[col, j]);
            var scale = a[col, col]; for (var j = col; j < 4; j++) a[col, j] /= scale;
            for (var row = 0; row < 3; row++) if (row != col) {
                    var factor = a[row, col]; for (var j = col; j < 4; j++) a[row, j] -= factor * a[col, j];
                }
        }
        x0 = a[0, 3]; x1 = a[1, 3]; x2 = a[2, 3]; return true;
    }

    private static bool Invert2(double xx, double yy, double xy,
            out double ixx, out double iyy, out double ixy) {
        var determinant = xx * yy - xy * xy;
        if (!double.IsFinite(determinant) || determinant <= 1e-12) {
            ixx = iyy = ixy = 0; return false;
        }
        ixx = yy / determinant; iyy = xx / determinant; ixy = -xy / determinant; return true;
    }

    private static bool IsBlended(TppaAstapStar target,
            IReadOnlyList<TppaAstapStar> stars, double radiusArcseconds) =>
        stars.Any(value => !ReferenceEquals(value, target)
            && SeparationArcseconds(target, value) < radiusArcseconds);

    private static TppaAstapStar FindUniqueMatch(TppaAstapStar target,
            IReadOnlyList<TppaAstapStar> stars, double maximumArcseconds) {
        var candidates = stars.Select(value => new {
            Star = value,
            Separation = SeparationArcseconds(target, value)
        })
            .Where(value => value.Separation <= maximumArcseconds)
            .OrderBy(value => value.Separation).Take(2).ToArray();
        return candidates.Length == 1 ? candidates[0].Star : null;
    }

    private static double SeparationArcseconds(TppaAstapStar left, TppaAstapStar right) {
        var dec1 = left.DeclinationDegrees * Math.PI / 180;
        var dec2 = right.DeclinationDegrees * Math.PI / 180;
        var dra = ((right.RightAscensionDegrees - left.RightAscensionDegrees + 540) % 360 - 180)
            * Math.PI / 180;
        var cosine = Math.Sin(dec1) * Math.Sin(dec2)
            + Math.Cos(dec1) * Math.Cos(dec2) * Math.Cos(dra);
        return Math.Acos(Math.Clamp(cosine, -1, 1)) * 180 / Math.PI * 3600;
    }

    private static string Zone(TppaAstapStar star, int width, int height) {
        var dx = (star.X - 0.5 * (width + 1)) / (0.5 * width);
        var dy = (star.Y - 0.5 * (height + 1)) / (0.5 * height);
        var radius = Math.Sqrt(dx * dx + dy * dy);
        if (radius <= 0.45) { return "Center"; }
        if (radius < 0.65) { return "Middle"; }
        return (dy >= 0, dx >= 0) switch {
            (true, false) => "OuterNW",
            (true, true) => "OuterNE",
            (false, false) => "OuterSW",
            _ => "OuterSE"
        };
    }

    private static TppaActualExposureZoneSummary SummarizeZone(
            string name, IReadOnlyList<MatchedStar> stars) {
        if (stars.Count == 0) {
            return new(name, 0, double.NaN, double.NaN, double.NaN,
                double.NaN, double.NaN);
        }
        var de1 = Median(stars.Select(value => value.DeltaE1));
        var de2 = Median(stars.Select(value => value.DeltaE2));
        return new(name, stars.Count,
            Median(stars.Select(value => value.MajorGrowth)), de1, de2,
            Math.Sqrt(de1 * de1 + de2 * de2),
            Median(stars.Select(value => value.LongEccentricity)));
    }

    private static List<string> ValidatePolicy(TppaActualExposureStarShapePolicy policy) {
        var issues = new List<string>();
        if (policy == null) { issues.Add("star-shape policy is missing"); return issues; }
        if (policy.SchemaVersion != TppaActualExposureStarShapePolicy.CurrentSchemaVersion
                || string.IsNullOrWhiteSpace(policy.OpticalTrainId)) issues.Add("star-shape policy schema or optical-train identity is invalid");
        var positive = new[] { policy.PixelScaleArcsecondsPerPixel, policy.GainElectronsPerAdu,
            policy.ApertureRadiusArcseconds, policy.BackgroundInnerRadiusArcseconds,
            policy.BackgroundOuterRadiusArcseconds, policy.BlendExclusionRadiusArcseconds,
            policy.LinearityCeilingAdu, policy.MinimumApertureSnr,
            policy.MaximumCatalogMatchArcseconds, policy.MinimumControlExposureSeconds,
            policy.MaximumControlExposureSeconds, policy.ExpectedLongExposureSeconds,
            policy.LongExposureToleranceSeconds, policy.MaximumControlToLongGapSeconds,
            policy.MaximumBracketSpanMinutes };
        if (positive.Any(value => !double.IsFinite(value) || value <= 0)
                || policy.BackgroundInnerRadiusArcseconds <= policy.ApertureRadiusArcseconds
                || policy.BackgroundOuterRadiusArcseconds <= policy.BackgroundInnerRadiusArcseconds
                || policy.MaximumControlExposureSeconds < policy.MinimumControlExposureSeconds
                || policy.MinimumPreControlFrames < 5 || policy.MinimumPostControlFrames < 5
                || policy.MinimumMatchedStars < 5 || policy.MinimumStarsPerZone < 1) {
            issues.Add("star-shape policy numeric bounds are invalid");
        }
        var fractions = new[] { policy.MaximumBackgroundFluxNoiseFraction,
            policy.MaximumMedianDifferentialEllipticity,
            policy.MaximumMedianMajorSigmaGrowthFraction,
            policy.MaximumOuterMedianMajorSigmaGrowthFraction,
            policy.MaximumPrePostMajorSigmaDriftFraction,
            policy.MaximumLinearityRejectionFraction,
            policy.MaximumLongFrameAttritionFraction,
            policy.MaximumPixelScaleRelativeError,
            policy.MaximumPixelScaleAnisotropyFraction,
            policy.MaximumControlFrameMajorSigmaScatterFraction,
            policy.MaximumLongMedianEccentricity };
        if (fractions.Any(value => !double.IsFinite(value) || value <= 0 || value >= 1)) {
            issues.Add("star-shape policy fractional bounds are invalid");
        }
        if (policy.RequiredEqualHeaderKeywords == null
                || policy.RequiredEqualHeaderKeywords.Length == 0
                || policy.RequiredEqualHeaderKeywords.Any(string.IsNullOrWhiteSpace)) {
            issues.Add("star-shape policy required FITS state keys are missing");
        }
        if (!double.IsFinite(policy.MinimumQualifiedFwhmPixels)
                || policy.MinimumQualifiedFwhmPixels < 2.0) {
            issues.Add("star-shape policy sampling floor is invalid");
        }
        return issues;
    }

    private static TppaActualExposureStarShapeResult Invalid(
            string train, IReadOnlyList<string> issues, int pre = 0, int post = 0,
            int candidates = 0, int longRejected = 0,
            double attritionFraction = 0, int matched = 0, int linearity = 0,
            double linearityFraction = 0, double measuredScale = double.NaN,
            IReadOnlyList<TppaActualExposureZoneSummary> zones = null) =>
        new(false, candidates > 0, false, train ?? string.Empty, false,
            pre, post, candidates, longRejected, attritionFraction,
            matched, linearity, linearityFraction, measuredScale,
            double.NaN, double.NaN,
            double.NaN, double.NaN, double.NaN, double.NaN, double.NaN,
            zones ?? Array.Empty<TppaActualExposureZoneSummary>(), issues);

    private static double Median(IEnumerable<double> values) {
        var ordered = values.Where(double.IsFinite).OrderBy(value => value).ToArray();
        if (ordered.Length == 0) { return double.NaN; }
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 1 ? ordered[middle]
            : 0.5 * (ordered[middle - 1] + ordered[middle]);
    }
}
