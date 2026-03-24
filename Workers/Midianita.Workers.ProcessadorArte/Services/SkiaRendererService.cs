using Midianita.Workers.ProcessadorArte.Models;
using SkiaSharp;

namespace Midianita.Workers.ProcessadorArte.Services;

/// <summary>
/// Composes a final banner by layering background, person cutout, and text
/// elements using deterministic coordinates from <see cref="LayoutRulesV2"/>.
/// </summary>
public sealed class SkiaRendererService : ISkiaRendererService
{
    private const string DefaultFontFamily = "Arial";
    private const int    ShadowOffset      = 3;
    private const byte   ShadowAlpha       = 153; // ~60% opacity

    public byte[] RenderFinalBanner(
        LayoutRulesV2 layout,
        byte[] backgroundBytes,
        byte[] personBytes,
        int canvasWidth  = 1080,
        int canvasHeight = 1350)
    {
        using var surface = SKSurface.Create(new SKImageInfo(canvasWidth, canvasHeight));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);

        // ── Pass 1: Background ─────────────────────────────────────────────
        DrawBackground(canvas, backgroundBytes, canvasWidth, canvasHeight);

        // ── Pass 2: Person Cutout ──────────────────────────────────────────
        if (personBytes is { Length: > 0 } && layout.Pessoa is not null)
        {
            DrawPerson(canvas, personBytes, layout.Pessoa, canvasWidth, canvasHeight);
        }

        // ── Pass 3: Text Overlays ──────────────────────────────────────────
        if (layout.Textos is { Count: > 0 })
        {
            DrawTexts(canvas, layout.Textos, canvasWidth);
        }

        // ── Encode to JPEG ─────────────────────────────────────────────────
        using var snapshot = surface.Snapshot();
        using var encoded  = snapshot.Encode(SKEncodedImageFormat.Jpeg, 92);
        return encoded.ToArray();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  PASS 1 — BACKGROUND
    // ═══════════════════════════════════════════════════════════════════════

    private static void DrawBackground(SKCanvas canvas, byte[] bgBytes, int w, int h)
    {
        if (bgBytes is not { Length: > 0 }) return;

        using var data  = SKData.CreateCopy(bgBytes);
        using var image = SKImage.FromEncodedData(data);
        if (image is null) return;

        using var paint = new SKPaint { FilterQuality = SKFilterQuality.High };
        canvas.DrawImage(image, new SKRect(0, 0, w, h), paint);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  PASS 2 — PERSON CUTOUT (scale, filters, anchor)
    // ═══════════════════════════════════════════════════════════════════════

    private static void DrawPerson(
        SKCanvas canvas, byte[] personBytes, PessoaLayout pessoa, int canvasW, int canvasH)
    {
        using var data  = SKData.CreateCopy(personBytes);
        using var image = SKImage.FromEncodedData(data);
        if (image is null) return;

        // ── Scale ──────────────────────────────────────────────────────────
        float scale       = Math.Clamp(pessoa.Scale, 0.1f, 1.0f);
        float targetH     = canvasH * scale;
        float aspectRatio = (float)image.Width / image.Height;
        float targetW     = targetH * aspectRatio;

        // ── Anchor ─────────────────────────────────────────────────────────
        var (x, y) = ResolveAnchor(pessoa.Anchor, canvasW, canvasH, targetW, targetH);
        y += pessoa.OffsetY;

        var destRect = new SKRect(x, y, x + targetW, y + targetH);

        // ── Filters ────────────────────────────────────────────────────────
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.High };
        ApplyFilters(paint, pessoa.Filters);

        // ── Draw ───────────────────────────────────────────────────────────
        canvas.DrawImage(image, destRect, paint);
    }

    private static (float X, float Y) ResolveAnchor(
        string? anchor, int canvasW, int canvasH, float imgW, float imgH)
    {
        return (anchor?.ToLowerInvariant() ?? "bottom-center") switch
        {
            "bottom-center" => ((canvasW - imgW) / 2f,      canvasH - imgH),
            "bottom-right"  => (canvasW - imgW,              canvasH - imgH),
            "bottom-left"   => (0f,                          canvasH - imgH),
            "center-right"  => (canvasW - imgW,              (canvasH - imgH) / 2f),
            "center-left"   => (0f,                          (canvasH - imgH) / 2f),
            _               => ((canvasW - imgW) / 2f,      canvasH - imgH), // default → bottom-center
        };
    }

