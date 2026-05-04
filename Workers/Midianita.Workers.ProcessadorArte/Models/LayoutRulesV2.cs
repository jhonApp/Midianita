using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Midianita.Workers.ProcessadorArte.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  LayoutRulesV2 — Contrato semântico para geração de banner via OpenAI GPT Image
//
//  Esta estrutura NÃO contém valores numéricos específicos para um motor gráfico
//  (como YPosition, FontSize px, ShadowAlpha). Em vez disso, usa linguagem natural
//  que é traduzida diretamente para o prompt enviado à API da OpenAI.
//
//  [FUTURO] Quando a API gpt-image-1 lançar suporte nativo a bounding_boxes para
//  posicionamento preciso de texto e elementos, adicionar os campos abaixo em
//  TextoInstrucao e PessoaInstrucoes:
//    - BoundingBox? BoundingBox  →  record BoundingBox(float X, float Y, float Width, float Height)
//  Isso permitirá coordenadas percentuais exatas (0.0–1.0) no payload da API.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Regras de layout para composição de banner via OpenAI GPT Image 2.0.
/// Todos os campos são descritivos em linguagem natural para montagem do prompt.
/// </summary>
public record LayoutRulesV2(
    /// <summary>
    /// Prompt base que descreve o cenário/background do banner.
    /// Ex: "cenário urbano noturno com luzes neon azuis e roxas, estilo cinematográfico".
    /// </summary>
    [property: JsonPropertyName("masterPrompt")] string MasterPrompt,

    /// <summary>
    /// Tamanho do canvas de saída. Padrão: 1080x1350 (proporção 4:5, ideal para Instagram).
    /// </summary>
    [property: JsonPropertyName("canvasSize")] CanvasSize CanvasSize,

    /// <summary>
    /// Instruções sobre como posicionar e integrar a foto da pessoa ao cenário.
    /// Null caso o banner não possua recorte de pessoa.
    /// </summary>
    [property: JsonPropertyName("pessoa")] PessoaInstrucoes? Pessoa,

    /// <summary>
    /// Lista de textos que devem ser renderizados no banner, com posição e estilo descritivos.
    /// </summary>
    [property: JsonPropertyName("textos")] List<TextoInstrucao> Textos,

    /// <summary>
    /// Descrição geral do estilo visual do banner.
    /// Ex: "poster cinematográfico, iluminação dramática lateral, tons quentes".
    /// </summary>
    [property: JsonPropertyName("estiloGeral")] string EstiloGeral
);

// ─────────────────────────────────────────────────────────────────────────────
//  CANVAS SIZE
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Dimensões do banner de saída, em pixels.
/// A API gpt-image-1 aceita: "1024x1024", "1024x1536", "1536x1024".
/// Use "1024x1536" para banners verticais (estilo Instagram/stories).
/// </summary>
public record CanvasSize(
    [property: JsonPropertyName("width")]  int Width,
    [property: JsonPropertyName("height")] int Height
)
{
    /// <summary>Retorna o tamanho no formato aceito pela API da OpenAI (ex: "1024x1536").</summary>
    public string ToApiFormat() => $"{Width}x{Height}";
}

// ─────────────────────────────────────────────────────────────────────────────
//  PESSOA / CUTOUT
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Instruções de integração da foto da pessoa ao cenário gerado pela IA.
/// O modelo recebe estas descrições como parte do prompt e é responsável por
/// todo o posicionamento, iluminação e composição — sem nenhum processamento local.
/// </summary>
public record PessoaInstrucoes(
    /// <summary>
    /// Âncora de posicionamento semântica.
    /// Valores suportados: "bottom-center", "bottom-right", "bottom-left",
    /// "center-right", "center-left", "center".
    /// </summary>
    [property: JsonPropertyName("anchor")] string Anchor,

    /// <summary>
    /// Descrição em linguagem natural do tamanho relativo da pessoa no frame.
    /// Ex: "ocupa aproximadamente 75% da altura do banner",
    ///     "meio corpo, da cintura para cima".
    /// </summary>
    [property: JsonPropertyName("sizeDescription")] string SizeDescription,

    /// <summary>
    /// Notas livres de integração e estilo para o modelo de IA.
    /// Ex: "iluminação combinada com o cenário noturno",
    ///     "integre suavemente sem borda visível",
    ///     "aplique tom sépia leve".
    /// Null se não houver instruções especiais.
    /// </summary>
    [property: JsonPropertyName("integrationNotes")] string? IntegrationNotes

    // [FUTURO] Quando a API suportar bounding_box:
    // [property: JsonPropertyName("boundingBox")] BoundingBox? BoundingBox
);

// ─────────────────────────────────────────────────────────────────────────────
//  TEXTOS (por elemento)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Instrução de um elemento de texto a ser renderizado pela IA no banner.
/// Todos os campos são descritivos — o modelo interpreta e posiciona o texto
/// com precisão tipográfica e composicional.
/// </summary>
public record TextoInstrucao(
    /// <summary>
    /// O conteúdo textual exato a ser renderizado no banner.
    /// Ex: "Workshop de Liderança", "15 de Junho, 2025", "vagas limitadas".
    /// </summary>
    [property: JsonPropertyName("conteudo")] string Conteudo,

    /// <summary>
    /// Posição semântica no banner.
    /// Valores sugeridos: "topo", "topo-esquerda", "centro", "rodapé",
    /// "lateral-esquerda", "lateral-direita", "sobre-a-pessoa".
    /// </summary>
    [property: JsonPropertyName("posicao")] string Posicao,

    /// <summary>
    /// Descrição do estilo tipográfico em linguagem natural.
    /// Ex: "título principal, fonte grande, bold, cor branca, sombra escura suave",
    ///     "subtítulo, médio, regular, cor dourada",
    ///     "rodapé pequeno, regular, cor cinza claro, alinhado ao centro".
    /// </summary>
    [property: JsonPropertyName("estilo")] string Estilo

    // [FUTURO] Quando a API suportar bounding_box para texto:
    // [property: JsonPropertyName("boundingBox")] BoundingBox? BoundingBox
);
