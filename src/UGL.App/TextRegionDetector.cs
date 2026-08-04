using Microsoft.Extensions.Logging;
using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace UGL.App;

/// <summary>
/// Finds text/logo-like regions anywhere on a cover, using a PP-OCRv3 "DB"
/// (Differentiable Binarization) text-detection ONNX model, run through OpenCV's
/// generic DNN module. This exists to catch stray publisher/platform badges that
/// LogoRegionDetector's template match (which only knows the shape of the game's own
/// scraped logo) can't — those badges aren't an asset UGL has on file to match against.
///
/// OpenCvSharp doesn't expose OpenCV's own high-level `TextDetectionModel_DB` wrapper
/// (checked via reflection — not present in this version), so this is a hand-rolled,
/// simplified port of that class's pre/post-processing, verified against the model's
/// own reference script (opencv_zoo/models/text_detection_ppocr/ppocr_det.py) for the
/// preprocessing constants, but simplified in one deliberate way: the reference
/// algorithm's "unclip" step expands each detected polygon outward using proper
/// polygon offsetting; this port approximates that with a simple padded bounding
/// rectangle instead, since the downstream use (inpainting mask regions) only needs
/// coverage, not precise oriented quadrilaterals for text reading.
///
/// Best-effort: this has not been visually verified against real output (no interactive
/// test loop available while writing it), so treat results as a starting point that may
/// need threshold/padding tuning once tested against real covers.
/// </summary>
internal static class TextRegionDetector
{
    private const int InputSize = 736;
    private const double BinaryThreshold = 0.3;
    private const double PadFactor = 0.4;
    private const int MinBoxArea = 40;

    /// <summary>ImageNet-standard per-channel mean/std, in RGB order — the exact
    /// constants the model's own reference script uses (ppocr_det.py:
    /// setInputMean((123.675, 116.28, 103.53)),
    /// setInputScale(1/255/[0.229, 0.224, 0.225])).</summary>
    private static readonly Scalar MeanRgb = new(123.675, 116.28, 103.53);
    private static readonly double[] StdRgb = [0.229, 0.224, 0.225];

    public static List<System.Drawing.Rectangle> DetectTextRegions(
        byte[] imageBytes, string onnxModelPath, ILogger logger)
    {
        try
        {
            using var image = Cv2.ImDecode(imageBytes, ImreadModes.Color);
            if (image.Empty()) return [];

            using var net = CvDnn.ReadNetFromOnnx(onnxModelPath);
            if (net is null || net.Empty())
            {
                logger.LogWarning("Failed to load text detection model at {Path}.", onnxModelPath);
                return [];
            }

            using var resized = new Mat();
            Cv2.Resize(image, resized, new Size(InputSize, InputSize));

            // BlobFromImage only takes one scalar factor for all channels (1/255 here);
            // the per-channel std division has to be applied separately afterward.
            using var blob = CvDnn.BlobFromImage(resized, 1.0 / 255.0, new Size(InputSize, InputSize),
                MeanRgb, swapRB: true, crop: false);
            DivideChannelsByStd(blob, StdRgb);

            net.SetInput(blob);
            using var output = net.Forward();

            if (output.Total() != (long)InputSize * InputSize)
            {
                logger.LogWarning("Text detection model returned an unexpected output size ({Total} elements, expected {Expected}) — skipping this pass.",
                    output.Total(), InputSize * InputSize);
                return [];
            }

            using var probMap = output.Reshape(1, [InputSize, InputSize]);
            using var binary = new Mat();
            Cv2.Threshold(probMap, binary, BinaryThreshold, 255, ThresholdTypes.Binary);
            using var binary8u = new Mat();
            binary.ConvertTo(binary8u, MatType.CV_8UC1);

            var contours = Cv2.FindContoursAsArray(binary8u, RetrievalModes.List, ContourApproximationModes.ApproxSimple);

            double scaleX = (double)image.Width / InputSize;
            double scaleY = (double)image.Height / InputSize;

            var regions = new List<System.Drawing.Rectangle>();
            foreach (var contour in contours)
            {
                var rect = Cv2.BoundingRect(contour);
                if ((long)rect.Width * rect.Height < MinBoxArea) continue;

                // Padded bounding box in place of true polygon "unclip" — see class
                // doc comment.
                int padX = (int)(rect.Width * PadFactor);
                int padY = (int)(rect.Height * PadFactor);
                int x0 = Math.Max(0, rect.X - padX);
                int y0 = Math.Max(0, rect.Y - padY);
                int x1 = Math.Min(InputSize, rect.X + rect.Width + padX);
                int y1 = Math.Min(InputSize, rect.Y + rect.Height + padY);

                regions.Add(System.Drawing.Rectangle.FromLTRB(
                    (int)(x0 * scaleX), (int)(y0 * scaleY),
                    (int)(x1 * scaleX), (int)(y1 * scaleY)));
            }
            return regions;
        }
        catch (Exception ex)
        {
            // Never let a detector failure block card art generation — the caller
            // falls back to whatever other detection succeeded, or the old fixed
            // top-band heuristic if nothing did.
            logger.LogWarning(ex, "Text region detection failed — continuing without it.");
            return [];
        }
    }

    private static void DivideChannelsByStd(Mat blob, double[] std)
    {
        // blob shape: [1, 3, H, W], NCHW contiguous float32.
        int h = blob.Size(2), w = blob.Size(3);
        for (int c = 0; c < 3; c++)
        {
            using var channel = Mat.FromPixelData(h, w, MatType.CV_32FC1, blob.Ptr(0, c));
            Cv2.Divide(channel, new Scalar(std[c]), channel);
        }
    }
}
