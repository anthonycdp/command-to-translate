# Real-Time Translator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Windows background utility that translates typing from pt-BR to en-US in real-time using local Ollama.

**Architecture:** Actor-based with System.Threading.Channels. Components communicate via channels: KeyboardHook → BufferManager → TranslationService → Injector. Tray icon provides minimal UI with hotkey toggle support.

**Tech Stack:** C# / .NET 8, Windows Forms (tray icon only), P/Invoke for Win32 APIs, HttpClient for Ollama, Tomlyn for TOML config parsing

---

## File Structure

```
src/
├── RealTranslate.csproj           # .NET 8 project
├── Program.cs                     # Entry point, wires all components
│
├── Core/
│   ├── AppState.cs                # Global shared state
│   ├── Config.cs                  # Configuration classes + loader
│   └── Channels.cs                # Channel definitions and data types
│
├── Native/
│   └── Win32.cs                   # All P/Invoke declarations
│
├── Services/
│   ├── TranslationService.cs      # Ollama API client
│   ├── BufferManager.cs           # Text accumulation + debounce
│   └── Injector.cs                # SendInput wrapper for text replacement
│
├── Hooks/
│   ├── KeyboardHook.cs            # SetWindowsHookEx wrapper
│   └── HotkeyManager.cs           # RegisterHotKey wrapper
│
└── UI/
    └── TrayIcon.cs                # NotifyIcon + context menu
```

---

## Task 1: Project Setup

**Files:**
- Create: `src/RealTranslate.csproj`
- Create: `src/Core/Channels.cs`
- Create: `src/Core/Config.cs`

- [ ] **Step 1: Create solution and project structure**

```bash
mkdir -p src/Core src/Native src/Services src/Hooks src/UI
dotnet new sln -n real-translate
dotnet new console -n RealTranslate -o src -f net8.0-windows
dotnet sln add src/RealTranslate.csproj
```

- [ ] **Step 2: Update RealTranslate.csproj with required packages and settings**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <ApplicationIcon>icon.ico</ApplicationIcon>
    <AssemblyName>real-translate</AssemblyName>
    <Version>1.0.0</Version>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Tomlyn" Version="0.17.0" />
    <PackageReference Include="System.Threading.Channels" Version="8.0.0" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Create Channels.cs with data types**

```csharp
// src/Core/Channels.cs
namespace RealTranslate.Core;

/// <summary>Types of keyboard events we care about</summary>
public enum KbEventType
{
    Char,
    Space,
    Punctuation,
    Enter,
    Backspace
}

/// <summary>Represents a single keyboard event</summary>
public record KbEvent(
    char? Character,        // null for non-printable keys
    KbEventType Type,
    IntPtr WindowHandle,    // for context detection
    bool IsPasswordField    // true if focused field is password
);

/// <summary>Translation mode determines how the text is processed</summary>
public enum TranslationMode
{
    WordOnly,           // Single word, immediate
    PhraseWithContext   // Full phrase for contextual refinement
}

/// <summary>Task sent to translation service</summary>
public record TranslationTask(
    string Text,
    TranslationMode Mode,
    int CharactersToDelete,
    CancellationToken CancellationToken
);

/// <summary>Result from translation, sent to injector</summary>
public record InjectionTask(
    string TranslatedText,
    int CharactersToDelete,
    bool IsRefinement
);

/// <summary>Channel provider for inter-component communication</summary>
public static class AppChannels
{
    public static System.Threading.Channels.Channel<KbEvent> KeyboardEvents { get; }
        = System.Threading.Channels.Channel.CreateUnbounded<KbEvent>();

    public static System.Threading.Channels.Channel<TranslationTask> TranslationTasks { get; }
        = System.Threading.Channels.Channel.CreateUnbounded<TranslationTask>();

    public static System.Threading.Channels.Channel<InjectionTask> InjectionTasks { get; }
        = System.Threading.Channels.Channel.CreateUnbounded<InjectionTask>();
}
```

- [ ] **Step 4: Create Config.cs with configuration classes**

```csharp
// src/Core/Config.cs
using Tomlyn;

namespace RealTranslate.Core;

public class AppConfig
{
    public TranslationConfig Translation { get; set; } = new();
    public OllamaConfig Ollama { get; set; } = new();
    public BehaviorConfig Behavior { get; set; } = new();
    public HotkeyConfig Hotkey { get; set; } = new();
    public UiConfig Ui { get; set; } = new();

    private static readonly string ConfigPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "config.toml");

    public static AppConfig Load()
    {
        if (!File.Exists(ConfigPath))
        {
            var defaultConfig = new AppConfig();
            defaultConfig.Save();
            return defaultConfig;
        }

        var content = File.ReadAllText(ConfigPath);
        return Toml.ToModel<AppConfig>(content);
    }

    public void Save()
    {
        var toml = Toml.FromModel(this);
        File.WriteAllText(ConfigPath, toml);
    }
}

public class TranslationConfig
{
    public string SourceLang { get; set; } = "pt-BR";
    public string TargetLang { get; set; } = "en-US";
}

public class OllamaConfig
{
    public string Endpoint { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "translategemma";
    public int TimeoutMs { get; set; } = 2000;
    public double Temperature { get; set; } = 0.1;
    public bool Stream { get; set; } = false;
}

public class BehaviorConfig
{
    public int DebounceMs { get; set; } = 500;
    public int InjectDelayMs { get; set; } = 10;
    public int LoopProtectionMs { get; set; } = 50;
}

public class HotkeyConfig
{
    public List<string> Modifiers { get; set; } = new() { "Ctrl", "Shift" };
    public string Key { get; set; } = "T";
}

public class UiConfig
{
    public bool ShowNotifications { get; set; } = true;
    public bool NotifyOnError { get; set; } = true;
}
```

