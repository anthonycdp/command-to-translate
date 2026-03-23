using CommandToTranslate.Core;
using Xunit;

namespace CommandToTranslate.Tests;

public class TranslationConfigTests
{
    [Fact]
    public void Normalize_UsesDefaultPair_WhenSelectionIsInvalid()
    {
        var config = new AppConfig
        {
            Translation = new TranslationConfig
            {
                SourceLanguage = "ja-JP",
                TargetLanguage = "ja-JP"
            }
        };

        config.Normalize();

        var activePair = config.Translation.GetActivePair();

        Assert.Equal("pt-BR", config.Translation.SourceLanguage);
        Assert.Equal("en-US", config.Translation.TargetLanguage);
        Assert.Equal("pt-BR", activePair.SourceLanguage.Code);
        Assert.Equal("en-US", activePair.TargetLanguage.Code);
    }

    [Fact]
    public void Normalize_MigratesLegacyActivePair_WhenNewSelectionIsMissing()
    {
        var config = new AppConfig
        {
            Translation = new TranslationConfig
            {
                SourceLanguage = string.Empty,
                TargetLanguage = string.Empty,
                ActivePair = "fr-FR:de-DE"
            }
        };

        config.Normalize();

        Assert.Equal("fr-FR", config.Translation.SourceLanguage);
        Assert.Equal("de-DE", config.Translation.TargetLanguage);
    }

    [Fact]
    public void TrySetActiveTranslationPair_UpdatesStateAndConfig()
    {
        var state = new AppState
        {
            Config = new AppConfig()
        };

        var changed = state.TrySetActiveTranslationPair("en-US", "pt-BR", out var activePair);

        Assert.True(changed);
        Assert.Equal("en-US", state.Config.Translation.SourceLanguage);
        Assert.Equal("pt-BR", state.Config.Translation.TargetLanguage);
        Assert.Equal("en-US:pt-BR", state.ActiveTranslationPair.Id);
        Assert.Equal("English (US)", activePair.SourceLanguage.Label);
        Assert.Equal("Portuguese (Brazil)", activePair.TargetLanguage.Label);
    }

    [Fact]
    public void TrySetActiveTranslationPair_RejectsSameLanguage_AndKeepsCurrentSelection()
    {
        var state = new AppState
        {
            Config = new AppConfig()
        };

        var changed = state.TrySetActiveTranslationPair("ja-JP", "ja-JP", out var activePair);

        Assert.False(changed);
        Assert.Equal("pt-BR:en-US", state.ActiveTranslationPair.Id);
        Assert.Equal(state.ActiveTranslationPair, activePair);
    }
}
