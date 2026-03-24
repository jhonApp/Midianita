using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Midianita.Workers.AnalisadorBanner.Models;

public record LayoutRulesV2(
    [property: JsonPropertyName("masterPrompt")] string MasterPrompt,
    [property: JsonPropertyName("background")]  BackgroundLayout Background,
    [property: JsonPropertyName("pessoa")]      PessoaLayout     Pessoa,
    [property: JsonPropertyName("textos")]      List<TextElement> Textos
);

// ─────────────────────────────────────────────────────────────────
//  BACKGROUND
// ─────────────────────────────────────────────────────────────────
public record BackgroundLayout(
    /// <summary>Dominant hex colors, e.g. ["#1A1A2E", "#E94560"]</summary>
    [property: JsonPropertyName("coresDominantes")]    List<string> CoresDominantes,

    /// <summary>Textural / abstract style descriptors, e.g. ["gradiente radial", "partículas"]</summary>
    [property: JsonPropertyName("elementosVisuais")]   List<string> ElementosVisuais
);

// ─────────────────────────────────────────────────────────────────
//  PESSOA (CUTOUT)
// ─────────────────────────────────────────────────────────────────
public record PessoaLayout(
    /// <summary>Anchor position. One of: bottom-center, bottom-right, bottom-left, center-right, center-left.</summary>
    [property: JsonPropertyName("anchor")]     string       Anchor,

    /// <summary>Scale relative to canvas height, range 0.1–1.0.</summary>
    [property: JsonPropertyName("scale")]      float        Scale,

    /// <summary>Vertical pixel offset from anchor (positive = down).</summary>
    [property: JsonPropertyName("offsetY")]    int          OffsetY,

    /// <summary>Post-processing filters to apply, e.g. ["grayscale", "high_contrast"].</summary>
    [property: JsonPropertyName("filters")]    List<string> Filters
);

// ─────────────────────────────────────────────────────────────────
//  TEXTOS (per-element)
// ─────────────────────────────────────────────────────────────────
public record TextElement(
    /// <summary>Semantic role. One of: titulo, info, data.</summary>
    [property: JsonPropertyName("tipo")]       string Tipo,

    /// <summary>Y position in pixels from the top of the canvas (1080px reference height).</summary>
    [property: JsonPropertyName("yPosition")] int    YPosition,

    /// <summary>Font size in pixels (1080px reference height).</summary>
    [property: JsonPropertyName("fontSize")]  int    FontSize,

    /// <summary>Text color in hexadecimal, e.g. "#FFFFFF".</summary>
    [property: JsonPropertyName("color")]     string Color,

    /// <summary>Font weight: "regular", "medium", "semibold", "bold", "extrabold".</summary>
    [property: JsonPropertyName("fontWeight")] string FontWeight,

    /// <summary>Text alignment: "left", "center", "right".</summary>
    [property: JsonPropertyName("alignment")] string Alignment
);
