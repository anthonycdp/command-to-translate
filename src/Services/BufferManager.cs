// src/Services/BufferManager.cs
using System.Text;
using RealTranslate.Core;

namespace RealTranslate.Services;

/// <summary>
/// Manages text buffer accumulation and debounce logic for translation triggers.
/// Sits between KeyboardHook and TranslationService.
/// </summary>
public class BufferManager : IDisposable
{
    private readonly AppState _state;
    private readonly object _lock = new();

    // Buffer state
    private readonly StringBuilder _currentWord = new();
    private readonly StringBuilder _currentPhrase = new();
    private int _charactersSinceLastInject;
    private DateTime _lastKeystroke = DateTime.MinValue;
    private CancellationTokenSource? _pendingTranslation;
    private string _lastInjectedText = string.Empty;

    // Debounce timer
    private System.Threading.Timer? _debounceTimer;
    private bool _disposed;

    // Punctuation characters that trigger translation with context refinement
    private static readonly HashSet<char> PunctuationChars = new()
    {
        '.', ',', '!', '?', ';', ':', '-', '"', '\'',
        '(', ')', '[', ']', '{', '}', '<', '>'
    };

    public BufferManager(AppState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    /// <summary>
    /// Gets the current word buffer content.
    /// </summary>
    public string CurrentWord
    {
        get
        {
            lock (_lock)
            {
                return _currentWord.ToString();
            }
        }
    }

    /// <summary>
    /// Gets the current phrase buffer content.
    /// </summary>
    public string CurrentPhrase
    {
        get
        {
            lock (_lock)
            {
                return _currentPhrase.ToString();
            }
        }
    }

    /// <summary>
    /// Gets the number of characters since the last injection.
    /// </summary>
    public int CharactersSinceLastInject
    {
        get
        {
            lock (_lock)
            {
                return _charactersSinceLastInject;
            }
        }
    }

    /// <summary>
    /// Gets the timestamp of the last keystroke.
    /// </summary>
    public DateTime LastKeystroke
    {
        get
        {
            lock (_lock)
            {
                return _lastKeystroke;
            }
        }
    }

    /// <summary>
    /// Processes a keyboard event and returns a translation task if one should be triggered.
    /// </summary>
    /// <param name="kbEvent">The keyboard event to process.</param>
    /// <returns>A translation task if translation should occur, null otherwise.</returns>
    public TranslationTask? ProcessEvent(KbEvent kbEvent)
    {
        if (kbEvent.IsPasswordField)
        {
            // Don't capture or translate password fields
            return null;
        }

        lock (_lock)
        {
            if (_disposed)
                return null;

            _lastKeystroke = DateTime.UtcNow;

            return kbEvent.Type switch
            {
                KbEventType.Char => ProcessChar(kbEvent.Character),
                KbEventType.Space => ProcessSpace(),
                KbEventType.Punctuation => ProcessPunctuation(kbEvent.Character),
                KbEventType.Enter => ProcessEnter(),
                KbEventType.Backspace => ProcessBackspace(),
                _ => null
            };
        }
    }

    private TranslationTask? ProcessChar(char? character)
    {
        if (character is null)
            return null;

        // Append to both buffers
        _currentWord.Append(character);
        _currentPhrase.Append(character);
        _charactersSinceLastInject++;

        // Start debounce timer for phrase translation
        StartDebounceTimer();

        return null;
    }

    private TranslationTask? ProcessSpace()
    {
        // Cancel any pending debounce
        CancelPendingTranslation();

        var currentWordStr = _currentWord.ToString();

        // Add space to phrase buffer
        _currentPhrase.Append(' ');
        _charactersSinceLastInject++;

        // Translate current word if not empty
        if (!string.IsNullOrWhiteSpace(currentWordStr))
        {
            // Reset word buffer
            _currentWord.Clear();

            return CreateTranslationTask(
                currentWordStr,
                TranslationMode.WordOnly,
                currentWordStr.Length);
        }

        _currentWord.Clear();
        return null;
    }

    private TranslationTask? ProcessPunctuation(char? character)
    {
        // Cancel any pending debounce
        CancelPendingTranslation();

        var currentWordStr = _currentWord.ToString();

        // Add punctuation to phrase buffer
        if (character.HasValue)
        {
            _currentPhrase.Append(character.Value);
            _charactersSinceLastInject++;
        }

        // Translate word + schedule context refinement
        if (!string.IsNullOrWhiteSpace(currentWordStr))
        {
            _currentWord.Clear();

            // Return with punctuation included for context
            return CreateTranslationTask(
                currentWordStr,
                TranslationMode.WordOnly,
                currentWordStr.Length);
        }

        _currentWord.Clear();
        return null;
    }

    private TranslationTask? ProcessEnter()
    {
        // Cancel any pending debounce
        CancelPendingTranslation();

        var phrase = _currentPhrase.ToString().Trim();
        var word = _currentWord.ToString().Trim();

        // Combine word and phrase if word has content
        string textToTranslate;
        int charsToDelete;

        if (!string.IsNullOrWhiteSpace(word) && !phrase.EndsWith(word))
        {
            textToTranslate = string.IsNullOrWhiteSpace(phrase)
                ? word
                : $"{phrase} {word}";
            charsToDelete = _charactersSinceLastInject;
        }
        else if (!string.IsNullOrWhiteSpace(phrase))
        {
            textToTranslate = phrase;
            charsToDelete = _charactersSinceLastInject;
        }
        else
        {
            ResetBuffers();
            return null;
        }

        // Reset buffers after creating the task
        ResetBuffers();

        return CreateTranslationTask(
            textToTranslate,
            TranslationMode.PhraseWithContext,
            charsToDelete);
    }

    private TranslationTask? ProcessBackspace()
    {
        // Cancel any pending translation
        CancelPendingTranslation();

        // Adjust word buffer
        if (_currentWord.Length > 0)
        {
            _currentWord.Remove(_currentWord.Length - 1, 1);
        }

        // Adjust phrase buffer
        if (_currentPhrase.Length > 0)
        {
            _currentPhrase.Remove(_currentPhrase.Length - 1, 1);
        }

        // Adjust character count
        if (_charactersSinceLastInject > 0)
        {
            _charactersSinceLastInject--;
        }

        return null;
    }

    private void StartDebounceTimer()
    {
        CancelPendingTranslation();

        var debounceMs = _state.Config?.Behavior?.DebounceMs ?? 500;

        _pendingTranslation = new CancellationTokenSource();
        _debounceTimer = new System.Threading.Timer(
            OnDebounceElapsed,
            _pendingTranslation.Token,
            debounceMs,
            Timeout.Infinite);
    }

    private void OnDebounceElapsed(object? state)
    {
        if (state is not CancellationToken cancellationToken || cancellationToken.IsCancellationRequested)
            return;

        TranslationTask? taskToWrite = null;

        lock (_lock)
        {
            if (_disposed || cancellationToken.IsCancellationRequested)
                return;

            var phrase = _currentPhrase.ToString().Trim();

            if (string.IsNullOrWhiteSpace(phrase))
                return;

            // Emit phrase with context for refinement
            taskToWrite = CreateTranslationTask(
                phrase,
                TranslationMode.PhraseWithContext,
                _charactersSinceLastInject);
        }

        // Write to channel outside the lock to avoid potential deadlock
        if (taskToWrite is not null)
        {
            _ = AppChannels.TranslationTasks.Writer.WriteAsync(taskToWrite, cancellationToken);
        }
    }

    private static TranslationTask? CreateTranslationTask(
        string text,
        TranslationMode mode,
        int charactersToDelete)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // Use CancellationToken.None - cancellation is managed by the debounce timer
        return new TranslationTask(
            text,
            mode,
            charactersToDelete,
            CancellationToken.None);
    }

