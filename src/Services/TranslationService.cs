// src/Services/TranslationService.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommandToTranslate.Core;

namespace CommandToTranslate.Services;

/// <summary>
/// Service for communicating with Ollama API for translation.
/// Builds requests dynamically from the active translation pair.
/// </summary>
public class TranslationService : IDisposable, ITextTranslator
{
    private const int MinimumRecommendedTimeoutMs = 8000;

    private readonly AppState _state;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _endpoint;
    private readonly string _keepAlive;
    private bool _disposed;

    // Rate limiting for error notifications
    private DateTime _lastNotificationTime = DateTime.MinValue;
    private readonly TimeSpan _notificationCooldown = TimeSpan.FromSeconds(30);

    public TranslationService(AppState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _endpoint = NormalizeEndpoint(_state.Config.Ollama.Endpoint);
        _keepAlive = _state.Config.Ollama.KeepAlive;
        var timeoutMs = Math.Max(_state.Config.Ollama.TimeoutMs, MinimumRecommendedTimeoutMs);

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMilliseconds(timeoutMs)
        };

        if (_state.Config.Ollama.TimeoutMs < MinimumRecommendedTimeoutMs)
        {
            Logger.Warning(
                $"Configured Ollama timeout {_state.Config.Ollama.TimeoutMs}ms is too low for chat responses; using {timeoutMs}ms.");
        }

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    /// <summary>
    /// Translates text using the currently selected translation pair.
    /// </summary>
    /// <param name="text">The text to translate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The translated text, or null if translation failed or timed out.</returns>
    public async Task<string?> TranslateAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        if (!_state.OllamaAvailable)
            return null;

        var model = _state.Config.Ollama.Model;
        var temperature = _state.Config.Ollama.Temperature;
        var stream = _state.Config.Ollama.Stream;

