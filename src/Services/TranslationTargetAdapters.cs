using CommandToTranslate.Core;
using CommandToTranslate.Native;

namespace CommandToTranslate.Services;

public interface ITranslationTargetAdapter
{
    string Name { get; }
    bool CanHandle(FocusedContext context);
    /// <summary>
    /// When true the coordinator skips the speculative copy attempt (which
    /// sends the copy shortcut without a prior selection).  Electron terminals
    /// need this because a bare Ctrl+C with no selection is interpreted as
    /// SIGINT, killing the user's input.
    /// </summary>
    bool SkipPreSelectionCopy => false;
    /// <summary>
    /// When true the coordinator captures source text from the keystroke
    /// buffer (BufferManager) instead of attempting clipboard-based
    /// select+copy.  Required for TUI terminals (xterm.js / Electron) where
    /// no keyboard shortcut can create a copyable text selection.
    /// </summary>
    bool UsesKeystrokeBuffer => false;
    Task SelectSourceAsync(IInputDispatcher inputDispatcher, CancellationToken ct);
    Task CopySelectionAsync(IInputDispatcher inputDispatcher, CancellationToken ct);
    Task ReplaceSelectionAsync(
        IInputDispatcher inputDispatcher,
        string sourceText,
        string translatedText,
        bool usedCursorFallback,
        CancellationToken ct);
}

public sealed class GenericTextFieldAdapter : ITranslationTargetAdapter
{
    public string Name => "GenericTextField";

    public bool CanHandle(FocusedContext context)
    {
        return !string.Equals(context.WindowClassName, "ConsoleWindowClass", StringComparison.OrdinalIgnoreCase) &&
               !context.ProcessName.Contains("terminal", StringComparison.OrdinalIgnoreCase) &&
               !context.ProcessName.Contains("conhost", StringComparison.OrdinalIgnoreCase) &&
               !context.ProcessName.Contains("openconsole", StringComparison.OrdinalIgnoreCase);
    }

    public Task SelectSourceAsync(IInputDispatcher inputDispatcher, CancellationToken ct)
    {
        // Ctrl+A selects all text in the field regardless of cursor position,
        // ensuring the full content is captured and later replaced.
        return inputDispatcher.SendChordAsync([VirtualKeys.Control, VirtualKeys.A], ct);
    }

    public Task CopySelectionAsync(IInputDispatcher inputDispatcher, CancellationToken ct)
    {
        return inputDispatcher.SendChordAsync([VirtualKeys.Control, VirtualKeys.C], ct);
    }

    public Task ReplaceSelectionAsync(
        IInputDispatcher inputDispatcher,
        string sourceText,
        string translatedText,
        bool usedCursorFallback,
        CancellationToken ct)
    {
        // The translated text is already in the clipboard (set by the coordinator
        // before calling this method).  Paste via Ctrl+V — faster and more
        // reliable than typing character-by-character, and fully preserves
        // formatting (line breaks, accented characters, sentence spacing).
        if (usedCursorFallback)
            return SelectAllAndPasteAsync(inputDispatcher, ct);

        // Pre-existing selection is still active — paste directly over it.
        return inputDispatcher.SendChordAsync([VirtualKeys.Control, VirtualKeys.V], ct);
    }

    private static async Task SelectAllAndPasteAsync(
        IInputDispatcher inputDispatcher,
        CancellationToken ct)
    {
        await inputDispatcher.SendChordAsync([VirtualKeys.Control, VirtualKeys.A], ct);
        await inputDispatcher.SendChordAsync([VirtualKeys.Control, VirtualKeys.V], ct);
    }
}

public sealed class WindowsTerminalLineAdapter : ITranslationTargetAdapter
{
    public string Name => "WindowsTerminalLine";

    public bool CanHandle(FocusedContext context)
    {
        return context.ProcessName.Contains("terminal", StringComparison.OrdinalIgnoreCase) ||
               context.WindowClassName.Contains("CASCADIA", StringComparison.OrdinalIgnoreCase);
    }

    public Task SelectSourceAsync(IInputDispatcher inputDispatcher, CancellationToken ct)
    {
        return inputDispatcher.SendChordAsync([VirtualKeys.Shift, VirtualKeys.Home], ct);
    }

    public Task CopySelectionAsync(IInputDispatcher inputDispatcher, CancellationToken ct)
    {
        return inputDispatcher.SendChordAsync([VirtualKeys.Control, VirtualKeys.Shift, VirtualKeys.C], ct);
    }

