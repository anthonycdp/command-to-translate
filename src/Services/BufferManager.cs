using System.Text;
using CommandToTranslate.Core;

namespace CommandToTranslate.Services;

/// <summary>
/// Captures typed text for the focused host so on-demand translation can
/// recover the current terminal line when clipboard-based selection is
/// unavailable.
/// </summary>
public sealed class BufferManager : IDisposable
{
    private static readonly HashSet<char> WordBoundaryCharacters =
    [
        ' ',
        '.',
        ',',
        '!',
        '?',
        ';',
        ':',
        '-',
        '/',
        '"',
        '\'',
        '(',
        ')',
        '[',
        ']',
        '{',
        '}',
        '<',
        '>'
    ];

    private readonly object _lock = new();
    private readonly StringBuilder _currentPhrase = new();
    private readonly StringBuilder _currentWord = new();

    private IntPtr _lastWindowHandle = IntPtr.Zero;
    private bool _disposed;

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

    public int BufferedCharacterCount
    {
        get
        {
            lock (_lock)
            {
                return _currentPhrase.Length;
            }
        }
    }

    public void ProcessEvent(KbEvent keyboardEvent)
    {
        if (keyboardEvent.IsPasswordField)
            return;

        lock (_lock)
        {
            if (_disposed)
                return;

            if (keyboardEvent.WindowHandle != _lastWindowHandle)
            {
                ResetState();
                _lastWindowHandle = keyboardEvent.WindowHandle;
            }

            switch (keyboardEvent.Type)
            {
                case KbEventType.Char:
                    AppendCharacter(keyboardEvent.Character);
                    break;
                case KbEventType.Space:
                    AppendSpace();
                    break;
                case KbEventType.Punctuation:
                    AppendPunctuation(keyboardEvent.Character);
                    break;
                case KbEventType.Backspace:
                    RemoveLastCharacter();
                    break;
                case KbEventType.Enter:
                    ResetState();
                    break;
            }
        }
    }

    public (string Phrase, int CharacterCount) ConsumeCurrentPhrase()
    {
        lock (_lock)
        {
            var phrase = _currentPhrase.ToString();
            var characterCount = _currentPhrase.Length;
            ResetState();
            return (phrase, characterCount);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;
            ResetState();
        }

        GC.SuppressFinalize(this);
    }

    private void AppendCharacter(char? character)
    {
        if (character is null)
            return;

        _currentPhrase.Append(character.Value);
        _currentWord.Append(character.Value);
    }

    private void AppendSpace()
    {
        if (_currentPhrase.Length == 0)
            return;

        _currentPhrase.Append(' ');
        _currentWord.Clear();
    }

    private void AppendPunctuation(char? character)
    {
        if (character is null)
            return;

        _currentPhrase.Append(character.Value);
        _currentWord.Clear();
    }

    private void RemoveLastCharacter()
    {
        if (_currentPhrase.Length == 0)
            return;

        _currentPhrase.Length--;
        RebuildCurrentWord();
    }

    private void RebuildCurrentWord()
    {
        _currentWord.Clear();

        if (_currentPhrase.Length == 0)
            return;

        var lastCharacter = _currentPhrase[^1];
        if (char.IsWhiteSpace(lastCharacter) || WordBoundaryCharacters.Contains(lastCharacter))
            return;

        var startIndex = _currentPhrase.Length - 1;
        while (startIndex >= 0)
        {
            var character = _currentPhrase[startIndex];
            if (char.IsWhiteSpace(character) || WordBoundaryCharacters.Contains(character))
            {
                startIndex++;
                break;
            }

            startIndex--;
        }

        if (startIndex < 0)
            startIndex = 0;

        _currentWord.Append(_currentPhrase.ToString(startIndex, _currentPhrase.Length - startIndex));
    }

    private void ResetState()
    {
        _currentPhrase.Clear();
        _currentWord.Clear();
    }
}
