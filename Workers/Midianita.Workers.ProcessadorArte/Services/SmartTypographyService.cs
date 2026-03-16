using SkiaSharp;

namespace Midianita.Workers.ProcessadorArte.Services;

/// <summary>
/// Responsive typography engine that auto-fits multi-line text into a bounding box.
/// Starts at a large font size and decrements until every wrapped line fits
/// both horizontally and vertically within the target rectangle.
/// </summary>
public sealed class SmartTypographyService : ISmartTypographyService
{
    private const float MaxFontSize = 300f;
    private const float MinFontSize = 12f;
    private const float FontSizeStep = 1f;

    /// <inheritdoc />
    public void DrawDynamicText(
        SKCanvas    canvas,
        string      text,
        SKRect      boundingBox,
        SKColor     textColor,
        string      fontName,
        SKTextAlign textAlign = SKTextAlign.Left)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        using var typeface = SKTypeface.FromFamilyName(fontName)
                          ?? SKTypeface.Default;

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color       = textColor,
            TextAlign   = textAlign,
        };

        // ── 1. Binary-search for the largest font size that fits ──────────────
        var fittedLines  = Array.Empty<string>();
        var fittedSize   = MinFontSize;
        var currentSize  = MaxFontSize;

        while (currentSize >= MinFontSize)
        {
            paint.TextSize = currentSize;
            paint.Typeface = typeface;

            var lines      = WrapWords(text, paint, boundingBox.Width);
            var lineHeight = paint.FontMetrics.Descent - paint.FontMetrics.Ascent;
            var totalH     = lineHeight * lines.Count;
            var maxW       = lines.Max(l => paint.MeasureText(l));

            if (maxW <= boundingBox.Width && totalH <= boundingBox.Height)
            {
                fittedLines = lines.ToArray();
                fittedSize  = currentSize;
                break;
            }

            currentSize -= FontSizeStep;
        }

        // Fallback: if nothing fit, force the minimum size
        if (fittedLines.Length == 0)
        {
            paint.TextSize = MinFontSize;
            paint.Typeface = typeface;
            fittedLines = WrapWords(text, paint, boundingBox.Width).ToArray();
            fittedSize  = MinFontSize;
        }

        // ── 2. Draw each line ─────────────────────────────────────────────────
        paint.TextSize = fittedSize;
        paint.Typeface = typeface;

        var metrics    = paint.FontMetrics;
        var lineHeight2 = metrics.Descent - metrics.Ascent;

        // Baseline of the first line: top of bounding box + ascent offset
        var y = boundingBox.Top - metrics.Ascent; // metrics.Ascent is negative

        foreach (var line in fittedLines)
        {
            float x = textAlign switch
            {
                SKTextAlign.Center => boundingBox.MidX,
                SKTextAlign.Right  => boundingBox.Right,
                _                  => boundingBox.Left,
            };

            canvas.DrawText(line, x, y, paint);
            y += lineHeight2;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Splits <paramref name="text"/> into lines so that no line exceeds
    /// <paramref name="maxWidth"/> when measured with <paramref name="paint"/>.
    /// </summary>
    private static List<string> WrapWords(string text, SKPaint paint, float maxWidth)
    {
        var words  = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines  = new List<string>();
        var current = string.Empty;

        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : current + " " + word;

            if (paint.MeasureText(candidate) <= maxWidth)
            {
                current = candidate;
            }
            else
            {
                if (current.Length > 0)
                    lines.Add(current);

                // If a single word is wider than the box, add it anyway to avoid infinite loop
                current = word;
            }
        }

        if (current.Length > 0)
            lines.Add(current);

        return lines;
    }
}
