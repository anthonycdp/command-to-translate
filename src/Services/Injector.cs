// src/Services/Injector.cs
using RealTranslate.Core;
using RealTranslate.Native;

namespace RealTranslate.Services;

/// <summary>
/// Handles text injection by simulating keyboard input via SendInput.
/// Replaces original text with translated text.
/// </summary>
public class Injector
{
    private readonly AppState _state;
    private readonly BufferManager _bufferManager;

    public Injector(AppState state, BufferManager bufferManager)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _bufferManager = bufferManager ?? throw new ArgumentNullException(nameof(bufferManager));
    }

    /// <summary>
    /// Injects translated text by deleting original characters and typing new text.
    /// </summary>
    /// <param name="task">The injection task containing the translated text.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task InjectAsync(InjectionTask task, CancellationToken ct = default)
    {
        if (task == null)
            throw new ArgumentNullException(nameof(task));

        if (string.IsNullOrEmpty(task.TranslatedText))
            return;

        _state.IsInjecting = true;

        try
        {
            var injectDelay = _state.Config?.Behavior?.InjectDelayMs ?? 10;

            // Delete original characters
            await DeleteCharactersAsync(task.CharactersToDelete, injectDelay, ct);

            if (ct.IsCancellationRequested)
                return;

            // Type translated text
            await TypeTextAsync(task.TranslatedText, injectDelay, ct);

            if (ct.IsCancellationRequested)
                return;

            // Add trailing space for word translations
            if (!task.IsRefinement)
            {
                await SendKeyAsync(Win32.VK_SPACE, injectDelay, ct);
            }

            // Update the last injected text for refinement tracking
            _bufferManager.UpdateInjectedText(task.TranslatedText);
        }
        finally
        {
            // Loop protection: wait before clearing IsInjecting
            var loopProtectionMs = _state.Config?.Behavior?.LoopProtectionMs ?? 50;
            await Task.Delay(loopProtectionMs, ct);

            _state.IsInjecting = false;
        }
    }

    /// <summary>
    /// Injects a refinement by calculating word-level diff and only changing differing portions.
    /// </summary>
    /// <param name="refinedText">The refined/corrected text.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task InjectRefinementAsync(string refinedText, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(refinedText))
            return;

        var lastInjected = _bufferManager.GetInjectedText();

        if (string.IsNullOrEmpty(lastInjected))
        {
            // No previous text to refine, just inject
            await InjectAsync(new InjectionTask(refinedText, 0, true), ct);
            return;
        }

        _state.IsInjecting = true;

        try
        {
            var injectDelay = _state.Config?.Behavior?.InjectDelayMs ?? 10;

            // Calculate word-level diff
            var (charsToBackspace, textToType) = CalculateDiff(lastInjected, refinedText);

            if (charsToBackspace == 0 && string.IsNullOrEmpty(textToType))
            {
                // No changes needed
                return;
            }

            // Backspace differing portion
            if (charsToBackspace > 0)
            {
                await DeleteCharactersAsync(charsToBackspace, injectDelay, ct);
            }

            if (ct.IsCancellationRequested)
                return;

            // Type corrected portion
            if (!string.IsNullOrEmpty(textToType))
            {
                await TypeTextAsync(textToType, injectDelay, ct);
            }

            // Update the last injected text
            _bufferManager.UpdateInjectedText(refinedText);
        }
        finally
        {
            // Loop protection: wait before clearing IsInjecting
            var loopProtectionMs = _state.Config?.Behavior?.LoopProtectionMs ?? 50;
            await Task.Delay(loopProtectionMs, ct);

            _state.IsInjecting = false;
        }
    }

    /// <summary>
    /// Calculates the difference between two strings at the word level.
    /// Returns the number of characters to backspace and the text to type.
    /// </summary>
    private static (int charsToBackspace, string textToType) CalculateDiff(string original, string refined)
    {
        if (string.IsNullOrEmpty(original))
            return (0, refined);

        if (string.IsNullOrEmpty(refined))
            return (original.Length, string.Empty);

        // Split into words
        var originalWords = original.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var refinedWords = refined.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Find the first differing word from the end
        int originalWordIndex = originalWords.Length - 1;
        int refinedWordIndex = refinedWords.Length - 1;

        // Compare from the end to find common suffix
        while (originalWordIndex >= 0 && refinedWordIndex >= 0)
        {
            if (originalWords[originalWordIndex] != refinedWords[refinedWordIndex])
                break;

            originalWordIndex--;
            refinedWordIndex--;
        }

        // If all words match, no change needed
        if (originalWordIndex < 0 && refinedWordIndex < 0)
            return (0, string.Empty);

        // Calculate characters to backspace (from first differing word to end)
        int charsToBackspace = 0;
        for (int i = Math.Max(0, originalWordIndex); i < originalWords.Length; i++)
        {
            charsToBackspace += originalWords[i].Length;
            // Add space between words (except for last word)
            if (i < originalWords.Length - 1)
                charsToBackspace++;
        }

        // Build text to type (from first differing word to end)
        var textToType = string.Empty;
        if (refinedWordIndex >= 0)
        {
            var wordsToType = refinedWords.Skip(Math.Max(0, refinedWordIndex)).ToArray();
            textToType = string.Join(" ", wordsToType);
        }

        return (charsToBackspace, textToType);
    }

    /// <summary>
    /// Deletes characters by sending backspace key presses.
    /// </summary>
    private async Task DeleteCharactersAsync(int count, int delayMs, CancellationToken ct)
    {
        for (int i = 0; i < count && !ct.IsCancellationRequested; i++)
        {
            await SendKeyAsync(Win32.VK_BACK, delayMs, ct);
        }
    }

    /// <summary>
    /// Types text character by character using Unicode input.
    /// </summary>
    private async Task TypeTextAsync(string text, int delayMs, CancellationToken ct)
    {
        foreach (char c in text)
        {
            if (ct.IsCancellationRequested)
                break;

            await SendUnicodeCharAsync(c, delayMs, ct);
        }
    }

    /// <summary>
    /// Sends a virtual key press (down + up).
    /// </summary>
    private async Task SendKeyAsync(ushort vk, int delayMs, CancellationToken ct)
    {
        var inputs = new Win32.INPUT[2];

        // Key down
        inputs[0].Type = Win32.INPUT_KEYBOARD;
        inputs[0].Ki = new Win32.KEYBDINPUT
        {
            wVk = vk,
            wScan = 0,
            dwFlags = 0,
            time = 0,
            dwExtraInfo = IntPtr.Zero
        };

        // Key up
        inputs[1].Type = Win32.INPUT_KEYBOARD;
        inputs[1].Ki = new Win32.KEYBDINPUT
        {
            wVk = vk,
            wScan = 0,
            dwFlags = Win32.KEYEVENTF_KEYUP,
            time = 0,
            dwExtraInfo = IntPtr.Zero
        };

        Win32.SendInput(2, inputs, System.Runtime.InteropServices.Marshal.SizeOf<Win32.INPUT>());

        await Task.Delay(delayMs, ct);
    }

    /// <summary>
    /// Sends a Unicode character using KEYEVENTF_UNICODE.
    /// </summary>
    private async Task SendUnicodeCharAsync(char c, int delayMs, CancellationToken ct)
    {
        var inputs = new Win32.INPUT[2];

        // Key down
        inputs[0].Type = Win32.INPUT_KEYBOARD;
        inputs[0].Ki = new Win32.KEYBDINPUT
        {
            wVk = 0,
            wScan = c,
            dwFlags = Win32.KEYEVENTF_UNICODE,
            time = 0,
            dwExtraInfo = IntPtr.Zero
        };

        // Key up
        inputs[1].Type = Win32.INPUT_KEYBOARD;
        inputs[1].Ki = new Win32.KEYBDINPUT
        {
            wVk = 0,
            wScan = c,
            dwFlags = Win32.KEYEVENTF_UNICODE | Win32.KEYEVENTF_KEYUP,
            time = 0,
            dwExtraInfo = IntPtr.Zero
        };

        Win32.SendInput(2, inputs, System.Runtime.InteropServices.Marshal.SizeOf<Win32.INPUT>());

        await Task.Delay(delayMs, ct);
    }
}
