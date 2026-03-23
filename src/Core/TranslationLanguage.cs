namespace CommandToTranslate.Core;

/// <summary>
/// Immutable runtime representation of a supported language.
/// </summary>
public sealed record TranslationLanguage(string Code, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Immutable runtime representation of a translation direction.
/// </summary>
public sealed record TranslationPair(TranslationLanguage SourceLanguage, TranslationLanguage TargetLanguage)
{
    public string Id => $"{SourceLanguage.Code}:{TargetLanguage.Code}";
    public string Label => $"{SourceLanguage.Label} -> {TargetLanguage.Label}";
}

/// <summary>
/// Central catalog of supported languages and selection helpers.
/// </summary>
public static class TranslationLanguageCatalog
{
    public const string DefaultSourceLanguageCode = "pt-BR";
    public const string DefaultTargetLanguageCode = "en-US";

    private static readonly TranslationLanguage[] SupportedLanguageArray =
    [
        new("pt-BR", "Portuguese (Brazil)"),
        new("en-US", "English (US)"),
        new("es-ES", "Spanish"),
        new("fr-FR", "French"),
        new("de-DE", "German"),
        new("ja-JP", "Japanese"),
        new("zh-Hans", "Mandarin (Simplified)"),
        new("it-IT", "Italian")
    ];

    private static readonly Dictionary<string, TranslationLanguage> LanguageLookup =
        SupportedLanguageArray.ToDictionary(language => language.Code, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<TranslationLanguage> SupportedLanguages => SupportedLanguageArray;

    public static TranslationPair DefaultPair => new(
        Resolve(DefaultSourceLanguageCode),
        Resolve(DefaultTargetLanguageCode));

    public static bool TryResolve(string? languageCode, out TranslationLanguage language)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            language = null!;
            return false;
        }

        return LanguageLookup.TryGetValue(languageCode.Trim(), out language!);
    }

    public static TranslationLanguage Resolve(string languageCode)
    {
        if (!TryResolve(languageCode, out var language))
            throw new ArgumentException($"Unsupported language code '{languageCode}'.", nameof(languageCode));

        return language;
    }

    public static bool TryCreatePair(
        string? sourceLanguageCode,
        string? targetLanguageCode,
        out TranslationPair pair)
    {
        pair = DefaultPair;

        if (!TryResolve(sourceLanguageCode, out var sourceLanguage) ||
            !TryResolve(targetLanguageCode, out var targetLanguage) ||
            string.Equals(sourceLanguage.Code, targetLanguage.Code, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        pair = new TranslationPair(sourceLanguage, targetLanguage);
        return true;
    }
}
