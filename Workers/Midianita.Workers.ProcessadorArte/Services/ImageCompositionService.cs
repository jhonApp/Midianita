using Amazon.Lambda.Core;
using Midianita.Workers.ProcessadorArte.Models;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.IO;

namespace Midianita.Workers.ProcessadorArte.Services;

public sealed class ImageCompositionService : IImageCompositionService
{
    private readonly FontCollection _fonts;
    private readonly FontFamily _mainFont;

    public ImageCompositionService()
    {
        _fonts = new FontCollection();
        try
        {
            _mainFont = SystemFonts.Get("Arial");
        }
        catch
        {
            _mainFont = SystemFonts.Families.FirstOrDefault();
        }
    }

    public async Task<byte[]> ApplyTypographyAsync(
        byte[] aiGeneratedImageBytes, BannerAnalysisResult bannerMetadata, string userText, ILambdaLogger logger)
    {
        using var bgImage = await Image.LoadAsync<Rgba32>(new MemoryStream(aiGeneratedImageBytes));
        DrawUserTypography(bgImage, userText, bannerMetadata.LayoutRules, logger);
        
        logger.LogInformation("[ImageCompositionService] 💾 Saving final typography mapped image stream...");
        using var outStream = new MemoryStream();
        await bgImage.SaveAsJpegAsync(outStream);
        return outStream.ToArray();
    }

    private void DrawUserTypography(Image<Rgba32> bgImage, string text, LayoutRules rules, ILambdaLogger logger)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        logger.LogInformation($"[ImageCompositionService] ✍️ Writing user text: '{text}'");

        var fontSize = bgImage.Width * 0.08f; 
        var font = _mainFont.CreateFont(fontSize, FontStyle.Bold);

        var textOptions = new RichTextOptions(font)
        {
            Origin = new PointF(0, 0),
            WrappingLength = bgImage.Width * 0.8f,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var alignment = rules.TextAlign?.ToLowerInvariant();
        if (alignment == "left") textOptions.HorizontalAlignment = HorizontalAlignment.Left;
        if (alignment == "right") textOptions.HorizontalAlignment = HorizontalAlignment.Right;

        var txtPlacement = rules.TextPlacement?.ToLowerInvariant() ?? "topcenter";
        var measuredSize = TextMeasurer.MeasureSize(text, textOptions);
        
        var txtX = (bgImage.Width) / 2f; 
        if (textOptions.HorizontalAlignment == HorizontalAlignment.Left) txtX = bgImage.Width * 0.1f;
        if (textOptions.HorizontalAlignment == HorizontalAlignment.Right) txtX = bgImage.Width * 0.9f;

        var txtY = bgImage.Height * 0.1f; 
        if (txtPlacement.Contains("bottom"))
            txtY = bgImage.Height - measuredSize.Height - (bgImage.Height * 0.1f);

        textOptions.Origin = new PointF(txtX, txtY);

        var shadowOptions = new RichTextOptions(font)
        {
            Origin = new PointF(txtX + 4, txtY + 4),
            WrappingLength = textOptions.WrappingLength,
            HorizontalAlignment = textOptions.HorizontalAlignment
        };
        bgImage.Mutate(x => x.DrawText(shadowOptions, text, Color.Black.WithAlpha(0.6f)));
        bgImage.Mutate(x => x.DrawText(textOptions, text, Color.White));
    }
}
