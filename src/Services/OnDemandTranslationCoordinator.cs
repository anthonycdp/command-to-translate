using CommandToTranslate.Core;

namespace CommandToTranslate.Services;

public sealed record OnDemandTranslationResult(bool Success, string Message);

public sealed class OnDemandTranslationCoordinator
{
    private sealed record CaptureResult(string SourceText, bool UsedCursorFallback);

    private readonly AppState _state;
    private readonly ITextTranslator _translator;
    private readonly IFocusContextService _focusContextService;
    private readonly IClipboardService _clipboardService;
    private readonly IInputDispatcher _inputDispatcher;
    private readonly IReadOnlyList<ITranslationTargetAdapter> _adapters;
    private readonly BufferManager? _bufferManager;
    private readonly SemaphoreSlim _executionGate = new(1, 1);
    private readonly TimeSpan _clipboardTimeout;
    private readonly TimeSpan _hostSettleDelay;

    public OnDemandTranslationCoordinator(
        AppState state,
        ITextTranslator translator,
        IFocusContextService focusContextService,
        IClipboardService clipboardService,
        IInputDispatcher inputDispatcher,
        IReadOnlyList<ITranslationTargetAdapter> adapters,
        BufferManager? bufferManager = null)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _translator = translator ?? throw new ArgumentNullException(nameof(translator));
        _focusContextService = focusContextService ?? throw new ArgumentNullException(nameof(focusContextService));
        _clipboardService = clipboardService ?? throw new ArgumentNullException(nameof(clipboardService));
        _inputDispatcher = inputDispatcher ?? throw new ArgumentNullException(nameof(inputDispatcher));
        _adapters = adapters ?? throw new ArgumentNullException(nameof(adapters));
        _bufferManager = bufferManager;
        _clipboardTimeout = TimeSpan.FromMilliseconds(Math.Max(100, _state.Config.Behavior.ClipboardTimeoutMs));
        _hostSettleDelay = TimeSpan.FromMilliseconds(Math.Max(0, _state.Config.Behavior.HostSettleDelayMs));
    }

    public async Task<OnDemandTranslationResult> ExecuteAsync(CancellationToken ct)
    {
        if (!await _executionGate.WaitAsync(0, ct))
            return new OnDemandTranslationResult(false, "Another translation is already in progress.");

        try
        {
            if (_state.IsPaused)
                return new OnDemandTranslationResult(false, "The translation hotkey is currently disabled.");

            if (!_state.OllamaAvailable)
                return new OnDemandTranslationResult(false, "Ollama is unavailable.");

            var context = _focusContextService.GetFocusedContext();
            if (context.WindowHandle == IntPtr.Zero)
                return new OnDemandTranslationResult(false, "No active window was found.");

            if (context.IsPasswordField)
                return new OnDemandTranslationResult(false, "Focused field is protected and cannot be translated.");

            Logger.Info(
                $"Focused context resolved: process='{context.ProcessName}', class='{context.WindowClassName}'");

            var candidateAdapters = GetCandidateAdapters(context);
            if (candidateAdapters.Count == 0)
            {
                return new OnDemandTranslationResult(
                    false,
                    $"The current host is unsupported for on-demand translation ({context.ProcessName}).");
            }

            var clipboardSnapshot = _clipboardService.CaptureSnapshot();

            try
            {
                CaptureResult? captureResult = null;
                ITranslationTargetAdapter? selectedAdapter = null;
                bool triedKeystrokeBufferAdapter = false;

                foreach (var candidateAdapter in candidateAdapters)
                {
                    Logger.Info($"Attempting capture with {candidateAdapter.Name}");

                    if (candidateAdapter.CanHandle(context) && candidateAdapter.UsesKeystrokeBuffer)
                    {
                        triedKeystrokeBufferAdapter = true;
                    }

                    captureResult = await CaptureSourceAsync(candidateAdapter, ct);
                    if (captureResult != null)
                    {
                        selectedAdapter = candidateAdapter;
                        break;
                    }

                    Logger.Info($"Capture with {candidateAdapter.Name} returned no text");

                    if (ShouldStopAdapterFallback(candidateAdapter, context))
                        break;
                }

                if (captureResult == null || selectedAdapter == null)
                {
                    var message = triedKeystrokeBufferAdapter
                        ? "No text in keystroke buffer. Type the text instead of pasting, then try again."
                        : $"No text could be captured from the active host ({context.ProcessName}/{context.WindowClassName}).";
                    return new OnDemandTranslationResult(false, message);
                }

                Logger.Info($"On-demand translating with {selectedAdapter.Name}: '{captureResult.SourceText}'");

                var translatedText = await _translator.TranslateAsync(captureResult.SourceText, ct);
                if (string.IsNullOrWhiteSpace(translatedText))
                {
                    return new OnDemandTranslationResult(false, "Translation returned empty.");
                }

                _clipboardService.SetText(translatedText);

                await selectedAdapter.ReplaceSelectionAsync(
                    _inputDispatcher,
                    captureResult.SourceText,
                    translatedText,
                    captureResult.UsedCursorFallback,
                    ct);

                // Allow the target application time to process the paste
                // command and read the clipboard before restoring it.
                await Task.Delay(200, ct);

                Logger.Info($"On-demand translation completed with {selectedAdapter.Name}");
                return new OnDemandTranslationResult(true, "Translation completed.");
            }
            finally
            {
                _clipboardService.Restore(clipboardSnapshot);
            }
        }
        catch (OperationCanceledException)
        {
            return new OnDemandTranslationResult(false, "Translation was cancelled.");
        }
        catch (Exception ex)
        {
            Logger.Error("On-demand translation failed", ex);
            return new OnDemandTranslationResult(false, ex.Message);
        }
        finally
        {
            _executionGate.Release();
        }
    }

    private async Task WaitForHostSettleAsync(CancellationToken ct)
    {
        if (_hostSettleDelay > TimeSpan.Zero)
            await Task.Delay(_hostSettleDelay, ct);
    }

    private async Task<CaptureResult?> CaptureSourceAsync(ITranslationTargetAdapter adapter, CancellationToken ct)
    {
        // Keystroke-buffer capture: read from the BufferManager instead of
        // attempting clipboard-based select+copy (which is impossible in
        // TUI terminals like xterm.js / Electron).
        if (adapter.UsesKeystrokeBuffer)
        {
            if (_bufferManager == null)
            {
                Logger.Warning("Adapter requests keystroke buffer but BufferManager is not available");
                return null;
            }

            var (phrase, charCount, _) = _bufferManager.ConsumeCurrentPhrase();
            if (string.IsNullOrWhiteSpace(phrase) || charCount <= 0)
            {
                Logger.Info("Keystroke buffer is empty — nothing to translate");
                return null;
            }

            Logger.Info($"Captured from keystroke buffer ({charCount} chars): '{phrase}'");
            return new CaptureResult(phrase, UsedCursorFallback: true);
        }

        if (!adapter.SkipPreSelectionCopy)
        {
            var selectedText = await CopyCurrentSelectionAsync(adapter, ct);
            if (!string.IsNullOrWhiteSpace(selectedText))
                return new CaptureResult(selectedText, UsedCursorFallback: false);
        }

        await adapter.SelectSourceAsync(_inputDispatcher, ct);
        await WaitForHostSettleAsync(ct);

        var cursorScopedText = await CopyCurrentSelectionAsync(adapter, ct);
        if (string.IsNullOrWhiteSpace(cursorScopedText))
            return null;

        return new CaptureResult(cursorScopedText, UsedCursorFallback: true);
    }

    private async Task<string?> CopyCurrentSelectionAsync(ITranslationTargetAdapter adapter, CancellationToken ct)
    {
        var clipboardProbe = $"__command_to_translate_probe_{Guid.NewGuid():N}__";
        _clipboardService.SetText(clipboardProbe);

        await adapter.CopySelectionAsync(_inputDispatcher, ct);
        return await _clipboardService.WaitForCopiedTextAsync(clipboardProbe, _clipboardTimeout, ct);
    }

    private IReadOnlyList<ITranslationTargetAdapter> GetCandidateAdapters(FocusedContext context)
    {
        var preferredAdapters = _adapters.Where(candidate => candidate.CanHandle(context)).ToList();
        var fallbackAdapters = _adapters.Where(candidate => !candidate.CanHandle(context)).ToList();

        return preferredAdapters.Concat(fallbackAdapters).ToList();
    }

    /// <summary>
    /// Determines whether adapter fallback should stop to avoid sending dangerous shortcuts
    /// (like Ctrl+C/SIGINT) to terminals after a keystroke-buffer adapter fails.
    /// </summary>
    private static bool ShouldStopAdapterFallback(ITranslationTargetAdapter adapter, FocusedContext context)
    {
        // Non-keystroke-buffer adapters can safely continue to fallback
        if (!adapter.UsesKeystrokeBuffer)
            return false;

        // A keystroke-buffer adapter that wasn't the preferred handler for this context
        // can be skipped - continue trying other adapters
        if (!adapter.CanHandle(context))
            return false;

        // A preferred keystroke-buffer adapter failed (empty buffer) - stop here to avoid
        // sending Ctrl+C to terminals which would trigger SIGINT
        return true;
    }
}
