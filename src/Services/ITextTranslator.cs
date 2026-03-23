namespace CommandToTranslate.Services;

public interface ITextTranslator
{
    Task<string?> TranslateAsync(string text, CancellationToken ct);
    Task<(bool IsHealthy, string? ErrorMessage)> CheckHealthAsync();
}