    public async Task ReplaceSelectionAsync(
        IInputDispatcher inputDispatcher,
        string sourceText,
        string translatedText,
        bool usedCursorFallback,
        CancellationToken ct)
    {
        // Terminal paste (Ctrl+Shift+V) sends text as keyboard input — it does
        // NOT replace a viewport selection.  This means re-selecting with
        // Shift+Home only works in shells with their own selection logic
        // (PSReadLine) but fails in TUI apps (Claude Code, Codex, etc.).
        //
        // Universal approach: move cursor to end, erase the source text with
        // Backspace, then paste the translated text from the clipboard.
        await inputDispatcher.SendKeyAsync(VirtualKeys.End, ct);
        await inputDispatcher.SendRepeatedKeyAsync(Win32.VK_BACK, sourceText.Length, ct);
        await inputDispatcher.SendChordAsync([VirtualKeys.Control, VirtualKeys.Shift, VirtualKeys.V], ct);
    }
}

public sealed class ClassicConsoleLineAdapter : ITranslationTargetAdapter
{
    public string Name => "ClassicConsoleLine";

    public bool CanHandle(FocusedContext context)
    {
        return string.Equals(context.WindowClassName, "ConsoleWindowClass", StringComparison.OrdinalIgnoreCase) ||
               context.ProcessName.Contains("conhost", StringComparison.OrdinalIgnoreCase) ||
               context.ProcessName.Contains("openconsole", StringComparison.OrdinalIgnoreCase);
    }

    public Task SelectSourceAsync(IInputDispatcher inputDispatcher, CancellationToken ct)
    {
        return inputDispatcher.SendChordAsync([VirtualKeys.Shift, VirtualKeys.Home], ct);
    }

    public Task CopySelectionAsync(IInputDispatcher inputDispatcher, CancellationToken ct)
    {
        return inputDispatcher.SendChordAsync([VirtualKeys.Control, VirtualKeys.C], ct);
    }

    public async Task ReplaceSelectionAsync(
        IInputDispatcher inputDispatcher,
        string sourceText,
        string translatedText,
        bool usedCursorFallback,
        CancellationToken ct)
    {
        await inputDispatcher.SendKeyAsync(VirtualKeys.End, ct);
        await inputDispatcher.SendRepeatedKeyAsync(Win32.VK_BACK, sourceText.Length, ct);
        await inputDispatcher.SendChordAsync([VirtualKeys.Control, VirtualKeys.V], ct);
    }
}

public sealed class ElectronTerminalAdapter : ITranslationTargetAdapter
{
    public string Name => "ElectronTerminal";

    // No clipboard-based capture is possible in TUI terminals — use the
    // keystroke buffer instead.
    public bool SkipPreSelectionCopy => true;
    public bool UsesKeystrokeBuffer => true;

    public bool CanHandle(FocusedContext context)
    {
        return string.Equals(context.WindowClassName, "Chrome_WidgetWin_1", StringComparison.OrdinalIgnoreCase);
    }

    public Task SelectSourceAsync(IInputDispatcher inputDispatcher, CancellationToken ct)
    {
        // Not used — capture comes from the keystroke buffer.
        return Task.CompletedTask;
    }

    public Task CopySelectionAsync(IInputDispatcher inputDispatcher, CancellationToken ct)
    {
        // Not used — capture comes from the keystroke buffer.
        return Task.CompletedTask;
    }

    public async Task ReplaceSelectionAsync(
        IInputDispatcher inputDispatcher,
        string sourceText,
        string translatedText,
        bool usedCursorFallback,
        CancellationToken ct)
    {
        // Move cursor to end of line first — the cursor may be in the middle
        // after the user edited text with arrow keys.
        await inputDispatcher.SendKeyAsync(Win32.VK_END, ct);

        // Erase the typed text with Backspace, then paste the translation
        // via Ctrl+Shift+V (standard terminal paste shortcut that works in
        // xterm.js/Electron terminals like Antigravity, Claude Code, Codex).
        await inputDispatcher.SendRepeatedKeyAsync(Win32.VK_BACK, sourceText.Length, ct);
        await inputDispatcher.SendChordAsync([VirtualKeys.Control, VirtualKeys.Shift, VirtualKeys.V], ct);
    }
}

/// <summary>
/// Common virtual key codes used by translation target adapters.
/// </summary>
internal static class VirtualKeys
{
    public const ushort Control = 0x11;
    public const ushort Shift = 0x10;
    public const ushort A = 0x41;
    public const ushort C = 0x43;
    public const ushort V = 0x56;
    public const ushort Home = 0x24;
    public const ushort End = 0x23;
}
