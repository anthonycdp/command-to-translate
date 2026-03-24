# Cursor-Aware Keystroke Buffer

**Date:** 2026-03-24
**Status:** Approved

## Problem

The `BufferManager` is an append-only keystroke recorder with no concept of cursor position. This causes two bugs in Electron/xterm.js terminals (the only hosts that use the keystroke buffer path):

1. **Mid-string edits are ignored.** Navigation keys (arrows, Home, End) are discarded by `KeyboardHook.IsIgnoredKey()`. Backspace always removes from the end of the `StringBuilder`, not from the cursor position. The buffer diverges from the actual terminal input after any cursor movement.

2. **Paste events are not captured.** `LowLevelKeyboardProc` early-returns when `ctrlPressed` is true, so Ctrl+V / Ctrl+Shift+V never reach the buffer. Pasted text is invisible to the translation flow.

Both bugs are isolated to the `ElectronTerminalAdapter` path. Clipboard-based adapters (GenericTextField, WindowsTerminal, ClassicConsole) are unaffected.

Clipboard-based capture is not viable for these terminals because Ctrl+C is intercepted as SIGINT and Ctrl+Shift+C does not reliably copy.

## Solution: Cursor-Tracking Buffer

Evolve `BufferManager` from append-only to a line-editor model with cursor position tracking.

### Coverage

Handles: typed characters, backspace, delete, arrow navigation, Home/End, paste (Ctrl+V, Ctrl+Shift+V).

Does not handle: mouse-based cursor repositioning, readline shortcuts (Ctrl+W, Ctrl+A, Ctrl+E), terminal-specific selection. Up/Down arrows (shell history) reset the buffer since the recalled command is unknowable.

Estimated coverage of real editing scenarios: ~90%.

## Design

### 1. Data Model Changes

#### `KbEventType` (KeyboardCapture.cs)

New values added to the existing enum:

```
Existing:  Char, Space, Punctuation, Enter, Backspace
New:       CursorLeft, CursorRight, Home, End, Delete, Paste, HistoryNavigation
```

#### `KbEvent` (KeyboardCapture.cs)

Add optional `Text` field for paste content:

```csharp
public readonly record struct KbEvent(
    char? Character,
    KbEventType Type,
    IntPtr WindowHandle,
    bool IsPasswordField,
    string? Text = null);
```

The default value preserves backward compatibility with existing call sites.

### 2. BufferManager Redesign (BufferManager.cs)

#### New field

```csharp
private int _cursorPosition;
```

Initialized to 0, reset alongside `_currentPhrase` in `ResetState()`.

#### Operation semantics

| Event | Before (append-only) | After (cursor-aware) |
|---|---|---|
| Char / Space / Punctuation | `Append()` at end | `Insert()` at `_cursorPosition`, advance cursor |
| Backspace | Remove last char | Remove at `_cursorPosition - 1`, decrement cursor |
| Delete | (ignored) | Remove at `_cursorPosition` (forward delete) |
| CursorLeft / CursorRight | (ignored) | `_cursorPosition ± 1`, clamped to `[0, Length]` |
| Home / End | (ignored) | `_cursorPosition = 0` / `_cursorPosition = Length` |
| Paste | (ignored) | `Insert(text)` at `_cursorPosition`, advance by `text.Length` |
| HistoryNavigation (Up/Down) | (ignored) | `ResetState()` — buffer is invalidated |
| Enter | `ResetState()` | No change |

#### `_currentWord` tracking

`RebuildCurrentWord()` scans backward from `_cursorPosition` (not from the end of `_currentPhrase`) to find the current word boundary.

#### `ConsumeCurrentPhrase` signature change

```csharp
public (string Phrase, int CharacterCount, int CursorPosition) ConsumeCurrentPhrase()
```

Returns the cursor position so the adapter can decide how to erase the source text.

### 3. KeyboardHook Changes (KeyboardHook.cs)

#### `IsIgnoredKey`

Remove from the ignored set:
- Left (`0x25`), Right (`0x27`) — cursor movement
- Home (`0x24`), End (`0x23`) — cursor jump
- Up (`0x26`), Down (`0x28`) — history navigation (generates `HistoryNavigation`)
- Delete (`0x2E`) — forward delete

Still ignored: Page Up (`0x21`), Page Down (`0x22`), Insert (`0x2D`), Escape (`0x1B`), Tab (`0x09`).

#### `LowLevelKeyboardProc` — paste detection

Before the `ctrlPressed || altPressed` early-return, add a check:

```
if ctrlPressed and vk == 0x56 (V) and not altPressed:
    read clipboard text via Clipboard.GetText() (safe — hook thread is STA)
    emit KbEvent(type: Paste, text: clipboardContent)
    skip the early-return
```

This handles both Ctrl+V and Ctrl+Shift+V (shift state is irrelevant for this detection).

All other Ctrl/Alt combinations remain ignored.

#### `ConvertToKbEvent`

New switch cases for: Left, Right, Home, End, Delete, Up, Down → corresponding `KbEventType` values.

### 4. ElectronTerminalAdapter Changes (TranslationTargetAdapters.cs)

#### `ReplaceSelectionAsync`

The cursor may not be at the end of the text when the hotkey fires. Updated sequence:

1. Send **End** key (move cursor to end of input line)
2. Send **Backspace × `sourceText.Length`** (erase all source text)
3. Send **Ctrl+Shift+V** (paste translation from clipboard)

Step 1 is the only addition — it ensures backspacing erases the correct number of characters regardless of cursor position.

### 5. OnDemandTranslationCoordinator

`CaptureSourceAsync` — adapt to the new `ConsumeCurrentPhrase` return type (destructure the added `CursorPosition` field). No behavioral change needed here; the cursor position is consumed by the adapter.

## Files Changed

| File | Change |
|---|---|
| `src/Core/KeyboardCapture.cs` | Add enum values, add `Text` field to `KbEvent` |
| `src/Services/BufferManager.cs` | Add `_cursorPosition`, rewrite all operations to cursor-aware semantics |
| `src/Hooks/KeyboardHook.cs` | Update `IsIgnoredKey`, add paste detection, extend `ConvertToKbEvent` |
| `src/Services/TranslationTargetAdapters.cs` | Add End key before backspace sequence in `ElectronTerminalAdapter` |
| `src/Services/OnDemandTranslationCoordinator.cs` | Adapt `ConsumeCurrentPhrase` destructuring |
| `tests/CommandToTranslate.Tests/BufferManagerTests.cs` | New tests for cursor operations, paste, history reset |

## Testing Strategy

Unit tests for `BufferManager`:
- Insert at middle of string via cursor movement
- Backspace/Delete at cursor position
- Home/End cursor jumps followed by edits
- Paste inserts text at cursor position
- History navigation (Up/Down) resets buffer
- Cursor clamping (Left at 0, Right at Length)
- `ConsumeCurrentPhrase` returns correct cursor position

Existing `BufferManagerTests` must continue passing (append-at-end is a special case where cursor is always at the end).
