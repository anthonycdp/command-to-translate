using CommandToTranslate.Core;
using CommandToTranslate.Services;
using Xunit;

namespace CommandToTranslate.Tests;

public class TranslationServiceTests
{
    [Fact]
    public void CreateChatRequest_UsesActiveTranslationPairInSystemPrompt()
    {
        var request = TranslationService.CreateChatRequest(
            model: "translategemma",
            stream: false,
            keepAlive: "5m",
            temperature: 0.0,
            translationPair: new TranslationPair(
                new TranslationLanguage("en-US", "English (US)"),
                new TranslationLanguage("pt-BR", "Portuguese (Brazil)")),
            text: "good morning");

        Assert.Equal("system", request.Messages[0].Role);
        Assert.Contains("from en-US to pt-BR", request.Messages[0].Content, StringComparison.Ordinal);
        Assert.Contains("If the text is already in pt-BR", request.Messages[0].Content, StringComparison.Ordinal);
        Assert.Equal("good morning", request.Messages[1].Content);
    }

    [Fact]
    public void BuildSystemPrompt_PreservesTargetLanguageSpecificInstructions()
    {
        var prompt = TranslationService.BuildSystemPrompt(
            new TranslationPair(
                new TranslationLanguage("pt-BR", "Portuguese (Brazil)"),
                new TranslationLanguage("es-ES", "Spanish")));

        Assert.Contains("from pt-BR to es-ES", prompt, StringComparison.Ordinal);
        Assert.Contains("already in es-ES", prompt, StringComparison.Ordinal);
        Assert.Contains("target language", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Preserve the original structure exactly", prompt, StringComparison.Ordinal);
        Assert.Contains("[[CTT_LINE_BREAK]]", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void PrepareTextForTranslation_ReplacesLineBreaksWithProtectedTokens()
    {
        var preparedText = TranslationService.PrepareTextForTranslation(
            "MISSION\r\n\r\n- First item\r\n- Second item");

        Assert.Equal(
            "MISSION[[CTT_LINE_BREAK]][[CTT_LINE_BREAK]]- First item[[CTT_LINE_BREAK]]- Second item",
            preparedText);
    }

    [Fact]
    public void FinalizeTranslatedText_RestoresProtectedLineBreaks_UsingSourceLineEndings()
    {
        const string sourceText = "MISSION\r\n\r\n- First item\r\n- Second item";
        const string translatedText =
            "MISSAO[[CTT_LINE_BREAK]][[CTT_LINE_BREAK]]- Primeiro item[[CTT_LINE_BREAK]]- Segundo item";

        var finalizedText = TranslationService.FinalizeTranslatedText(sourceText, translatedText);

        Assert.Equal(
            "MISSAO\r\n\r\n- Primeiro item\r\n- Segundo item",
            finalizedText);
    }

    [Fact]
    public void FinalizeTranslatedText_RepairsCollapsedSentenceSpacing()
    {
        const string sourceText = "Architecture. Work within the project.";
        const string translatedText = "Arquitetura.Trabalhe dentro do projeto.";

        var finalizedText = TranslationService.FinalizeTranslatedText(sourceText, translatedText);

        Assert.Equal("Arquitetura. Trabalhe dentro do projeto.", finalizedText);
    }

    [Fact]
    public void FinalizeTranslatedText_DoesNotAlterValidDomainSpacing()
    {
        const string sourceText = "Visit example.com today.";
        const string translatedText = "Visite example.com hoje.";

        var finalizedText = TranslationService.FinalizeTranslatedText(sourceText, translatedText);

        Assert.Equal("Visite example.com hoje.", finalizedText);
    }
}
