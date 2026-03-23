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
    private const ushort ControlKey = 0x11;
    private const ushort ShiftKey = 0x10;
    private const ushort HomeKey = 0x24;
    private const ushort CKey = 0x43;

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
        return inputDispatcher.SendChordAsync([ControlKey, ShiftKey, HomeKey], ct);
    }

    public Task CopySelectionAsync(IInputDispatcher inputDispatcher, CancellationToken ct)
    {
        return inputDispatcher.SendChordAsync([ControlKey, CKey], ct);
    }

    public Task ReplaceSelectionAsync(
        IInputDispatcher inputDispatcher,
        string sourceText,
        string translatedText,
        bool usedCursorFallback,
        CancellationToken ct)
    {
        if (usedCursorFallback)
            return ReplaceCursorScopedTextAsync(inputDispatcher, translatedText, ct);

        return inputDispatcher.TypeTextAsync(translatedText, ct);
    }

    private static async Task ReplaceCursorScopedTextAsync(
        IInputDispatcher inputDispatcher,
        string translatedText,
        CancellationToken ct)
    {
        await inputDispatcher.SendChordAsync([ControlKey, ShiftKey, HomeKey], ct);
        await inputDispatcher.TypeTextAsync(translatedText, ct);
    }
}

public sealed class WindowsTerminalLineAdapter : ITranslationTargetAdapter
{
    private const ushort ControlKey = 0x11;
    private const ushort ShiftKey = 0x10;
    private const ushort HomeKey = 0x24;
    private const ushort EndKey = 0x23;
    private const ushort CKey = 0x43;
    private const ushort VKey = 0x56;

    public string Name => "WindowsTerminalLine";

    public bool CanHandle(FocusedContext context)
    {
        return context.ProcessName.Contains("terminal", StringComparison.OrdinalIgnoreCase) ||
               context.WindowClassName.Contains("CASCADIA", StringComparison.OrdinalIgnoreCase);
    }

    public Task SelectSourceAsync(IInputDispatcher inputDispatcher, CancellationToken ct)
    {
        return inputDispatcher.SendChordAsync([ShiftKey, HomeKey], ct);
    }

    public Task CopySelectionAsync(IInputDispatcher inputDispatcher, CancellationToken ct)
    {
        return inputDispatcher.SendChordAsync([ControlKey, ShiftKey, CKey], ct);
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
        await inputDispatcher.SendKeyAsync(EndKey, ct);
        await inputDispatcher.SendRepeatedKeyAsync(Win32.VK_BACK, sourceText.Length, ct);
        await inputDispatcher.SendChordAsync([ControlKey, ShiftKey, VKey], ct);
    }
}

public sealed class ClassicConsoleLineAdapter : ITranslationTargetAdapter
{
    private const ushort ControlKey = 0x11;
    private const ushort ShiftKey = 0x10;
    private const ushort HomeKey = 0x24;
    private const ushort EndKey = 0x23;
    private const ushort CKey = 0x43;
    private const ushort VKey = 0x56;

    public string Name => "ClassicConsoleLine";

    public bool CanHandle(FocusedContext context)
    {
        return string.Equals(context.WindowClassName, "ConsoleWindowClass", StringComparison.OrdinalIgnoreCase) ||
               context.ProcessName.Contains("conhost", StringComparison.OrdinalIgnoreCase) ||
               context.ProcessName.Contains("openconsole", StringComparison.OrdinalIgnoreCase);
    }

    public Task SelectSourceAsync(IInputDispatcher inputDispatcher, CancellationToken ct)
    {
        return inputDispatcher.SendChordAsync([ShiftKey, HomeKey], ct);
    }

    public Task CopySelectionAsync(IInputDispatcher inputDispatcher, CancellationToken ct)
    {
        return inputDispatcher.SendChordAsync([ControlKey, CKey], ct);
    }

    public async Task ReplaceSelectionAsync(
        IInputDispatcher inputDispatcher,
        string sourceText,
        string translatedText,
        bool usedCursorFallback,
        CancellationToken ct)
    {
        await inputDispatcher.SendKeyAsync(EndKey, ct);
        await inputDispatcher.SendRepeatedKeyAsync(Win32.VK_BACK, sourceText.Length, ct);
        await inputDispatcher.SendChordAsync([ControlKey, VKey], ct);
    }
}

public sealed class ElectronTerminalAdapter : ITranslationTargetAdapter
{
    private const ushort ShiftKey = 0x10;
    private const ushort InsertKey = 0x2D;

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
        // Erase the typed text with Backspace, then paste the translation
        // via Shift+Insert (universally supported, avoids SIGINT from
        // Ctrl+C/V keybinding conflicts in TUI apps).
        await inputDispatcher.SendRepeatedKeyAsync(Win32.VK_BACK, sourceText.Length, ct);
        await inputDispatcher.SendChordAsync([ShiftKey, InsertKey], ct);
    }
}