        var requestBody = CreateChatRequest(
            model,
            stream,
            _keepAlive,
            temperature,
            _state.ActiveTranslationPair,
            text);

        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"{_endpoint}/api/chat",
                requestBody,
                _jsonOptions,
                ct);

            if (!response.IsSuccessStatusCode)
            {
                await HandleErrorAsync(response, "translate");
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(_jsonOptions, ct);

            if (result?.Message?.Content is null || string.IsNullOrWhiteSpace(result.Message.Content))
            {
                // Empty response - retry once
                return await RetryTranslationOnceAsync(text, ct);
            }

            // Success - clear any error notifications
            _state.ClearErrorNotification();
            _state.OllamaAvailable = true;

            return result.Message.Content.Trim();
        }
        catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException)
        {
            // Connection refused
            HandleConnectionRefused();
            return null;
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("Connection refused"))
        {
            // Connection refused (alternative detection)
            HandleConnectionRefused();
            return null;
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            // User cancelled - not an error
            return null;
        }
        catch (TaskCanceledException)
        {
            // Timeout
            HandleTimeout();
            return null;
        }
        catch (Exception)
        {
            // Unexpected error
            return null;
        }
    }

    private async Task<string?> RetryTranslationOnceAsync(string text, CancellationToken ct)
    {
        // Single retry for empty response
        try
        {
            var model = _state.Config.Ollama.Model;

            var requestBody = CreateChatRequest(
                model,
                stream: false,
                _keepAlive,
                _state.Config.Ollama.Temperature,
                _state.ActiveTranslationPair,
                text);

            var response = await _httpClient.PostAsJsonAsync(
                $"{_endpoint}/api/chat",
                requestBody,
                _jsonOptions,
                ct);

            if (!response.IsSuccessStatusCode)
                return null;

            var result = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(_jsonOptions, ct);
            return result?.Message?.Content?.Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Checks if Ollama is running and the configured model exists.
    /// </summary>
    /// <returns>True if Ollama is healthy and model is available.</returns>
    public async Task<(bool IsHealthy, string? ErrorMessage)> CheckHealthAsync()
    {
        var model = _state.Config.Ollama.Model;

        try
        {
            var response = await _httpClient.GetAsync($"{_endpoint}/api/tags");

            if (!response.IsSuccessStatusCode)
            {
                return (false, $"Ollama returned status {response.StatusCode}");
            }

            var result = await response.Content.ReadFromJsonAsync<OllamaTagsResponse>(_jsonOptions);

            if (result?.Models is null || result.Models.Count == 0)
            {
                return (false, "No models installed in Ollama");
            }

            var modelExists = result.Models.Any(m =>
                m.Name?.Equals(model, StringComparison.OrdinalIgnoreCase) == true ||
                m.Name?.StartsWith($"{model}:", StringComparison.OrdinalIgnoreCase) == true);

            if (!modelExists)
            {
                // Model not found - this is a fatal error
                return (false, $"Model '{model}' not found. Please pull it with: ollama pull {model}");
            }

            // Success
            _state.ClearErrorNotification();
            _state.OllamaAvailable = true;

            return (true, null);
        }
        catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException)
        {
            HandleConnectionRefused();
            return (false, "Ollama is not running. Please start Ollama first.");
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("Connection refused"))
        {
            HandleConnectionRefused();
            return (false, "Ollama is not running. Please start Ollama first.");
        }
        catch (TaskCanceledException)
        {
            HandleTimeout();
            return (false, "Ollama health check timed out.");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to check Ollama health: {ex.Message}");
        }
    }

    private async Task HandleErrorAsync(HttpResponseMessage response, string operation)
    {
        var statusCode = response.StatusCode;

        if (statusCode == HttpStatusCode.NotFound)
        {
            // Model not found - fatal error
            TryNotifyError($"Model '{_state.Config.Ollama.Model}' not found. Please pull it with: ollama pull {_state.Config.Ollama.Model}");
        }
        else if (statusCode == HttpStatusCode.InternalServerError ||
                 statusCode == HttpStatusCode.ServiceUnavailable)
        {
            // Server error - try to recover
            TryNotifyError($"Ollama returned error {statusCode} during {operation}.");
        }
    }

    private void HandleConnectionRefused()
    {
        _state.OllamaAvailable = false;
        TryNotifyError("Cannot connect to Ollama. Please ensure Ollama is running.");
    }

    private void HandleTimeout()
    {
        TryNotifyError("Translation timed out. The model may be loading or overloaded.");
    }

    private void TryNotifyError(string message)
    {
        // Rate limit notifications
        var now = DateTime.UtcNow;
        if (now - _lastNotificationTime < _notificationCooldown)
            return;

        if (_state.TryMarkErrorNotification())
        {
            _lastNotificationTime = now;
            // In a real implementation, this would show a notification
            // For now, we just mark the state so the UI can handle it
            Console.Error.WriteLine($"[TranslationService] {message}");
        }
    }

    private static string NormalizeEndpoint(string endpoint)
    {
        var normalized = endpoint.TrimEnd('/');

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
            return normalized;

        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        var builder = new UriBuilder(uri)
        {
            Host = "127.0.0.1"
        };

        return builder.Uri.ToString().TrimEnd('/');
    }

    internal static OllamaChatRequest CreateChatRequest(
        string model,
        bool stream,
        string? keepAlive,
        double temperature,
        TranslationPair translationPair,
        string text)
    {
        return new OllamaChatRequest
        {
            Model = model,
            Stream = stream,
            KeepAlive = keepAlive,
            Options = new OllamaOptions { Temperature = temperature },
            Messages = new List<OllamaMessage>
            {
                new() { Role = "system", Content = BuildSystemPrompt(translationPair) },
                new() { Role = "user", Content = text }
            }
        };
    }

    internal static string BuildSystemPrompt(TranslationPair translationPair)
    {
        ArgumentNullException.ThrowIfNull(translationPair);

        return $@"You are a deterministic translator from {translationPair.SourceLanguage.Code} to {translationPair.TargetLanguage.Code}.

Rules:
- Translate ONLY the given text exactly once.
- Never ask for clarification.
- Never explain ambiguity.
- If the input is a single word, translate it to the most common neutral equivalent in the target language.
- Preserve the tone and register of the original text when possible.
- Keep punctuation appropriate for the target language.
- If the text is already in {translationPair.TargetLanguage.Code}, return it unchanged.
- Prefer natural, idiomatic usage in the target language.
- Return ONLY the translation, nothing else.";
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _httpClient.Dispose();
        _disposed = true;

        GC.SuppressFinalize(this);
    }
}

#region Ollama API Models

internal class OllamaChatRequest
{
    public string Model { get; set; } = "";
    public bool Stream { get; set; }
    public string? KeepAlive { get; set; }
    public OllamaOptions? Options { get; set; }
    public List<OllamaMessage> Messages { get; set; } = new();
}

internal class OllamaMessage
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
}

internal class OllamaOptions
{
    [JsonPropertyName("temperature")]
    public double Temperature { get; set; }
}

internal class OllamaChatResponse
{
    public string Model { get; set; } = "";
    public OllamaMessage? Message { get; set; }
    public bool Done { get; set; }
}

internal class OllamaTagsResponse
{
    public List<OllamaModelInfo> Models { get; set; } = new();
}

internal class OllamaModelInfo
{
    public string? Name { get; set; }
    public string? ModifiedAt { get; set; }
    public long Size { get; set; }
}

#endregion
