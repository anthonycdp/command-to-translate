# Real-Time Translator - Design Document

**Date**: 2026-03-22
**Status**: Approved
**Platform**: Windows
**Technology**: C# / .NET 8

---

## Overview

### Objective

Create a Windows background utility that translates user typing in real-time from Brazilian Portuguese (pt-BR) to American English (en-US) using a local Ollama instance with the `translategemma` model.

### Key Principles

- **No frontend complexity** - tray icon only
- **Low resource consumption** - minimal memory/CPU footprint
- **Local-only** - no external APIs
- **Low latency** - real-time feel
- **Non-intrusive** - automatic translation without user intervention

---

## Architecture

### High-Level Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              real-translate                                  │
│                                                                              │
│  ┌─────────────┐   ┌─────────────┐   ┌─────────────┐   ┌─────────────┐      │
│  │ KeyboardHook│──▶│BufferManager│──▶│ Translation │──▶│  Injector   │      │
│  │             │   │             │   │  Service    │   │             │      │
│  └─────────────┘   └─────────────┘   └─────────────┘   └─────────────┘      │
│         │                 │                 │                 │              │
│         ▼                 ▼                 ▼                 ▼              │
│  ┌─────────────────────────────────────────────────────────────────┐        │
│  │                    Channel<KbEvent> / Channel<TranslationTask>  │        │
│  └─────────────────────────────────────────────────────────────────┘        │
│                                                                              │
│  ┌─────────────┐   ┌─────────────┐   ┌─────────────┐                        │
│  │ TrayIcon    │   │ HotkeyMgr   │   │ AppState    │                        │
│  │             │   │             │   │             │                        │
│  └─────────────┘   └─────────────┘   └─────────────┘                        │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
                          ┌─────────────────┐
                          │  Ollama API     │
                          │  localhost:11434│
                          └─────────────────┘
```

### Actor-Based Architecture with Channels

Each component runs independently and communicates via `System.Threading.Channels`:

| Channel | Producer | Consumer | Content |
|---------|----------|----------|---------|
| `Channel<KbEvent>` | KeyboardHook | BufferManager | Raw keyboard events |
| `Channel<TranslationTask>` | BufferManager | TranslationService | Text to translate |
| `Channel<InjectionTask>` | TranslationService | Injector | Translated text + metadata |

### Thread Model

| Thread | Purpose | Priority |
|--------|---------|----------|
| UI Thread | Tray icon, notifications, menu | Normal |
| Hook Thread | Windows keyboard hook (must be responsive) | High |
| Worker Pool | Async translation requests | Normal |
| Background | Debounce timers, health checks | Below Normal |

---

## Components

### 2.1 KeyboardHook

**Responsibility**: Capture all global keyboard events on Windows.

**Implementation Details**:
- Use `SetWindowsHookEx` with `WH_KEYBOARD_LL` (low-level keyboard hook)
- Run on dedicated thread with Windows message pump
- Filter system events (Ctrl, Alt, Shift alone, Windows shortcuts)
- Detect password fields via `GetWindowThreadProcessId` + UI Accessibility

**Data Structure**:
```csharp
public enum KbEventType { Char, Space, Punctuation, Enter, Backspace }

public record KbEvent(
    char? Character,        // null for non-printable keys
    KbEventType Type,
    IntPtr WindowHandle,    // for context detection
    bool IsPasswordField    // detected via accessibility API
);
```

**Password Field Detection**:
- Use `IAccessible` or UI Automation to check if focused field has `IsPassword = true`
- If password field: completely ignore the event (no capture, no buffer)

**Event Filtering**:
| Key/Combination | Action |
|-----------------|--------|
| Single char (a-z, 0-9, etc.) | Emit as `KbEventType.Char` |
| Space | Emit as `KbEventType.Space` |
| `. , ! ? ; :` | Emit as `KbEventType.Punctuation` |
| Enter | Emit as `KbEventType.Enter` |
| Backspace | Emit as `KbEventType.Backspace` |
| Ctrl/Alt/Win combinations | Ignore (system shortcuts) |
| Arrow keys, F-keys | Ignore |

---

### 2.2 BufferManager

**Responsibility**: Accumulate text, detect translation triggers, manage debounce.

**Internal State**:
```csharp
public class TextBuffer
{
    public StringBuilder CurrentWord { get; }       // word being typed
    public StringBuilder CurrentPhrase { get; }     // accumulated phrase
    public int CharactersSinceLastInject { get; set; }  // for deletion calculation
    public DateTime LastKeystroke { get; set; }     // for debounce
    public CancellationTokenSource? PendingTranslation { get; set; }
}
```

**Translation Triggers**:

| Trigger | Action | Mode |
|---------|--------|------|
| Space after word | Translate word immediately | `WordOnly` |
| Punctuation (.,!?) | Translate word + schedule context refinement | `WordOnly` → `PhraseWithContext` |
| Enter | Translate phrase + context refinement | `PhraseWithContext` |
| Debounce timeout (500ms) | Context refinement of current phrase | `PhraseWithContext` |
| Backspace | Adjust buffer, cancel pending translation if needed | - |

**Output Structure**:
```csharp
public enum TranslationMode { WordOnly, PhraseWithContext }

