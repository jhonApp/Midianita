using Midianita.Workers.ProcessadorArte.Models;

namespace Midianita.Workers.ProcessadorArte.Services;

/// <summary>
/// Renders the final composed banner using the deterministic layout
/// extracted by the AI (<see cref="LayoutRulesV2"/>).
/// </summary>
public interface ISkiaRendererService
{
    /// <summary>
    /// Composes background + person cutout + text overlays onto
    /// a single SkiaSharp canvas and returns the encoded JPEG bytes.
    /// </summary>
    byte[] RenderFinalBanner(
        LayoutRulesV2 layout,
        byte[] backgroundBytes,
        byte[] personBytes,
        int canvasWidth = 1080,
        int canvasHeight = 1350);
}
