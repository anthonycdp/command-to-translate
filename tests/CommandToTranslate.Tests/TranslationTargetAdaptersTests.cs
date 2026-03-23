using CommandToTranslate.Core;
using CommandToTranslate.Native;
using CommandToTranslate.Services;
using Xunit;

namespace CommandToTranslate.Tests;

public class TranslationTargetAdaptersTests
{
    [Fact]
    public async Task GenericTextField_SelectSource_UsesCtrlShiftHome()
    {
        var adapter = new GenericTextFieldAdapter();
        var inputDispatcher = new RecordingInputDispatcher();

        await adapter.SelectSourceAsync(inputDispatcher, CancellationToken.None);

        Assert.Collection(
            inputDispatcher.Chords,
            chord => Assert.Equal(new ushort[] { 0x11, 0x10, 0x24 }, chord));
    }

    [Fact]
    public async Task GenericTextField_ReplaceSelection_WithCursorFallback_ReselectsAndTypesTranslation()
    {
        var adapter = new GenericTextFieldAdapter();
        var inputDispatcher = new RecordingInputDispatcher();

        await adapter.ReplaceSelectionAsync(
            inputDispatcher,
            sourceText: "vamos",
            translatedText: "let's go",
            usedCursorFallback: true,
            CancellationToken.None);

        Assert.Empty(inputDispatcher.Keys);
        Assert.Equal(["let's go"], inputDispatcher.TypedTexts);
        Assert.Collection(
            inputDispatcher.Chords,
            chord => Assert.Equal(new ushort[] { 0x11, 0x10, 0x24 }, chord));
    }

    [Fact]
    public async Task WindowsTerminal_ReplaceSelection_WithCursorFallback_UsesBackspaceAndTerminalPaste()
    {
        var adapter = new WindowsTerminalLineAdapter();
        var inputDispatcher = new RecordingInputDispatcher();

        await adapter.ReplaceSelectionAsync(
            inputDispatcher,
            sourceText: "vamos",
            translatedText: "let's go",
            usedCursorFallback: true,
            CancellationToken.None);

        Assert.Equal(new ushort[] { 0x23 }, inputDispatcher.Keys);
        Assert.Equal([(ushort)0x08, (ushort)0x08, (ushort)0x08, (ushort)0x08, (ushort)0x08], inputDispatcher.RepeatedKeys);
        Assert.Empty(inputDispatcher.TypedTexts);
        Assert.Collection(
            inputDispatcher.Chords,
            chord => Assert.Equal(new ushort[] { 0x11, 0x10, 0x56 }, chord));
    }

    [Fact]
    public async Task ElectronTerminal_ReplaceSelection_UsesBackspaceAndShiftInsert()
    {
        var adapter = new ElectronTerminalAdapter();
        var inputDispatcher = new RecordingInputDispatcher();

        await adapter.ReplaceSelectionAsync(
            inputDispatcher,
            sourceText: "ola",
            translatedText: "hello",
            usedCursorFallback: true,
            CancellationToken.None);

        Assert.Empty(inputDispatcher.Keys);
        Assert.Equal([(ushort)0x08, (ushort)0x08, (ushort)0x08], inputDispatcher.RepeatedKeys);
        Assert.Empty(inputDispatcher.TypedTexts);
        Assert.Collection(
            inputDispatcher.Chords,
            chord => Assert.Equal(new ushort[] { 0x10, 0x2D }, chord));
    }

    private sealed class RecordingInputDispatcher : IInputDispatcher
    {
        public List<ushort> Keys { get; } = [];
        public List<ushort> RepeatedKeys { get; } = [];
        public List<ushort[]> Chords { get; } = [];
        public List<string> TypedTexts { get; } = [];

        public Task SendKeyAsync(ushort virtualKey, CancellationToken ct)
        {
            Keys.Add(virtualKey);
            return Task.CompletedTask;
        }

        public Task SendChordAsync(IReadOnlyList<ushort> virtualKeys, CancellationToken ct)
        {
            Chords.Add([.. virtualKeys]);
            return Task.CompletedTask;
        }

        public Task SendRepeatedKeyAsync(ushort virtualKey, int count, CancellationToken ct)
        {
            for (var index = 0; index < count; index++)
            {
                RepeatedKeys.Add(virtualKey);
            }

            return Task.CompletedTask;
        }

        public Task TypeTextAsync(string text, CancellationToken ct)
        {
            TypedTexts.Add(text);
            return Task.CompletedTask;
        }
    }
}
