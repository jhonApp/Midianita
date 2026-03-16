using SkiaSharp;

namespace Midianita.Workers.ProcessadorArte.Services;

/// <summary>
/// Draws dynamic, auto-fitting text into a bounding box on an SKCanvas.
/// The engine automatically calculates word wraps and scales down the font
/// size until the entire text block fits within the specified rectangle.
/// </summary>
public interface ISmartTypographyService
{
    /// <summary>
    /// Draws <paramref name="text"/> into <paramref name="boundingBox"/> on the given canvas,
    /// auto-wrapping words and shrinking the font until the whole block fits.
    /// </summary>
    /// <param name="canvas">The SkiaSharp canvas to draw on.</param>
    /// <param name="text">The user-provided text to render.</param>
    /// <param name="boundingBox">The rectangle that must contain all rendered text.</param>
    /// <param name="textColor">Fill color for the glyphs.</param>
    /// <param name="fontName">Typeface family name (e.g., "Arial", "Inter").</param>
    /// <param name="textAlign">Horizontal alignment within the bounding box.</param>
    void DrawDynamicText(
        SKCanvas      canvas,
        string        text,
        SKRect        boundingBox,
        SKColor       textColor,
        string        fontName,
        SKTextAlign   textAlign = SKTextAlign.Left);
}
