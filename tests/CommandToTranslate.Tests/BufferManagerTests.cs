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
    public void ProcessEvent_ResetsBufferWhenWindowChanges()
    {
        using var buffer = new BufferManager();

        Type(buffer, "primeira", (IntPtr)1);
        Type(buffer, "segunda", (IntPtr)2);

        Assert.Equal("segunda", buffer.CurrentPhrase);
        Assert.Equal("segunda", buffer.CurrentWord);
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

        var (phrase, characterCount) = buffer.ConsumeCurrentPhrase();

        Assert.Equal("sudo apt", phrase);
        Assert.Equal(8, characterCount);
        Assert.Equal(string.Empty, buffer.CurrentPhrase);
        Assert.Equal(0, buffer.BufferedCharacterCount);
    }

    private static void Type(BufferManager buffer, string text, IntPtr? windowHandle = null)
    {
        var handle = windowHandle ?? IntPtr.Zero;
        foreach (var character in text)
        {
            var eventType = character == ' ' ? KbEventType.Space : KbEventType.Char;
            char? value = eventType == KbEventType.Char ? character : null;
            buffer.ProcessEvent(new KbEvent(value, eventType, handle, false));
        }
    }
}
