using Amazon.Lambda.Core;
using Midianita.Workers.ProcessadorArte.Models;
using SkiaSharp;
using System.IO;

namespace Midianita.Workers.ProcessadorArte.Services;

public sealed class ImageCompositionService : IImageCompositionService
{
    // Canvas dimensions — matches the target banner format
    private const int CanvasWidth  = 1080;
    private const int CanvasHeight = 1350;
    private const string DefaultFont = "Arial";

    private readonly ISmartTypographyService _typography;

    public ImageCompositionService(ISmartTypographyService typography)
    {
        _typography = typography;
    }

    public async Task<byte[]> ApplyTypographyAsync(
        byte[] aiGeneratedImageBytes, BannerAnalysisResult bannerMetadata, string userText, ILambdaLogger logger)
    {
        logger.LogInformation("[ImageCompositionService] 🎨 Starting SkiaSharp composition pipeline...");

        await Task.CompletedTask; // keep async signature for interface compatibility

        using var skData      = SKData.CreateCopy(aiGeneratedImageBytes);
        using var sourceImage = SKImage.FromEncodedData(skData);

        using var surface = SKSurface.Create(new SKImageInfo(CanvasWidth, CanvasHeight));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);

        // ── 1. Draw AI-generated background (scaled to fill canvas) ──────────
        using var bgPaint = new SKPaint { FilterQuality = SKFilterQuality.High };
        var destRect = new SKRect(0, 0, CanvasWidth, CanvasHeight);
        canvas.DrawImage(sourceImage, destRect, bgPaint);

        // ── 2. Typography Overlay ─────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(userText))
        {
            logger.LogInformation($"[ImageCompositionService] ✍️ Applying typography. Placement: {bannerMetadata.LayoutRules?.TextPlacement}");

            var boundingBox = ResolveBoundingBox(bannerMetadata.LayoutRules?.TextPlacement);
            var textAlign   = ResolveTextAlign(bannerMetadata.LayoutRules?.TextAlign);

            // Drop shadow — slightly offset, translucent black
            _typography.DrawDynamicText(
                canvas,
                userText,
                new SKRect(boundingBox.Left + 4, boundingBox.Top + 4, boundingBox.Right + 4, boundingBox.Bottom + 4),
                new SKColor(0, 0, 0, 153), // ~60% opacity black
                DefaultFont,
                textAlign);

            // Main text — white
            _typography.DrawDynamicText(
                canvas,
                userText,
                boundingBox,
                SKColors.White,
                DefaultFont,
                textAlign);
        }

        // ── 3. Encode to JPEG and return ──────────────────────────────────────
        logger.LogInformation("[ImageCompositionService] 💾 Encoding final image to JPEG...");
        using var snapshot = surface.Snapshot();
        using var encoded  = snapshot.Encode(SKEncodedImageFormat.Jpeg, 92);
        return encoded.ToArray();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Maps the AI-provided <paramref name="textPlacement"/> value to a
    /// pixel-accurate bounding box on the 1080×1350 canvas.
    /// </summary>
    private static SKRect ResolveBoundingBox(string? textPlacement) =>
        (textPlacement?.ToLowerInvariant() ?? "topcenter") switch
        {
            "topcenter"   or "top"    => new SKRect( 100,   60,  980,  420),
            "topleft"                 => new SKRect(  50,   50,  800,  400),
            "topright"                => new SKRect( 280,   50, 1030,  400),
            "leftcenter"  or "left"   => new SKRect(  50,  400,  500,  950),
            "rightcenter" or "right"  => new SKRect( 580,  400, 1030,  950),
            "bottomcenter"or "bottom" => new SKRect( 100, 1000,  980, 1250),
            "bottomleft"              => new SKRect(  50, 1000,  700, 1250),
            "bottomright"             => new SKRect( 380, 1000, 1030, 1250),
            "split"                   => new SKRect( 100,   60,  980,  460), // top strip for split layouts
            _                         => new SKRect( 100,   60,  980,  420), // default → top
        };

    private static SKTextAlign ResolveTextAlign(string? textAlign) =>
        (textAlign?.ToLowerInvariant() ?? "center") switch
        {
            "left"  => SKTextAlign.Left,
            "right" => SKTextAlign.Right,
            _       => SKTextAlign.Center,
        };
}