public record TranslationTask(
    string Text,
    TranslationMode Mode,
    int CharactersToDelete,
    CancellationToken CancellationToken
);
```

**Debounce Logic**:
```csharp
async Task OnDebounceTimer()
{
    await Task.Delay(Config.DebounceMs);

    if (Buffer.LastKeystroke.Age < Config.DebounceMs)
        return; // User is still typing

    if (Buffer.CurrentPhrase.Length > 0)
    {
        EmitTranslationTask(
            Buffer.CurrentPhrase.ToString(),
            TranslationMode.PhraseWithContext,
            charactersToDelete: 0
        );
    }
}
```

---

### 2.3 TranslationService

**Responsibility**: Communicate with Ollama, handle retries and timeouts.

**Ollama Configuration**:
```csharp
public class OllamaConfig
{
    public string Endpoint { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "translategemma";
    public int TimeoutMs { get; set; } = 2000;
    public double Temperature { get; set; } = 0.1;
    public bool Stream { get; set; } = false;
}
```

**System Prompt**:
```
Translate the user's text from Brazilian Portuguese to American English.
Rules:
- Output only the translated text
- Do not explain
- Do not add quotation marks
- Preserve meaning
- Keep names, URLs, emails, numbers, and code unchanged unless translation is necessary
- Return only the final translated text
```

**API Call**:
```
POST /api/chat
{
  "model": "translategemma",
  "messages": [
    {"role": "system", "content": "<system prompt>"},
    {"role": "user", "content": "<text to translate>"}
  ],
  "stream": false,
  "options": {"temperature": 0.1}
}
```

**Error Handling**:

| Error | Action |
|-------|--------|
| Timeout (2s) | Cancel, pass original text, mark failure |
| Connection refused | Ollama unavailable, notify once, pause translations |
| Model not found | Fatal error notification, disable translation |
| Empty/malformed response | Retry once, if fails pass original |

**Health Check**:
- Periodic ping to Ollama every 30 seconds when translations are active
- On recovery: show notification "Connection restored"

---

### 2.4 Injector

**Responsibility**: Remove original text and insert translation.

**Injection Mechanism**:
1. Calculate characters to delete (from BufferManager)
2. Set `AppState.IsInjecting = true`
3. Send N backspaces via `SendInput`
4. Wait 10ms
5. Send translated text via `SendInput`
6. Wait 50ms (loop protection)
7. Set `AppState.IsInjecting = false`

**Loop Prevention**:
- `AppState.IsInjecting` flag checked by KeyboardHook
- KeyboardHook ignores all events while flag is active
- Flag expires after 50ms from last injected character

**Contextual Refinement Injection**:
When refinement produces different text than word-by-word:
1. Calculate diff between current injected text and refined text
2. Backspace to beginning of difference
3. Inject corrected portion

**Example**:
```
Original word-by-word: "Hello how you"
Refined translation:   "Hello how are you"
Difference: "+are "
Action: Backspace 4 (" you"), inject "are you"
```

---

### 2.5 TrayIcon

**Responsibility**: Minimal visual interface via system tray.

**Icon States**:

| State | Icon Color | Tooltip |
|-------|------------|---------|
| Active | Green | "real-translate: Active" |
| Paused | Gray | "real-translate: Paused" |
| Ollama Error | Red | "real-translate: Error - Ollama unavailable" |

**Context Menu**:
```
┌─────────────────────────────┐
│ ▶ Enable translation        │  (toggle)
│ ─────────────────────────── │
│ ⚙ Open config file          │  (opens config.toml in notepad)
│ ─────────────────────────── │
│ ❓ About                     │
│ ✖ Exit                      │
└─────────────────────────────┘
```

**Notifications**:
- First Ollama connection failure: Toast notification
- Connection restored after failure: Toast notification
- Hotkey registration conflict: Toast notification on startup

---

### 2.6 HotkeyManager

**Responsibility**: Register and detect global toggle hotkey.

**Default Configuration**:
- `Ctrl+Shift+T`
- Stored in config file, loaded at startup

**Implementation**:
```csharp
[DllImport("user32.dll")]
static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

// Modifiers: MOD_CONTROL (0x0002) | MOD_SHIFT (0x0004)
// Key: VK_T (0x54)
```

**Conflict Handling**:
- If hotkey already registered by another app: show notification
- User can configure alternative via config file

---

### 2.7 AppState

**Responsibility**: Shared global state across components.

```csharp
public class AppState
{
    public volatile bool IsPaused;           // Translation paused via hotkey/menu
    public volatile bool IsInjecting;        // Prevents capture loop
    public volatile bool OllamaAvailable;    // Connection status
    public DateTime? LastErrorNotification;  // Rate-limit notifications
    public Config Config { get; set; }
}
```

**Thread Safety**:
- `volatile` for simple boolean flags
- `lock` for complex state modifications

---

## Data Flow

### Word-by-Word Translation Flow

```
1. User types "Olá"
2. User presses Space
3. KeyboardHook → KbEvent(Char=' ', Type=Space)
4. BufferManager:
   - Detects completed word "Olá"
   - CharactersToDelete = 3
   - Emits TranslationTask("Olá", WordOnly, 3)
5. TranslationService:
   - Sends to Ollama
   - Receives "Hello"
6. Injector:
   - AppState.IsInjecting = true
   - Sends 3 backspaces
   - Sends "Hello "
   - Waits 50ms
   - AppState.IsInjecting = false
```

### Contextual Refinement Flow

```
1. User types "Olá como você"
2. Word-by-word resulted in "Hello how you"
3. User pauses for 500ms
4. BufferManager detects debounce timeout
5. Emits TranslationTask("Olá como você", PhraseWithContext, 0)
6. TranslationService returns "Hello how are you"
7. Injector:
   - Calculates diff: "how you" → "how are you"
   - Backspace to start of diff
   - Inserts "how are you"
```

---

## Configuration

**File**: `config.toml` (alongside executable)

```toml
[translation]
source_lang = "pt-BR"
target_lang = "en-US"

[ollama]
endpoint = "http://localhost:11434"
model = "translategemma"
timeout_ms = 2000
temperature = 0.1

[behavior]
debounce_ms = 500
inject_delay_ms = 10
loop_protection_ms = 50

[hotkey]
modifiers = ["Ctrl", "Shift"]
key = "T"

[ui]
show_notifications = true
notify_on_error = true
```

**Loading Behavior**:
- Create default config if not exists on startup
- Changes require application restart (no hot-reload for V1)

---

## File Structure

```
real-translate/
├── src/
│   ├── RealTranslate.csproj
│   ├── Program.cs                 # Entry point
│   │
│   ├── Core/
│   │   ├── AppState.cs
│   │   └── Config.cs
│   │
│   ├── Hooks/
│   │   ├── KeyboardHook.cs        # SetWindowsHookEx wrapper
│   │   └── HotkeyManager.cs       # RegisterHotKey wrapper
│   │
│   ├── Services/
│   │   ├── BufferManager.cs
│   │   ├── TranslationService.cs
│   │   └── Injector.cs
│   │
│   ├── UI/
│   │   └── TrayIcon.cs
│   │
│   └── Native/
│       └── Win32.cs               # P/Invoke declarations
│
├── real-translate.sln
└── config.toml                    # Generated on first run
```

---

## Decisions Log

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Language | C# / .NET 8 | First-class Windows integration, native tray support, easy P/Invoke |
| Translation mode | Word-by-word + contextual refinement | Balance between real-time feel and translation accuracy |
| Replacement behavior | Automatic on delimiter | No user intervention required |
| Debounce time | 500ms | Natural typing rhythm, good balance |
| Error handling | Notification + tray indicator | Visible feedback without notification fatigue |
| Hotkey | Configurable, default Ctrl+Shift+T | Flexibility for users, memorable default |
| Password exclusion | Auto-detect only | Security, no configuration needed |
| Architecture | Actor-based with Channels | Clean separation, testable, no external dependencies |

---

## Out of Scope (V1)

- Hot-reload of configuration
- GUI configuration editor
- Multiple language pairs
- Custom translation models
- Logging to file
- Auto-update mechanism
- Multiple hotkeys for different modes
