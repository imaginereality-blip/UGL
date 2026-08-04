using OpenCvSharp;

namespace UGL.App;

/// <summary>
/// Locates where a game's actual scraped logo sits on its cover art, so "Clean Cover
/// for Card" can mask exactly that region instead of assuming a fixed top band.
/// Multi-scale, alpha-masked template matching via OpenCV: the logo asset itself is
/// already the "answer" (scrapers extract it from box art in the first place), so this
/// is a template-matching problem rather than a general logo/text detection problem —
/// no training, no cloud call, fully local.
/// </summary>
internal static class LogoRegionDetector
{
    public readonly record struct Match(System.Drawing.Rectangle Bounds, double Confidence);

    /// <summary>
    /// Searches for the best match of <paramref name="logoBytes"/> (used as an
    /// alpha-masked template — only the logo's own opaque pixels count, so its actual
    /// background doesn't have to match) within <paramref name="coverBytes"/>, across a
    /// range of plausible logo sizes relative to the cover width. Returns null if no
    /// candidate scored above <paramref name="minConfidence"/> (e.g. no logo asset,
    /// corrupt image, or the logo genuinely isn't recognizable within the cover — some
    /// scraped "wheel" art is stylistically redrawn rather than cropped from the box).
    /// </summary>
    public static Match? LocateLogo(byte[] coverBytes, byte[] logoBytes, double minConfidence = 0.45)
    {
        using var coverColor = Cv2.ImDecode(coverBytes, ImreadModes.Color);
        using var logoRaw = Cv2.ImDecode(logoBytes, ImreadModes.Unchanged);
        if (coverColor.Empty() || logoRaw.Empty()) return null;

        using var coverGray = new Mat();
        Cv2.CvtColor(coverColor, coverGray, ColorConversionCodes.BGR2GRAY);

        using var logoGray = new Mat();
        Mat? alphaMask = null;
        if (logoRaw.Channels() == 4)
        {
            var channels = Cv2.Split(logoRaw); // B, G, R, A
            using (var bgr = new Mat())
            {
                Cv2.Merge([channels[0], channels[1], channels[2]], bgr);
                Cv2.CvtColor(bgr, logoGray, ColorConversionCodes.BGR2GRAY);
            }
            alphaMask = channels[3];
            channels[0].Dispose();
            channels[1].Dispose();
            channels[2].Dispose();
        }
        else
        {
            Cv2.CvtColor(logoRaw, logoGray, ColorConversionCodes.BGR2GRAY);
        }

        try
        {
            Match? best = null;
            // Plausible relative sizes of a title logo vs. the cover's own width —
            // wide enough to catch anything from a small corner wordmark to a
            // near-full-width banner treatment.
            double[] widthFractions = [0.15, 0.20, 0.25, 0.30, 0.35, 0.40, 0.45, 0.50, 0.55, 0.60, 0.65];

            foreach (var fraction in widthFractions)
            {
                int targetWidth = (int)(coverGray.Width * fraction);
                if (targetWidth < 8 || targetWidth >= coverGray.Width) continue;

                double scale = (double)targetWidth / logoGray.Width;
                int targetHeight = (int)(logoGray.Height * scale);
                if (targetHeight < 8 || targetHeight >= coverGray.Height) continue;

                using var scaledLogo = new Mat();
                Cv2.Resize(logoGray, scaledLogo, new Size(targetWidth, targetHeight));

                using var result = new Mat();
                if (alphaMask is not null)
                {
                    using var scaledMask = new Mat();
                    Cv2.Resize(alphaMask, scaledMask, new Size(targetWidth, targetHeight));
                    // Mask support in OpenCV's matchTemplate is limited to these two
                    // modes — CCorrNormed is the appropriate one for "does this
                    // masked template appear here", normalized so scale/lighting
                    // differences between the two assets don't skew the score.
                    Cv2.MatchTemplate(coverGray, scaledLogo, result, TemplateMatchModes.CCorrNormed, scaledMask);
                }
                else
                {
                    Cv2.MatchTemplate(coverGray, scaledLogo, result, TemplateMatchModes.CCoeffNormed);
                }

                Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out Point maxLoc);

                if (best is null || maxVal > best.Value.Confidence)
                {
                    best = new Match(new System.Drawing.Rectangle(maxLoc.X, maxLoc.Y, targetWidth, targetHeight), maxVal);
                }
            }

            return best is not null && best.Value.Confidence >= minConfidence ? best : null;
        }
        finally
        {
            alphaMask?.Dispose();
        }
    }
}