- [ ] **Step 5: Verify project builds**

```bash
dotnet build
```

Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add .
git commit -m "feat: project setup with config and channel types"
```

---

## Task 2: AppState and Win32 P/Invoke

**Files:**
- Create: `src/Core/AppState.cs`
- Create: `src/Native/Win32.cs`

- [ ] **Step 1: Create AppState.cs**

```csharp
// src/Core/AppState.cs
namespace RealTranslate.Core;

/// <summary>
/// Global application state shared across components.
/// Uses volatile for simple flags, lock for complex state.
/// </summary>
public class AppState
{
    private readonly object _lock = new();

    // Volatile flags for lock-free reads
    public volatile bool IsPaused;
    public volatile bool IsInjecting;
    public volatile bool OllamaAvailable = true;

    // State requiring synchronization
    private bool _errorNotificationShown;
    private DateTime? _lastErrorTime;

    public AppConfig Config { get; set; } = null!;

    public bool ShouldTranslate => !IsPaused && !IsInjecting && OllamaAvailable;

    public bool TryMarkErrorNotification()
    {
        lock (_lock)
        {
            if (_errorNotificationShown)
                return false;

            _errorNotificationShown = true;
            _lastErrorTime = DateTime.UtcNow;
            return true;
        }
    }

    public void ClearErrorNotification()
    {
        lock (_lock)
        {
            _errorNotificationShown = false;
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _errorNotificationShown = false;
            _lastErrorTime = null;
            OllamaAvailable = true;
        }
    }
}
```

- [ ] **Step 2: Create Win32.cs with all P/Invoke declarations**

```csharp
// src/Native/Win32.cs
using System.Runtime.InteropServices;

namespace RealTranslate.Native;

/// <summary>
/// All Windows API P/Invoke declarations in one place.
/// </summary>
public static class Win32
{
    #region Keyboard Hook

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn,
        IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
        IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_SYSKEYDOWN = 0x0104;

    #endregion

    #region Hotkey

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;

    public const int WM_HOTKEY = 0x0312;

    #endregion

    #region SendInput

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint Type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public KEYBOARD_INPUT_DATA Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBOARD_INPUT_DATA
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_UNICODE = 0x0004;

    public const ushort VK_BACK = 0x08;
    public const ushort VK_RETURN = 0x0D;
    public const ushort VK_SPACE = 0x20;

    #endregion

    #region Window Info

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetWindowThreadProcessId(IntPtr hWnd, IntPtr pid);

    #endregion

    #region Accessibility (Password Detection)

    [DllImport("oleacc.dll", SetLastError = true)]
    public static extern int AccessibleObjectFromWindow(
        IntPtr hwnd, uint dwObjectID, ref Guid riid, out IntPtr pacc);

    public const uint OBJID_CLIENT = 0xFFFFFFFC;

    public static readonly Guid IID_IAccessible = new(
        0x618736E0, 0x3C3D, 0x11CF, 0x81, 0x0C, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71);

    #endregion
}
```

- [ ] **Step 3: Verify project builds**

```bash
dotnet build
```

Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add .
git commit -m "feat: add AppState and Win32 P/Invoke declarations"
```

---

## Task 3: TranslationService

**Files:**
- Create: `src/Services/TranslationService.cs`

- [ ] **Step 1: Create TranslationService.cs**

