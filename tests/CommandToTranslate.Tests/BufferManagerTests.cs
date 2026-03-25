using CommandToTranslate.Core;
using CommandToTranslate.Services;
using Xunit;

namespace CommandToTranslate.Tests;

public class BufferManagerTests
{
    [Fact]
    public void ProcessEvent_BuildsPhraseAcrossWordsAndPunctuation()
    {
        using var buffer = new BufferManager();

        Type(buffer, "vamos");
        buffer.ProcessEvent(new KbEvent(null, KbEventType.Space, IntPtr.Zero, false));
        Type(buffer, "dormir");
        buffer.ProcessEvent(new KbEvent('?', KbEventType.Punctuation, IntPtr.Zero, false));

        Assert.Equal("vamos dormir?", buffer.CurrentPhrase);
        Assert.Equal(string.Empty, buffer.CurrentWord);
        Assert.Equal(13, buffer.BufferedCharacterCount);
    }

    [Fact]
    public void ProcessEvent_IgnoresLeadingSpaces()
    {
        using var buffer = new BufferManager();

        buffer.ProcessEvent(new KbEvent(null, KbEventType.Space, IntPtr.Zero, false));
        Type(buffer, "ola");

        Assert.Equal("ola", buffer.CurrentPhrase);
        Assert.Equal("ola", buffer.CurrentWord);
    }

    [Fact]
    public void ProcessEvent_BackspaceRebuildsCurrentWord()
    {
        using var buffer = new BufferManager();

        Type(buffer, "bom");
        buffer.ProcessEvent(new KbEvent(null, KbEventType.Space, IntPtr.Zero, false));
        Type(buffer, "dia");
        buffer.ProcessEvent(new KbEvent(null, KbEventType.Backspace, IntPtr.Zero, false));

        Assert.Equal("bom di", buffer.CurrentPhrase);
        Assert.Equal("di", buffer.CurrentWord);
    }

    [Fact]
    public void ProcessEvent_ResetsBufferOnEnter()
    {
        using var buffer = new BufferManager();

        Type(buffer, "linha atual");
        buffer.ProcessEvent(new KbEvent(null, KbEventType.Enter, IntPtr.Zero, false));

        Assert.Equal(string.Empty, buffer.CurrentPhrase);
        Assert.Equal(string.Empty, buffer.CurrentWord);
        Assert.Equal(0, buffer.BufferedCharacterCount);
    }

    [Fact]
    public void ProcessEvent_IgnoresPasswordFields()
    {
        using var buffer = new BufferManager();

        buffer.ProcessEvent(new KbEvent('s', KbEventType.Char, IntPtr.Zero, true));

        Assert.Equal(string.Empty, buffer.CurrentPhrase);
        Assert.Equal(0, buffer.BufferedCharacterCount);
    }

    [Fact]
    public void ConsumeCurrentPhrase_ReturnsBufferedTextAndClearsState()
    {
        using var buffer = new BufferManager();

        Type(buffer, "sudo ");
        Type(buffer, "apt");

        var (phrase, characterCount, _) = buffer.ConsumeCurrentPhrase();

        Assert.Equal("sudo apt", phrase);
        Assert.Equal(8, characterCount);
        Assert.Equal(string.Empty, buffer.CurrentPhrase);
        Assert.Equal(0, buffer.BufferedCharacterCount);
    }

    [Fact]
    public void ProcessEvent_InsertsAtCursorPosition_AfterCursorLeft()
    {
        using var buffer = new BufferManager();

        Type(buffer, "helo");
        // Move cursor left 1 position (between 'l' and 'o')
        buffer.ProcessEvent(new KbEvent(null, KbEventType.CursorLeft, IntPtr.Zero, false));
        // Insert 'l' at cursor position
        buffer.ProcessEvent(new KbEvent('l', KbEventType.Char, IntPtr.Zero, false));

        Assert.Equal("hello", buffer.CurrentPhrase);
    }

    [Fact]
    public void ProcessEvent_BackspaceDeletesAtCursorPosition()
    {
        using var buffer = new BufferManager();

        Type(buffer, "helllo");
        // Move cursor left 2 positions (between second 'l' and third 'l')
        buffer.ProcessEvent(new KbEvent(null, KbEventType.CursorLeft, IntPtr.Zero, false));
        buffer.ProcessEvent(new KbEvent(null, KbEventType.CursorLeft, IntPtr.Zero, false));
        // Backspace removes the char before cursor (second 'l')
        buffer.ProcessEvent(new KbEvent(null, KbEventType.Backspace, IntPtr.Zero, false));

        Assert.Equal("hello", buffer.CurrentPhrase);
    }

    [Fact]
    public void ProcessEvent_DeleteRemovesCharAfterCursor()
    {
        using var buffer = new BufferManager();

        Type(buffer, "helllo");
        // Move cursor left 3 positions (between 'e' and first 'l')
        buffer.ProcessEvent(new KbEvent(null, KbEventType.CursorLeft, IntPtr.Zero, false));
        buffer.ProcessEvent(new KbEvent(null, KbEventType.CursorLeft, IntPtr.Zero, false));
        buffer.ProcessEvent(new KbEvent(null, KbEventType.CursorLeft, IntPtr.Zero, false));
        // Delete removes char at cursor (first 'l')
        buffer.ProcessEvent(new KbEvent(null, KbEventType.Delete, IntPtr.Zero, false));

        Assert.Equal("hello", buffer.CurrentPhrase);
    }

