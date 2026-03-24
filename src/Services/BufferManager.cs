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
        ' ', '.', ',', '!', '?', ';', ':', '-', '/',
        '"', '\'', '(', ')', '[', ']', '{', '}', '<', '>'
    ];

    private readonly object _lock = new();
    private readonly StringBuilder _currentPhrase = new();
    private readonly StringBuilder _currentWord = new();
    private readonly Func<string?>? _clipboardReader;

    private int _cursorPosition;
    private bool _disposed;

    public BufferManager(Func<string?>? clipboardReader = null)
    {
        _clipboardReader = clipboardReader;
    }

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

            switch (keyboardEvent.Type)
            {
                case KbEventType.Char:
                    InsertCharacter(keyboardEvent.Character);
                    break;
                case KbEventType.Space:
                    InsertSpace();
                    break;
                case KbEventType.Punctuation:
                    InsertPunctuation(keyboardEvent.Character);
                    break;
                case KbEventType.Backspace:
                    RemoveCharacterBeforeCursor();
                    break;
                case KbEventType.Delete:
                    RemoveCharacterAfterCursor();
                    break;
                case KbEventType.CursorLeft:
                    MoveCursorLeft();
                    break;
                case KbEventType.CursorRight:
                    MoveCursorRight();
                    break;
                case KbEventType.Home:
                    MoveCursorToStart();
                    break;
                case KbEventType.End:
                    MoveCursorToEnd();
                    break;
                case KbEventType.Paste:
                    InsertPastedText(keyboardEvent.Text);
                    break;
                case KbEventType.HistoryNavigation:
                    ResetState();
                    break;
                case KbEventType.Enter:
                    ResetState();
                    break;
            }
        }
    }

    public (string Phrase, int CharacterCount, int CursorPosition) ConsumeCurrentPhrase()
    {
        lock (_lock)
        {
            var phrase = _currentPhrase.ToString();
            var characterCount = _currentPhrase.Length;
            var cursorPosition = _cursorPosition;
            ResetState();
            return (phrase, characterCount, cursorPosition);
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

    private void InsertCharacter(char? character)
    {
        if (character is null)
            return;

        _currentPhrase.Insert(_cursorPosition, character.Value);
        _cursorPosition++;
        RebuildCurrentWord();
    }

    private void InsertSpace()
    {
        if (_currentPhrase.Length == 0 && _cursorPosition == 0)
            return;

        _currentPhrase.Insert(_cursorPosition, ' ');
        _cursorPosition++;
        _currentWord.Clear();
    }

    private void InsertPunctuation(char? character)
    {
        if (character is null)
            return;

        _currentPhrase.Insert(_cursorPosition, character.Value);
        _cursorPosition++;
        _currentWord.Clear();
    }

    private void RemoveCharacterBeforeCursor()
    {
        if (_cursorPosition == 0)
            return;

        _cursorPosition--;
        _currentPhrase.Remove(_cursorPosition, 1);
        RebuildCurrentWord();
    }

    private void RemoveCharacterAfterCursor()
    {
        if (_cursorPosition >= _currentPhrase.Length)
            return;

        _currentPhrase.Remove(_cursorPosition, 1);
        RebuildCurrentWord();
    }

    private void MoveCursorLeft()
    {
        if (_cursorPosition > 0)
        {
            _cursorPosition--;
            RebuildCurrentWord();
        }
    }

    private void MoveCursorRight()
    {
        if (_cursorPosition < _currentPhrase.Length)
        {
            _cursorPosition++;
            RebuildCurrentWord();
        }
    }

    private void MoveCursorToStart()
    {
        _cursorPosition = 0;
        RebuildCurrentWord();
    }

    private void MoveCursorToEnd()
    {
        _cursorPosition = _currentPhrase.Length;
        RebuildCurrentWord();
    }

    private void InsertPastedText(string? text)
    {
        var pastedText = text;

        if (pastedText is null && _clipboardReader is not null)
            pastedText = _clipboardReader();

        if (string.IsNullOrEmpty(pastedText))
            return;

        // Strip newlines — buffer models a single input line
        pastedText = pastedText.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');

        _currentPhrase.Insert(_cursorPosition, pastedText);
        _cursorPosition += pastedText.Length;
        RebuildCurrentWord();
    }

    private void RebuildCurrentWord()
    {
        _currentWord.Clear();

        if (_currentPhrase.Length == 0 || _cursorPosition == 0)
            return;

        var charBeforeCursor = _currentPhrase[_cursorPosition - 1];
        if (char.IsWhiteSpace(charBeforeCursor) || WordBoundaryCharacters.Contains(charBeforeCursor))
            return;

        var startIndex = _cursorPosition - 1;
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

        var endIndex = _cursorPosition;
        _currentWord.Append(_currentPhrase.ToString(startIndex, endIndex - startIndex));
    }

    private void ResetState()
    {
        _currentPhrase.Clear();
        _currentWord.Clear();
        _cursorPosition = 0;
    }
}
