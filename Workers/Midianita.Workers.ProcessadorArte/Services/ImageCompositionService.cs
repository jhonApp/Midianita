using Amazon.Lambda.Core;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Midianita.Workers.ProcessadorArte.Services;

/// <summary>
/// Composes the final artwork:
///   1. Loads the AI-generated background.
///   2. Overlays the cutout image (if any) according to its placement hint.
///   3. Renders the user text on top.
///   4. Returns the result as a PNG byte array.
/// </summary>
public sealed class ImageCompositionService : IImageCompositionService
{
    public async Task<byte[]> ComposeFinalArtefactAsync(
        byte[] backgroundBytes,
        byte[]? cutoutBytes,
        string? cutoutPlacement,
        string userText,
        ILambdaLogger logger)
    {
        logger.LogInformation($"[ImageCompositionService] 🖼️  Composing image. HasCutout: {cutoutBytes is not null}, Placement: {cutoutPlacement ?? "none"}");

        using var background = Image.Load<Rgba32>(backgroundBytes);
        int bgWidth  = background.Width;
        int bgHeight = background.Height;

        // ── Overlay cutout if present ──────────────────────────────────────────
        if (cutoutBytes is not null && cutoutBytes.Length > 0)
        {
            using var cutout = Image.Load<Rgba32>(cutoutBytes);

            // Scale cutout to at most 50% of background height, keeping aspect ratio
            int targetHeight = bgHeight / 2;
            int targetWidth  = (int)((double)cutout.Width / cutout.Height * targetHeight);
            cutout.Mutate(x => x.Resize(targetWidth, targetHeight));

            var position = ResolvePlacement(cutoutPlacement, bgWidth, bgHeight, targetWidth, targetHeight);

            background.Mutate(ctx => ctx.DrawImage(cutout, position, opacity: 1f));

            logger.LogInformation($"[ImageCompositionService] ✅ Cutout overlaid at {position}");
        }

        // ── Render user text ───────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(userText))
        {
            var fontCollection = new FontCollection();
            // Use the system/bundled font (Lambda includes basic fonts). Fall back to Arial/DejaVu.
            FontFamily family = SystemFonts.TryGet("DejaVu Sans", out var found)
                ? found
                : SystemFonts.Families.First();

            var font    = family.CreateFont(48, FontStyle.Bold);
            var options = new RichTextOptions(font)
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Bottom,
                Origin              = new System.Numerics.Vector2(bgWidth / 2f, bgHeight - 60f),
                WrappingLength      = bgWidth - 80f
            };

            // Draw a subtle drop-shadow first, then the main white text
            background.Mutate(ctx =>
            {
                // 1. Salva a posição original
                var originalOrigin = options.Origin;

                // 2. Desloca a origem em +2 pixels (para a sombra)
                options.Origin = new System.Numerics.Vector2(originalOrigin.X + 2, originalOrigin.Y + 2);
                ctx.DrawText(options, userText, Color.Black.WithAlpha(0.6f));

                // 3. Restaura a posição original (para o texto principal)
                options.Origin = originalOrigin;
                ctx.DrawText(options, userText, Color.White);
            });

            logger.LogInformation($"[ImageCompositionService] ✅ Text rendered: \"{userText[..Math.Min(userText.Length, 40)]}...\"");
        }

        // ── Encode to PNG ──────────────────────────────────────────────────────
        using var output = new MemoryStream();
        await background.SaveAsync(output, new PngEncoder());
        var bytes = output.ToArray();

        logger.LogInformation($"[ImageCompositionService] ✅ Final PNG encoded — {bytes.Length} bytes");
        return bytes;
    }

    /// <summary>
    /// Translates a placement hint (e.g. "bottom-right, large scale") into a pixel Point.
    /// </summary>
    private static Point ResolvePlacement(string? hint, int bgW, int bgH, int overlayW, int overlayH)
    {
        if (string.IsNullOrWhiteSpace(hint))
            return new Point(bgW - overlayW - 20, bgH - overlayH - 20); // default: bottom-right

        hint = hint.ToLowerInvariant();

        int x = hint.Contains("right")  ? bgW - overlayW - 20
              : hint.Contains("left")   ? 20
              : (bgW - overlayW) / 2;   // center

        int y = hint.Contains("top")    ? 20
              : hint.Contains("bottom") ? bgH - overlayH - 20
              : (bgH - overlayH) / 2;   // center (middle)

        return new Point(x, y);
    }
}