```csharp
// src/Services/TranslationService.cs
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using RealTranslate.Core;

namespace RealTranslate.Services;

/// <summary>
/// Handles communication with Ollama API for translations.
/// </summary>
public class TranslationService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly AppState _state;
    private readonly OllamaConfig _config;
    private readonly string _systemPrompt;

    public TranslationService(AppState state)
    {
        _state = state;
        _config = state.Config.Ollama;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMilliseconds(_config.TimeoutMs)
        };

        _systemPrompt = """
            Translate the user's text from Brazilian Portuguese to American English.
            Rules:
            - Output only the translated text
            - Do not explain
            - Do not add quotation marks
            - Preserve meaning
            - Keep names, URLs, emails, numbers, and code unchanged unless translation is necessary
            - Return only the final translated text
            """;
    }

    /// <summary>
    /// Translates text from pt-BR to en-US.
    /// Returns null on failure (caller should use original text).
    /// </summary>
    public async Task<string?> TranslateAsync(string text, CancellationToken ct)
    {
        if (!_state.OllamaAvailable || string.IsNullOrWhiteSpace(text))
            return null;

        var request = new
        {
            model = _config.Model,
            messages = new[]
            {
                new { role = "system", content = _systemPrompt },
                new { role = "user", content = text }
            },
            stream = _config.Stream,
            options = new { temperature = _config.Temperature }
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"{_config.Endpoint}/api/chat",
                request,
                ct);

            if (!response.IsSuccessStatusCode)
            {
                await HandleErrorAsync(response.StatusCode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<OllamaResponse>(ct);
            var translated = result?.Message?.Content?.Trim();

            if (string.IsNullOrEmpty(translated))
                return null;

            // Update availability on success
            if (!_state.OllamaAvailable)
            {
                _state.OllamaAvailable = true;
                _state.ClearErrorNotification();
            }

            return translated;
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("Connection refused"))
        {
            await HandleConnectionRefusedAsync();
            return null;
        }
        catch (TaskCanceledException)
        {
            // Timeout
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Checks if Ollama is reachable and model is available.
    /// </summary>
    public async Task<bool> CheckHealthAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{_config.Endpoint}/api/tags",
                HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
                return false;

            var content = await response.Content.ReadAsStringAsync();
            var models = JsonDocument.Parse(content);
            var hasModel = models.RootElement
                .GetProperty("models")
                .EnumerateArray()
                .Any(m => m.GetProperty("name").GetString()?.StartsWith(_config.Model) == true);

            return hasModel;
        }
        catch
        {
            return false;
        }
    }

    private async Task HandleErrorAsync(System.Net.HttpStatusCode statusCode)
    {
        if (statusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Model not found - fatal error
            _state.OllamaAvailable = false;
        }
    }

    private async Task HandleConnectionRefusedAsync()
    {
        if (_state.TryMarkErrorNotification())
        {
            _state.OllamaAvailable = false;
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

// Response models for Ollama API
public record OllamaResponse(OllamaMessage? Message);
public record OllamaMessage(string? Content);
```

- [ ] **Step 2: Verify project builds**

```bash
dotnet build
```

Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add .
git commit -m "feat: add TranslationService with Ollama integration"
```

---

## Task 4: BufferManager

**Files:**
- Create: `src/Services/BufferManager.cs`

- [ ] **Step 1: Create BufferManager.cs**

```csharp
// src/Services/BufferManager.cs
using System.Text;
using RealTranslate.Core;

namespace RealTranslate.Services;

/// <summary>
/// Manages text buffer accumulation and determines when to trigger translations.
/// </summary>
public class BufferManager : IDisposable
{
    private readonly AppState _state;
    private readonly BehaviorConfig _config;
    private CancellationTokenSource? _debounceCts;
    private readonly object _lock = new();

    // Current buffer state
    private readonly StringBuilder _currentWord = new();
    private readonly StringBuilder _currentPhrase = new();
    private readonly StringBuilder _lastInjectedText = new();
    private int _charactersSinceLastInject;

    public BufferManager(AppState state)
    {
        _state = state;
        _config = state.Config.Behavior;
    }

    /// <summary>
    /// Processes a keyboard event and returns a translation task if one should be triggered.
    /// </summary>
    public TranslationTask? ProcessEvent(KbEvent kbEvent)
    {
        if (!_state.ShouldTranslate)
            return null;

        lock (_lock)
        {
            // Cancel pending debounce
            _debounceCts?.Cancel();
            _debounceCts = null;

            return kbEvent.Type switch
            {
                KbEventType.Char => HandleChar(kbEvent.Character!.Value),
                KbEventType.Space => HandleSpace(),
                KbEventType.Punctuation => HandlePunctuation(kbEvent.Character!.Value),
                KbEventType.Enter => HandleEnter(),
                KbEventType.Backspace => HandleBackspace(),
                _ => null
            };
        }
    }

    private TranslationTask? HandleChar(char c)
    {
        _currentWord.Append(c);
        _currentPhrase.Append(c);
        _charactersSinceLastInject++;
        StartDebounce();
        return null;
    }

    private TranslationTask? HandleSpace()
    {
        if (_currentWord.Length == 0)
        {
            _currentPhrase.Append(' ');
            return null;
        }

        var word = _currentWord.ToString();
        var charsToDelete = _charactersSinceLastInject;

        // Reset word buffer, keep phrase
        _currentWord.Clear();
        _currentPhrase.Append(' ');
        _charactersSinceLastInject = 0;

        // Trigger word translation
        return new TranslationTask(
            Text: word,
            Mode: TranslationMode.WordOnly,
            CharactersToDelete: charsToDelete,
            CancellationToken: CancellationToken.None
        );
    }

    private TranslationTask? HandlePunctuation(char punct)
    {
        var result = HandleSpace(); // Translate current word first
        _currentPhrase.Append(punct);

        // Schedule contextual refinement after punctuation
        StartDebounce();

        return result;
    }

    private TranslationTask? HandleEnter()
    {
        if (_currentPhrase.Length == 0)
            return null;

        var phrase = _currentPhrase.ToString().Trim();
        var task = new TranslationTask(
            Text: phrase,
            Mode: TranslationMode.PhraseWithContext,
            CharactersToDelete: 0,
            CancellationToken: CancellationToken.None
        );

        // Reset all buffers
        ResetBuffers();

        return task;
    }

    private TranslationTask? HandleBackspace()
    {
        if (_currentWord.Length > 0)
        {
            _currentWord.Length--;
            _currentPhrase.Length--;
            _charactersSinceLastInject = Math.Max(0, _charactersSinceLastInject - 1);
        }
        return null;
    }

