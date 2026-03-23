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
    }
}
