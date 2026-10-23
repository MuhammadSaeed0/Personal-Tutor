using Riwaq.Application.Abstractions;
using SkiaSharp;

namespace Riwaq.Infrastructure.Media;

/// <summary>
/// Normalises uploaded photos with SkiaSharp: decodes any supported format, applies the camera's EXIF orientation,
/// scales the long side down to <see cref="MaxSide"/> and re-encodes as JPEG, so stored pages are upright, small and
/// accepted by every vision model.
/// </summary>
public sealed class SkiaImageProcessor : IImageProcessor
{
    public const int MaxSide = 2400;
    private const int Quality = 85;

    public NormalizedImage? Normalize(byte[] data)
    {
        try
        {
            using var codec = SKCodec.Create(new MemoryStream(data));
            if (codec is null) return null;
            using var decoded = SKBitmap.Decode(codec);
            if (decoded is null || decoded.Width < 16 || decoded.Height < 16) return null;

            var origin = codec.EncodedOrigin;
            var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
            var (uprightWidth, uprightHeight) = swap ? (decoded.Height, decoded.Width) : (decoded.Width, decoded.Height);
            var scale = Math.Min(1.0, (double)MaxSide / Math.Max(uprightWidth, uprightHeight));
            var width = Math.Max(1, (int)Math.Round(uprightWidth * scale));
            var height = Math.Max(1, (int)Math.Round(uprightHeight * scale));

            using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.White); // transparent PNGs become white pages
            canvas.Scale((float)scale);
            ApplyOrientation(canvas, origin, decoded.Width, decoded.Height);
            using (var image = SKImage.FromBitmap(decoded))
                canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            canvas.Flush();

            using var snapshot = surface.Snapshot();
            using var jpeg = snapshot.Encode(SKEncodedImageFormat.Jpeg, Quality);
            return jpeg is null ? null : new NormalizedImage(jpeg.ToArray(), width, height);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NullReferenceException)
        {
            return null;
        }
    }

    /// <summary>Maps the stored pixel grid (w × h) onto the upright canvas.</summary>
    private static void ApplyOrientation(SKCanvas canvas, SKEncodedOrigin origin, int w, int h)
    {
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: canvas.Translate(w, 0); canvas.Scale(-1, 1); break;
            case SKEncodedOrigin.BottomRight: canvas.Translate(w, h); canvas.RotateDegrees(180); break;
            case SKEncodedOrigin.BottomLeft: canvas.Translate(0, h); canvas.Scale(1, -1); break;
            case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
            case SKEncodedOrigin.RightTop: canvas.Translate(h, 0); canvas.RotateDegrees(90); break;
            case SKEncodedOrigin.RightBottom: canvas.Translate(h, w); canvas.Scale(1, -1); canvas.RotateDegrees(90); break;
            case SKEncodedOrigin.LeftBottom: canvas.Translate(0, w); canvas.RotateDegrees(270); break;
        }
    }
}