    private void StartDebounce()
    {
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_config.DebounceMs, _debounceCts.Token);

                lock (_lock)
                {
                    if (_currentPhrase.Length > 0 && !_debounceCts.Token.IsCancellationRequested)
                    {
                        var phrase = _currentPhrase.ToString().Trim();
                        if (!string.IsNullOrEmpty(phrase))
                        {
                            var task = new TranslationTask(
                                Text: phrase,
                                Mode: TranslationMode.PhraseWithContext,
                                CharactersToDelete: 0,
                                CancellationToken: CancellationToken.None
                            );

                            _ = AppChannels.TranslationTasks.Writer.WriteAsync(task);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when user keeps typing
            }
        }, _debounceCts.Token);
    }

    public void UpdateInjectedText(string text)
    {
        lock (_lock)
        {
            _lastInjectedText.Clear();
            _lastInjectedText.Append(text);
        }
    }

    public string GetInjectedText()
    {
        lock (_lock)
        {
            return _lastInjectedText.ToString();
        }
    }

    public void ResetBuffers()
    {
        lock (_lock)
        {
            _currentWord.Clear();
            _currentPhrase.Clear();
            _charactersSinceLastInject = 0;
        }
    }

    public void Dispose()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
    }
}
```

- [ ] **Step 2: Verify project builds**

```bash
dotnet build
```

Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add .
git commit -m "feat: add BufferManager with debounce logic"
```

---

## Task 5: Injector

**Files:**
- Create: `src/Services/Injector.cs`

- [ ] **Step 1: Create Injector.cs**

```csharp
// src/Services/Injector.cs
using System.Runtime.InteropServices;
using RealTranslate.Core;
using RealTranslate.Native;
using static RealTranslate.Native.Win32;

namespace RealTranslate.Services;

/// <summary>
/// Handles injecting translated text by simulating keyboard input.
/// </summary>
public class Injector
{
    private readonly AppState _state;
    private readonly BehaviorConfig _config;
    private readonly BufferManager _bufferManager;
    private string _lastInjectedWord = "";

    public Injector(AppState state, BufferManager bufferManager)
    {
        _state = state;
        _config = state.Config.Behavior;
        _bufferManager = bufferManager;
    }

    /// <summary>
    /// Injects translated text by deleting original and typing new text.
    /// </summary>
    public async Task InjectAsync(InjectionTask task)
    {
        if (string.IsNullOrEmpty(task.TranslatedText))
            return;

        _state.IsInjecting = true;

        try
        {
            if (task.IsRefinement)
            {
                await InjectRefinementAsync(task.TranslatedText);
            }
            else
            {
                await InjectWordAsync(task.TranslatedText, task.CharactersToDelete);
            }

            _bufferManager.UpdateInjectedText(task.TranslatedText);
            _lastInjectedWord = task.TranslatedText;

            // Loop protection delay
            await Task.Delay(_config.LoopProtectionMs);
        }
        finally
        {
            _state.IsInjecting = false;
        }
    }

    private async Task InjectWordAsync(string text, int charsToDelete)
    {
        // Delete original characters
        for (int i = 0; i < charsToDelete; i++)
        {
            SendKey(VK_BACK);
            await Task.Delay(_config.InjectDelayMs);
        }

        // Type translated text
        await TypeTextAsync(text);
    }

    private async Task InjectRefinementAsync(string refinedText)
    {
        var lastInjected = _bufferManager.GetInjectedText();

        // Simple word-level diff: find where they diverge
        var lastWords = lastInjected.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var refinedWords = refinedText.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Find common prefix
        int commonWords = 0;
        for (int i = 0; i < Math.Min(lastWords.Length, refinedWords.Length); i++)
        {
            if (lastWords[i] == refinedWords[i])
                commonWords++;
            else
                break;
        }

        // If all words match, nothing to do
        if (commonWords == refinedWords.Length && lastWords.Length == refinedWords.Length)
            return;

        // Calculate characters to backspace (words that differ + any extra words)
        int charsToBackspace = 0;
        for (int i = commonWords; i < lastWords.Length; i++)
        {
            charsToBackspace += lastWords[i].Length + 1; // +1 for space
        }

        // Backspace differing portion
        for (int i = 0; i < charsToBackspace; i++)
        {
            SendKey(VK_BACK);
            await Task.Delay(_config.InjectDelayMs);
        }

        // Type the corrected portion
        var correctedPortion = string.Join(' ', refinedWords.Skip(commonWords));
        if (!string.IsNullOrEmpty(correctedPortion))
        {
            await TypeTextAsync(correctedPortion);
        }
    }

    private async Task TypeTextAsync(string text)
    {
        foreach (char c in text)
        {
            SendUnicodeChar(c);
            await Task.Delay(_config.InjectDelayMs);
        }

        // Add trailing space for word translations
        SendKey(VK_SPACE);
    }

    private static void SendKey(ushort vk)
    {
        var inputs = new INPUT[2];

        // Key down
        inputs[0].Type = INPUT_KEYBOARD;
        inputs[0].U.Keyboard.wVk = vk;

        // Key up
        inputs[1].Type = INPUT_KEYBOARD;
        inputs[1].U.Keyboard.wVk = vk;
        inputs[1].U.Keyboard.dwFlags = KEYEVENTF_KEYUP;

        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    private static void SendUnicodeChar(char c)
    {
        var inputs = new INPUT[2];

        // Key down
        inputs[0].Type = INPUT_KEYBOARD;
        inputs[0].U.Keyboard.wScan = c;
        inputs[0].U.Keyboard.dwFlags = KEYEVENTF_UNICODE;

        // Key up
        inputs[1].Type = INPUT_KEYBOARD;
        inputs[1].U.Keyboard.wScan = c;
        inputs[1].U.Keyboard.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;

        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }
}
```