    [Fact]
    public void ProcessEvent_HomeMovesToStart_EndMovesToEnd()
    {
        using var buffer = new BufferManager();

        Type(buffer, "abc");
        buffer.ProcessEvent(new KbEvent(null, KbEventType.Home, IntPtr.Zero, false));
        Type(buffer, "X");
        buffer.ProcessEvent(new KbEvent(null, KbEventType.End, IntPtr.Zero, false));
        Type(buffer, "Y");

        Assert.Equal("XabcY", buffer.CurrentPhrase);
    }

    [Fact]
    public void ProcessEvent_CursorClampedAtBoundaries()
    {
        using var buffer = new BufferManager();

        Type(buffer, "ab");
        // Move right beyond end — should clamp
        buffer.ProcessEvent(new KbEvent(null, KbEventType.CursorRight, IntPtr.Zero, false));
        buffer.ProcessEvent(new KbEvent(null, KbEventType.CursorRight, IntPtr.Zero, false));
        Type(buffer, "c");
        // Move left beyond start — should clamp
        buffer.ProcessEvent(new KbEvent(null, KbEventType.Home, IntPtr.Zero, false));
        buffer.ProcessEvent(new KbEvent(null, KbEventType.CursorLeft, IntPtr.Zero, false));
        buffer.ProcessEvent(new KbEvent(null, KbEventType.CursorLeft, IntPtr.Zero, false));
        Type(buffer, "Z");

        Assert.Equal("Zabc", buffer.CurrentPhrase);
    }

    [Fact]
    public void ProcessEvent_HistoryNavigationResetsBuffer()
    {
        using var buffer = new BufferManager();

        Type(buffer, "something typed");
        buffer.ProcessEvent(new KbEvent(null, KbEventType.HistoryNavigation, IntPtr.Zero, false));

        Assert.Equal(string.Empty, buffer.CurrentPhrase);
        Assert.Equal(0, buffer.BufferedCharacterCount);
    }

    [Fact]
    public void ProcessEvent_SpaceInsertsAtCursorPosition()
    {
        using var buffer = new BufferManager();

        Type(buffer, "helloworld");
        // Move cursor left 5 positions (between 'o' and 'w')
        for (int i = 0; i < 5; i++)
            buffer.ProcessEvent(new KbEvent(null, KbEventType.CursorLeft, IntPtr.Zero, false));
        // Insert space at cursor
        buffer.ProcessEvent(new KbEvent(null, KbEventType.Space, IntPtr.Zero, false));

        Assert.Equal("hello world", buffer.CurrentPhrase);
    }

    [Fact]
    public void ConsumeCurrentPhrase_ReturnsCursorPosition()
    {
        using var buffer = new BufferManager();

        Type(buffer, "hello");
        buffer.ProcessEvent(new KbEvent(null, KbEventType.CursorLeft, IntPtr.Zero, false));
        buffer.ProcessEvent(new KbEvent(null, KbEventType.CursorLeft, IntPtr.Zero, false));

        var (phrase, characterCount, cursorPosition) = buffer.ConsumeCurrentPhrase();

        Assert.Equal("hello", phrase);
        Assert.Equal(5, characterCount);
        Assert.Equal(3, cursorPosition);
    }

    [Fact]
    public void ProcessEvent_PasteInsertsTextAtCursor()
    {
        using var buffer = new BufferManager();

        Type(buffer, "hello ");
        buffer.ProcessEvent(new KbEvent(null, KbEventType.Paste, IntPtr.Zero, false, "world"));

        Assert.Equal("hello world", buffer.CurrentPhrase);
        Assert.Equal(11, buffer.BufferedCharacterCount);
    }

    [Fact]
    public void ProcessEvent_PasteInsertsAtMiddle()
    {
        using var buffer = new BufferManager();

        Type(buffer, "hd");
        buffer.ProcessEvent(new KbEvent(null, KbEventType.CursorLeft, IntPtr.Zero, false));
        buffer.ProcessEvent(new KbEvent(null, KbEventType.Paste, IntPtr.Zero, false, "ello worl"));

        Assert.Equal("hello world", buffer.CurrentPhrase);
    }

    [Fact]
    public void ProcessEvent_PasteStripsNewlines()
    {
        using var buffer = new BufferManager();

        buffer.ProcessEvent(new KbEvent(null, KbEventType.Paste, IntPtr.Zero, false, "line1\nline2\r\nline3"));

        Assert.Equal("line1 line2 line3", buffer.CurrentPhrase);
    }

    [Fact]
    public void ProcessEvent_PasteReadsClipboardWhenTextIsNull()
    {
        using var buffer = new BufferManager(clipboardReader: () => "from clipboard");

        buffer.ProcessEvent(new KbEvent(null, KbEventType.Paste, IntPtr.Zero, false));

        Assert.Equal("from clipboard", buffer.CurrentPhrase);
    }

    [Fact]
    public void ProcessEvent_PasteIgnoresEmptyClipboard()
    {
        using var buffer = new BufferManager(clipboardReader: () => null);

        Type(buffer, "existing");
        buffer.ProcessEvent(new KbEvent(null, KbEventType.Paste, IntPtr.Zero, false));

        Assert.Equal("existing", buffer.CurrentPhrase);
    }

    [Fact]
    public void ProcessEvent_ResetsBufferWhenWindowHandleChanges()
    {
        using var buffer = new BufferManager();

        Type(buffer, "hello", (IntPtr)1);
        Type(buffer, "world", (IntPtr)2);

        Assert.Equal("world", buffer.CurrentPhrase);
    }

    private static void Type(BufferManager buffer, string text, IntPtr? windowHandle = null)
    {
        foreach (var character in text)
        {
            var eventType = character == ' ' ? KbEventType.Space : KbEventType.Char;
            char? value = eventType == KbEventType.Char ? character : null;
            buffer.ProcessEvent(new KbEvent(value, eventType, windowHandle ?? IntPtr.Zero, false));
        }
    }
}
