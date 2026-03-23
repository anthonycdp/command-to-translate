using System.Runtime.InteropServices;
using CommandToTranslate.Core;
using CommandToTranslate.Native;

namespace CommandToTranslate.Services;

public interface IInputDispatcher
{
    Task SendKeyAsync(ushort virtualKey, CancellationToken ct);
    Task SendChordAsync(IReadOnlyList<ushort> virtualKeys, CancellationToken ct);
    Task SendRepeatedKeyAsync(ushort virtualKey, int count, CancellationToken ct);
    Task TypeTextAsync(string text, CancellationToken ct);
}

public sealed class InputDispatcher : IInputDispatcher
{
    private readonly TimeSpan _stepDelay;

    public InputDispatcher(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var stepDelayMs = Math.Max(0, state.Config.Behavior.ShortcutStepDelayMs);
        _stepDelay = TimeSpan.FromMilliseconds(stepDelayMs);
    }

    public Task SendKeyAsync(ushort virtualKey, CancellationToken ct)
    {
        return SendChordAsync([virtualKey], ct);
    }

    public async Task SendChordAsync(IReadOnlyList<ushort> virtualKeys, CancellationToken ct)
    {
        if (virtualKeys.Count == 0)
            return;

        var inputs = BuildChordInputs(virtualKeys);
        var sent = Win32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Win32.INPUT>());
        if (sent != inputs.Length)
        {
            throw new InvalidOperationException(
                $"SendInput failed for shortcut. Sent={sent}, Expected={inputs.Length}, LastError={Marshal.GetLastWin32Error()}");
        }

        if (_stepDelay > TimeSpan.Zero)
            await Task.Delay(_stepDelay, ct);
    }

    public async Task SendRepeatedKeyAsync(ushort virtualKey, int count, CancellationToken ct)
    {
        if (count <= 0)
            return;

        var inputs = new Win32.INPUT[count * 2];
        for (var i = 0; i < count; i++)
        {
            inputs[i * 2] = CreateKeyInput(virtualKey, keyUp: false);
            inputs[i * 2 + 1] = CreateKeyInput(virtualKey, keyUp: true);
        }

        var sent = Win32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Win32.INPUT>());
        if (sent != inputs.Length)
        {
            throw new InvalidOperationException(
                $"SendInput failed for repeated key. Sent={sent}, Expected={inputs.Length}, LastError={Marshal.GetLastWin32Error()}");
        }

        if (_stepDelay > TimeSpan.Zero)
            await Task.Delay(_stepDelay, ct);
    }

    public async Task TypeTextAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(text))
            return;

        foreach (var character in text)
        {
            var inputs = BuildUnicodeInputs(character);
            var sent = Win32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Win32.INPUT>());
            if (sent != inputs.Length)
            {
                throw new InvalidOperationException(
                    $"SendInput failed for text typing. Sent={sent}, Expected={inputs.Length}, LastError={Marshal.GetLastWin32Error()}");
            }

            if (_stepDelay > TimeSpan.Zero)
                await Task.Delay(_stepDelay, ct);
        }
    }

    private static Win32.INPUT[] BuildChordInputs(IReadOnlyList<ushort> virtualKeys)
    {
        var inputs = new Win32.INPUT[virtualKeys.Count * 2];

        for (var index = 0; index < virtualKeys.Count; index++)
        {
            inputs[index] = CreateKeyInput(virtualKeys[index], keyUp: false);
        }

        for (var index = 0; index < virtualKeys.Count; index++)
        {
            var keyIndex = virtualKeys.Count - 1 - index;
            inputs[virtualKeys.Count + index] = CreateKeyInput(virtualKeys[keyIndex], keyUp: true);
        }

        return inputs;
    }

    private static Win32.INPUT[] BuildUnicodeInputs(char character)
    {
        return
        [
            new Win32.INPUT
            {
                Type = Win32.INPUT_KEYBOARD,
                Ki = new Win32.KEYBDINPUT
                {
                    wVk = 0,
                    wScan = character,
                    dwFlags = Win32.KEYEVENTF_UNICODE,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            },
            new Win32.INPUT
            {
                Type = Win32.INPUT_KEYBOARD,
                Ki = new Win32.KEYBDINPUT
                {
                    wVk = 0,
                    wScan = character,
                    dwFlags = Win32.KEYEVENTF_UNICODE | Win32.KEYEVENTF_KEYUP,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        ];
    }

    private static Win32.INPUT CreateKeyInput(ushort virtualKey, bool keyUp)
    {
        return new Win32.INPUT
        {
            Type = Win32.INPUT_KEYBOARD,
            Ki = new Win32.KEYBDINPUT
            {
                wVk = virtualKey,
                wScan = 0,
                dwFlags = keyUp ? Win32.KEYEVENTF_KEYUP : 0,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        };
    }
}