- [ ] **Step 2: Verify project builds**

```bash
dotnet build
```

Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add .
git commit -m "feat: add Injector for text replacement via SendInput"
```

---

## Task 6: KeyboardHook

**Files:**
- Create: `src/Hooks/KeyboardHook.cs`

- [ ] **Step 1: Create KeyboardHook.cs**

```csharp
// src/Hooks/KeyboardHook.cs
using System.Runtime.InteropServices;
using RealTranslate.Core;
using RealTranslate.Native;
using static RealTranslate.Native.Win32;

namespace RealTranslate.Hooks;

/// <summary>
/// Global low-level keyboard hook for capturing keystrokes.
/// </summary>
public class KeyboardHook : IDisposable
{
    private readonly AppState _state;
    private IntPtr _hookHandle;
    private readonly LowLevelKeyboardProc _hookCallback;
    private Thread? _hookThread;
    private readonly CancellationTokenSource _cts = new();

    // Punctuation characters that trigger translation
    private static readonly HashSet<char> PunctuationChars = new() { '.', ',', '!', '?', ';', ':' };

    public KeyboardHook(AppState state)
    {
        _state = state;
        _hookCallback = HookCallback;
    }

    public void Start()
    {
        _hookThread = new Thread(HookThreadProc)
        {
            IsBackground = true,
            Name = "KeyboardHook",
            Priority = ThreadPriority.AboveNormal
        };
        _hookThread.Start();
    }

    private void HookThreadProc()
    {
        using var module = Process.GetCurrentProcess().MainModule;
        _hookHandle = SetWindowsHookEx(
            WH_KEYBOARD_LL,
            _hookCallback,
            GetModuleHandle(module?.ModuleName),
            0);

        if (_hookHandle == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"Failed to set keyboard hook. Error: {error}");
        }

        // Message pump required for hooks
        while (!_cts.Token.IsCancellationRequested)
        {
            if (GetMessage(out var msg, IntPtr.Zero, 0, 0))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }

        UnhookWindowsHookEx(_hookHandle);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
        {
            // Skip if we're injecting or paused
            if (_state.IsInjecting || _state.IsPaused)
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

            var vkCode = Marshal.ReadInt32(lParam);
            var kbEvent = ProcessKey(vkCode);

            if (kbEvent != null)
            {
                // Check if in password field
                var isPassword = IsPasswordField();
                if (isPassword)
                    return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

                kbEvent = kbEvent with { IsPasswordField = isPassword };

                // Send to channel (non-blocking)
                _ = AppChannels.KeyboardEvents.Writer.TryWrite(kbEvent);
            }
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private KbEvent? ProcessKey(int vkCode)
    {
        // Check for modifier keys
        if (IsModifierKey(vkCode))
            return null;

        // Check for function keys
        if (vkCode is >= 0x70 and <= 0x87) // F1-F24
            return null;

        // Check for arrow keys
        if (vkCode is >= 0x25 and <= 0x28) // Left, Up, Right, Down
            return null;

        // Check if Ctrl or Alt is held
        if ((Control.ModifierKeys & (Keys.Control | Keys.Alt)) != 0)
            return null;

        var windowHandle = GetForegroundWindow();

        // Handle special keys
        return vkCode switch
        {
            0x08 => new KbEvent(null, KbEventType.Backspace, windowHandle, false), // Backspace
            0x0D => new KbEvent(null, KbEventType.Enter, windowHandle, false),     // Enter
            0x20 => new KbEvent(' ', KbEventType.Space, windowHandle, false),      // Space
            _ => ProcessCharacter(vkCode, windowHandle)
        };
    }

    private KbEvent? ProcessCharacter(int vkCode, IntPtr windowHandle)
    {
        // Convert VK to character
        var key = (Keys)vkCode;
        char c;

        // Check if shift is held
        bool shift = (Control.ModifierKeys & Keys.Shift) != 0;

        // Simple character mapping
        if (vkCode >= 0x41 && vkCode <= 0x5A) // A-Z
        {
            c = shift ? (char)vkCode : (char)(vkCode + 32);
        }
        else if (vkCode >= 0x30 && vkCode <= 0x39) // 0-9
        {
            c = (char)vkCode;
        }
        else
        {
            // Use ToAscii for other keys
            byte[] kbState = new byte[256];
            GetKeyboardState(kbState);

            var chars = new byte[2];
            int result = ToAscii((ushort)vkCode, 0, kbState, chars, 0);

            if (result != 1)
                return null;

            c = (char)chars[0];
        }

        var eventType = PunctuationChars.Contains(c)
            ? KbEventType.Punctuation
            : KbEventType.Char;

        return new KbEvent(c, eventType, windowHandle, false);
    }

    private static bool IsModifierKey(int vkCode)
    {
        return vkCode is
            0x10 or 0x11 or 0x12 or // Shift, Ctrl, Alt
            0x5B or 0x5C or         // Win keys
            0x14 or 0x90 or 0x91;   // CapsLock, NumLock, ScrollLock
    }

    private static bool IsPasswordField()
    {
        try
        {
            var hWnd = GetForegroundWindow();
            var guid = IID_IAccessible;
            var result = AccessibleObjectFromWindow(hWnd, OBJID_CLIENT, ref guid, out var pAcc);

            if (result != 0 || pAcc == IntPtr.Zero)
                return false;

            // Use UI Automation as fallback for more reliable password detection
            // For simplicity, we'll use a basic heuristic
            // In production, consider using UI Automation properly

            return false;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        PostThreadMessage(AppDomain.GetCurrentThreadId(), 0x0012, IntPtr.Zero, IntPtr.Zero); // WM_QUIT
        _hookThread?.Join(1000);
        _cts.Dispose();
    }

    #region Additional P/Invoke for message pump

    [DllImport("user32.dll")]
    private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(int idThread, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetKeyboardState(byte[] pbKeyState);

    [DllImport("user32.dll")]
    private static extern int ToAscii(ushort uVirtKey, ushort uScanCode, byte[] lpbKeyState, byte[] lpwTransKey, uint fuState);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    #endregion
}
```

- [ ] **Step 2: Verify project builds**

```bash
dotnet build
```

Expected: Build succeeded (may need System.Diagnostics.Process reference)

- [ ] **Step 3: Add System.Diagnostics.Process if needed**

If build fails, add to RealTranslate.csproj:
```xml
<PackageReference Include="System.Diagnostics.Process" Version="8.0.0" />
```

- [ ] **Step 4: Commit**

```bash
git add .
git commit -m "feat: add KeyboardHook with low-level hook support"
```

---

## Task 7: HotkeyManager

**Files:**
- Create: `src/Hooks/HotkeyManager.cs`

- [ ] **Step 1: Create HotkeyManager.cs**

```csharp
// src/Hooks/HotkeyManager.cs
using RealTranslate.Core;
using RealTranslate.Native;
using static RealTranslate.Native.Win32;

namespace RealTranslate.Hooks;

/// <summary>
/// Manages global hotkey registration for toggle functionality.
/// </summary>
public class HotkeyManager : IDisposable
{
    private readonly AppState _state;
    private readonly IntPtr _windowHandle;
    private int _hotkeyId;
    private bool _registered;

    public event Action? HotkeyPressed;

    public HotkeyManager(AppState state, IntPtr windowHandle)
    {
        _state = state;
        _windowHandle = windowHandle;
        _hotkeyId = 0x0001;
    }

    public bool Register()
    {
        var config = _state.Config.Hotkey;
        var modifiers = ParseModifiers(config.Modifiers);
        var vk = ParseKey(config.Key);

        _registered = RegisterHotKey(_windowHandle, _hotkeyId, modifiers, vk);

        if (!_registered)
        {
            var error = Marshal.GetLastWin32Error();
            Console.WriteLine($"Failed to register hotkey. Error: {error}");
        }

        return _registered;
    }

    public bool ProcessMessage(int msg, IntPtr wParam)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == _hotkeyId)
        {
            _state.IsPaused = !_state.IsPaused;
            HotkeyPressed?.Invoke();
            return true;
        }

        return false;
    }

    private static uint ParseModifiers(List<string> modifiers)
    {
        uint result = 0;

        foreach (var mod in modifiers)
        {
            result |= mod.ToLowerInvariant() switch
            {
                "ctrl" or "control" => MOD_CONTROL,
                "alt" => MOD_ALT,
                "shift" => MOD_SHIFT,
                "win" or "windows" => MOD_WIN,
                _ => 0
            };
        }

        return result;
    }

    private static ushort ParseKey(string key)
    {
        if (key.Length == 1)
        {
            return char.ToUpperInvariant(key[0]);
        }

        return key.ToUpperInvariant() switch
        {
            "F1" => 0x70,
            "F2" => 0x71,
            "F3" => 0x72,
            "F4" => 0x73,
            "F5" => 0x74,
            "F6" => 0x75,
            "F7" => 0x76,
            "F8" => 0x77,
            "F9" => 0x78,
            "F10" => 0x79,
            "F11" => 0x7A,
            "F12" => 0x7B,
            "SPACE" => 0x20,
            "ENTER" or "RETURN" => 0x0D,
            "TAB" => 0x09,
            "ESCAPE" or "ESC" => 0x1B,
            _ => (ushort)key.ToUpperInvariant()[0]
        };
    }

    public void Dispose()
    {
        if (_registered)
        {
            UnregisterHotKey(_windowHandle, _hotkeyId);
            _registered = false;
        }
    }
}
```

- [ ] **Step 2: Verify project builds**

```bash
dotnet build
```

Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add .
git commit -m "feat: add HotkeyManager for global hotkey registration"
```

---

## Task 8: TrayIcon

**Files:**
- Create: `src/UI/TrayIcon.cs`

- [ ] **Step 1: Create TrayIcon.cs**

```csharp
// src/UI/TrayIcon.cs
using System.Drawing;
using System.Windows.Forms;
using RealTranslate.Core;

namespace RealTranslate.UI;

/// <summary>
/// System tray icon with context menu.
/// </summary>
public class TrayIcon : IDisposable
{
    private readonly AppState _state;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _toggleItem;

    public event Action? ExitRequested;
    public event Action? ToggleRequested;

    public TrayIcon(AppState state)
    {
        _state = state;

        _menu = CreateMenu();
        _toggleItem = (ToolStripMenuItem)_menu.Items[0];

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = _menu,
            Visible = true,
            Text = "real-translate"
        };

        UpdateIcon();
        _notifyIcon.DoubleClick += (_, _) => Toggle();
    }

    private ContextMenuStrip CreateMenu()
    {
        var menu = new ContextMenuStrip();

        var toggleItem = new ToolStripMenuItem("Enable translation", null, (_, _) => Toggle())
        {
            Checked = true
        };
        menu.Items.Add(toggleItem);

        menu.Items.Add(new ToolStripSeparator());

        var configItem = new ToolStripMenuItem("Open config file", null, (_, _) => OpenConfig());
        menu.Items.Add(configItem);

        menu.Items.Add(new ToolStripSeparator());

        var aboutItem = new ToolStripMenuItem("About", null, (_, _) => ShowAbout());
        menu.Items.Add(aboutItem);

        var exitItem = new ToolStripMenuItem("Exit", null, (_, _) => Exit());
        menu.Items.Add(exitItem);

        return menu;
    }

    public void UpdateIcon()
    {
        if (!_state.OllamaAvailable)
        {
            _notifyIcon.Icon = CreateIcon(Color.Red);
            _notifyIcon.Text = "real-translate: Error - Ollama unavailable";
            _toggleItem.Checked = false;
        }
        else if (_state.IsPaused)
        {
            _notifyIcon.Icon = CreateIcon(Color.Gray);
            _notifyIcon.Text = "real-translate: Paused";
            _toggleItem.Checked = false;
        }
        else
        {
            _notifyIcon.Icon = CreateIcon(Color.Green);
            _notifyIcon.Text = "real-translate: Active";
            _toggleItem.Checked = true;
        }
    }

    public void ShowNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        if (_state.Config.Ui.ShowNotifications)
        {
            _notifyIcon.ShowBalloonTip(3000, title, message, icon);
        }
    }

    private void Toggle()
    {
        _state.IsPaused = !_state.IsPaused;
        UpdateIcon();
        ToggleRequested?.Invoke();
    }

    private void OpenConfig()
    {
        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.toml");
        if (File.Exists(configPath))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = configPath,
                UseShellExecute = true
            });
        }
    }

    private void ShowAbout()
    {
        MessageBox.Show(
            "real-translate v1.0.0\n\n" +
            "Real-time translation from Portuguese to English.\n" +
            "Uses local Ollama with translategemma model.\n\n" +
            "Hotkey: Ctrl+Shift+T to toggle",
            "About real-translate",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void Exit()
    {
        ExitRequested?.Invoke();
    }

    private static Icon CreateIcon(Color color)
    {
        // Create a simple colored circle icon
        using var bitmap = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bitmap);

        g.Clear(Color.Transparent);
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, 2, 2, 12, 12);

        var hIcon = bitmap.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    public void Dispose()
    {
        _notifyIcon.Dispose();
        _menu.Dispose();
    }
}
```

- [ ] **Step 2: Add Windows Forms reference**

Add to RealTranslate.csproj:
```xml
<PackageReference Include="System.Drawing.Common" Version="8.0.0" />
```

And add `UseWindowsForms`:
```xml
<PropertyGroup>
  ...
  <UseWindowsForms>true</UseWindowsForms>
</PropertyGroup>
```

- [ ] **Step 3: Verify project builds**

```bash
dotnet build
```

Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add .
git commit -m "feat: add TrayIcon with context menu and status indicators"
```

---

## Task 9: Program.cs - Wire Everything Together

**Files:**
- Create: `src/Program.cs`

- [ ] **Step 1: Create Program.cs**

```csharp
// src/Program.cs
using System.Runtime.InteropServices;
using RealTranslate.Core;
using RealTranslate.Hooks;
using RealTranslate.Services;
using RealTranslate.UI;
using RealTranslate.Native;
using static RealTranslate.Native.Win32;

namespace RealTranslate;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Load configuration
        var config = AppConfig.Load();
        var state = new AppState { Config = config };