    private static void ApplyFilters(SKPaint paint, List<string>? filters)
    {
        if (filters is null || filters.Count == 0) return;

        SKColorFilter? combined = null;

        foreach (var filter in filters)
        {
            var lower = filter.ToLowerInvariant().Trim();
            SKColorFilter? current = null;

            // ── Grayscale ──────────────────────────────────────────────────
            if (lower.Contains("grayscale"))
            {
                current = SKColorFilter.CreateColorMatrix(new float[]
                {
                    0.2126f, 0.7152f, 0.0722f, 0, 0,
                    0.2126f, 0.7152f, 0.0722f, 0, 0,
                    0.2126f, 0.7152f, 0.0722f, 0, 0,
                    0,       0,       0,       1, 0
                });
            }
            // ── Contrast ───────────────────────────────────────────────────
            else if (lower.Contains("contrast"))
            {
                float c = ExtractNumericValue(lower, 1.15f);
                float t = (1f - c) / 2f;

                current = SKColorFilter.CreateColorMatrix(new float[]
                {
                    c, 0, 0, 0, t,
                    0, c, 0, 0, t,
                    0, 0, c, 0, t,
                    0, 0, 0, 1, 0
                });
            }

            if (current is not null)
            {
                combined = combined is null
                    ? current
                    : SKColorFilter.CreateCompose(current, combined);
            }
        }

        if (combined is not null)
        {
            paint.ColorFilter = combined;
        }
    }

    /// <summary>
    /// Extracts a numeric value from a CSS-style filter string, e.g. "contrast(1.15)" → 1.15f.
    /// </summary>
    private static float ExtractNumericValue(string filterString, float fallback)
    {
        var start = filterString.IndexOf('(');
        var end   = filterString.IndexOf(')');

        if (start >= 0 && end > start)
        {
            var raw = filterString[(start + 1)..end]
                        .Replace("%", "")
                        .Trim();

            if (float.TryParse(raw, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out var v))
                return v;
        }
        return fallback;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  PASS 3 — TEXT OVERLAYS
    // ═══════════════════════════════════════════════════════════════════════

    private static void DrawTexts(SKCanvas canvas, List<TextElement> textos, int canvasWidth)
    {
        foreach (var texto in textos)
        {
            if (texto is null) continue;

            var color     = ParseHexColor(texto.Color);
            var textAlign = ResolveTextAlign(texto.Alignment);
            var weight    = ResolveFontWeight(texto.FontWeight);

            using var typeface = SKTypeface.FromFamilyName(DefaultFontFamily, weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                              ?? SKTypeface.Default;

            // ── Drop shadow ────────────────────────────────────────────────
            using var shadowPaint = new SKPaint
            {
                IsAntialias = true,
                Color       = new SKColor(0, 0, 0, ShadowAlpha),
                TextSize    = texto.FontSize,
                TextAlign   = textAlign,
                Typeface    = typeface
            };

            float shadowX = ResolveTextX(textAlign, canvasWidth) + ShadowOffset;
            canvas.DrawText(texto.Tipo ?? string.Empty, shadowX, texto.YPosition + ShadowOffset, shadowPaint);

            // ── Main text ──────────────────────────────────────────────────
            using var mainPaint = new SKPaint
            {
                IsAntialias = true,
                Color       = color,
                TextSize    = texto.FontSize,
                TextAlign   = textAlign,
                Typeface    = typeface
            };

            float mainX = ResolveTextX(textAlign, canvasWidth);
            canvas.DrawText(texto.Tipo ?? string.Empty, mainX, texto.YPosition, mainPaint);
        }
    }

    // ── Text Helpers ───────────────────────────────────────────────────────

    private static float ResolveTextX(SKTextAlign align, int canvasWidth)
    {
        return align switch
        {
            SKTextAlign.Center => canvasWidth / 2f,
            SKTextAlign.Right  => canvasWidth - 40f,  // 40px right margin
            _                  => 40f                  // 40px left margin
        };
    }

    private static SKTextAlign ResolveTextAlign(string? alignment)
    {
        return (alignment?.ToLowerInvariant() ?? "center") switch
        {
            "left"  => SKTextAlign.Left,
            "right" => SKTextAlign.Right,
            _       => SKTextAlign.Center,
        };
    }

    private static SKFontStyleWeight ResolveFontWeight(string? fontWeight)
    {
        return (fontWeight?.ToLowerInvariant() ?? "regular") switch
        {
            "regular"   => SKFontStyleWeight.Normal,
            "medium"    => SKFontStyleWeight.Medium,
            "semibold"  => SKFontStyleWeight.SemiBold,
            "bold"      => SKFontStyleWeight.Bold,
            "extrabold" => SKFontStyleWeight.ExtraBold,
            _           => SKFontStyleWeight.Normal,
        };
    }

    private static SKColor ParseHexColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return SKColors.White;

        try
        {
            return SKColor.Parse(hex);
        }
        catch
        {
            return SKColors.White;
        }
    }
}