    private void CancelPendingTranslation()
    {
        _debounceTimer?.Dispose();
        _debounceTimer = null;

        _pendingTranslation?.Cancel();
        _pendingTranslation?.Dispose();
        _pendingTranslation = null;
    }

    /// <summary>
    /// Updates the tracked injected text after a successful injection.
    /// </summary>
    /// <param name="text">The text that was injected.</param>
    public void UpdateInjectedText(string text)
    {
        lock (_lock)
        {
            _lastInjectedText = text ?? string.Empty;
        }
    }

    /// <summary>
    /// Gets the last injected text.
    /// </summary>
    /// <returns>The last injected text.</returns>
    public string GetInjectedText()
    {
        lock (_lock)
        {
            return _lastInjectedText;
        }
    }

    /// <summary>
    /// Resets all buffers and pending translations.
    /// </summary>
    public void ResetBuffers()
    {
        lock (_lock)
        {
            CancelPendingTranslation();
            _currentWord.Clear();
            _currentPhrase.Clear();
            _charactersSinceLastInject = 0;
            _lastKeystroke = DateTime.MinValue;
        }
    }

    /// <summary>
    /// Checks if a character is a punctuation character.
    /// </summary>
    public static bool IsPunctuation(char c)
    {
        return PunctuationChars.Contains(c);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;
            CancelPendingTranslation();
        }

        GC.SuppressFinalize(this);
    }
}