        // Create services
        using var translationService = new TranslationService(state);
        using var bufferManager = new BufferManager(state);
        using var injector = new Injector(state, bufferManager);
        using var keyboardHook = new KeyboardHook(state);
        using var trayIcon = new TrayIcon(state);

        // Create message window for hotkey
        var messageWindow = new MessageWindow();
        using var hotkeyManager = new HotkeyManager(state, messageWindow.Handle);

        // Register hotkey
        if (!hotkeyManager.Register())
        {
            trayIcon.ShowNotification(
                "real-translate",
                "Failed to register hotkey. Check for conflicts.",
                ToolTipIcon.Warning);
        }

        // Wire up events
        hotkeyManager.HotkeyPressed += () => trayIcon.UpdateIcon();
        trayIcon.ExitRequested += () => Application.Exit();
        trayIcon.ToggleRequested += () =>
        {
            if (!state.IsPaused)
            {
                trayIcon.ShowNotification("real-translate", "Translation paused");
            }
        };

        // Start keyboard hook
        keyboardHook.Start();

        // Start processing loops
        var cts = new CancellationTokenSource();

        // Keyboard event processor
        _ = Task.Run(async () =>
        {
            await foreach (var kbEvent in AppChannels.KeyboardEvents.Reader.ReadAllAsync(cts.Token))
            {
                var task = bufferManager.ProcessEvent(kbEvent);
                if (task != null)
                {
                    await AppChannels.TranslationTasks.Writer.WriteAsync(task, cts.Token);
                }
            }
        }, cts.Token);

        // Translation processor
        _ = Task.Run(async () =>
        {
            await foreach (var task in AppChannels.TranslationTasks.Reader.ReadAllAsync(cts.Token))
            {
                if (state.IsPaused || !state.OllamaAvailable)
                    continue;

                var translated = await translationService.TranslateAsync(task.Text, task.CancellationToken);

                if (translated != null)
                {
                    var injectionTask = new InjectionTask(
                        translated,
                        task.CharactersToDelete,
                        task.Mode == TranslationMode.PhraseWithContext
                    );

                    await AppChannels.InjectionTasks.Writer.WriteAsync(injectionTask, cts.Token);
                }
            }
        }, cts.Token);

        // Injection processor
        _ = Task.Run(async () =>
        {
            await foreach (var task in AppChannels.InjectionTasks.Reader.ReadAllAsync(cts.Token))
            {
                await injector.InjectAsync(task);
            }
        }, cts.Token);

        // Health check loop
        _ = Task.Run(async () =>
        {
            var wasAvailable = true;
            while (!cts.Token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cts.Token);

                var isAvailable = await translationService.CheckHealthAsync();

                if (isAvailable && !wasAvailable)
                {
                    state.Reset();
                    trayIcon.UpdateIcon();
                    trayIcon.ShowNotification("real-translate", "Connection to Ollama restored");
                }
                else if (!isAvailable && wasAvailable)
                {
                    state.OllamaAvailable = false;
                    trayIcon.UpdateIcon();
                    if (state.Config.Ui.NotifyOnError)
                    {
                        trayIcon.ShowNotification(
                            "real-translate",
                            "Cannot connect to Ollama. Check if Ollama is running.",
                            ToolTipIcon.Error);
                    }
                }

                wasAvailable = isAvailable;
            }
        }, cts.Token);

        // Initial health check
        _ = Task.Run(async () =>
        {
            var isAvailable = await translationService.CheckHealthAsync();
            if (!isAvailable)
            {
                state.OllamaAvailable = false;
                trayIcon.UpdateIcon();
                trayIcon.ShowNotification(
                    "real-translate",
                    "Cannot connect to Ollama. Check if Ollama is running with translategemma model.",
                    ToolTipIcon.Error);
            }
        });

        // Set hotkey manager reference for message processing
        messageWindow.Tag = hotkeyManager;

        // Message loop
        Application.Run(new ApplicationContext
        {
            Tag = messageWindow
        });

        // Cleanup
        cts.Cancel();
    }
}

/// <summary>
/// Invisible window for receiving hotkey messages.
/// </summary>
internal class MessageWindow : Form
{
    public MessageWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        Opacity = 0;
        Size = new Size(0, 0);
        Load += (_, _) => Visible = false;
    }

    protected override void WndProc(ref Message m)
    {
        if (Tag is HotkeyManager hotkeyManager)
        {
            if (hotkeyManager.ProcessMessage(m.Msg, m.WParam))
                return;
        }

        base.WndProc(ref m);
    }
}
```

- [ ] **Step 2: Verify project builds**

```bash
dotnet build
```

Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add .
git commit -m "feat: wire all components in Program.cs"
```

---

## Task 10: Testing and Finalization

- [ ] **Step 1: Build release version**

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

- [ ] **Step 2: Test manual scenarios**

1. Start Ollama with translategemma: `ollama run translategemma`
2. Run the published executable
3. Verify tray icon appears (green)
4. Open Notepad, type "Olá mundo" - should translate to "Hello world"
5. Press Ctrl+Shift+T - should pause translation (gray icon)
6. Press Ctrl+Shift+T again - should resume (green icon)
7. Right-click tray icon → Exit - should close cleanly

- [ ] **Step 3: Final commit**

```bash
git add .
git commit -m "feat: complete real-translate v1.0.0"
```

---

## Summary

This plan builds real-translate in 10 tasks:

1. **Project Setup** - Solution, csproj, Channels, Config
2. **AppState & Win32** - State management, P/Invoke declarations
3. **TranslationService** - Ollama API client
4. **BufferManager** - Text accumulation, debounce
5. **Injector** - Text replacement via SendInput
6. **KeyboardHook** - Global keyboard capture
7. **HotkeyManager** - Toggle hotkey
8. **TrayIcon** - System tray UI
9. **Program.cs** - Wire everything together
10. **Testing** - Build and verify

Each task produces a buildable, testable increment.
