# command-to-translate

Windows tray app for translating text on demand between supported languages using Ollama.

## What it does

When the global hotkey is pressed, the app captures the current selection or the text before the cursor, sends it to Ollama, replaces the original text with the translation, and restores the clipboard.

Supported hosts:

- generic text fields
- Windows Terminal
- classic Windows console (`conhost`)
- Electron/xterm.js terminals using a keystroke buffer

## Requirements

- Windows 10 or 11
- .NET SDK 9.0+ for development
- Ollama running locally
- a translation model installed in Ollama

Default settings:

- Endpoint: `http://127.0.0.1:11434`
- Model: `translategemma`

## Run

Start Ollama if needed:

```powershell
ollama serve
```

Pull the default model:

```powershell
ollama pull translategemma
```

Run the app:

```powershell
dotnet run --project src\CommandToTranslate.csproj
```

Publish a release build:

```powershell
dotnet publish src\CommandToTranslate.csproj -c Release -r win-x64 --self-contained false
```

Published output:

```text
src\bin\Release\net9.0-windows\win-x64\publish\
```

## Configuration

`config.toml` is created automatically on first start and lives next to the app output.

Example:

```toml
[Ollama]
Endpoint = "http://127.0.0.1:11434"
Model = "translategemma"
TimeoutMs = 10000
Temperature = 0.0
Stream = false
KeepAlive = "5m"

[Translation]
SourceLanguage = "pt-BR"
TargetLanguage = "en-US"

[Behavior]
ShortcutStepDelayMs = 35
ClipboardTimeoutMs = 800
HostSettleDelayMs = 60

[Hotkey]
Modifiers = ["Ctrl", "Shift"]
Key = "T"

[Ui]
ShowNotifications = true
NotifyOnError = true
```

Main settings:

- `Ollama.Endpoint`: Ollama base URL
- `Ollama.Model`: translation model name
- `Translation.SourceLanguage`: currently selected source language
- `Translation.TargetLanguage`: currently selected target language
- `Hotkey.Modifiers` / `Hotkey.Key`: global hotkey
- `Ui.ShowNotifications`: enables tray notifications
- `Ui.NotifyOnError`: enables error notifications

Supported languages in this version:

- `pt-BR` Portuguese (Brazil)
- `en-US` English (US)
- `es-ES` Spanish
- `fr-FR` French
- `de-DE` German
- `ja-JP` Japanese
- `zh-Hans` Mandarin (Simplified)
- `it-IT` Italian

The selected source/target pair is also used to build the prompt sent to Ollama, so changing the selection changes the translation direction immediately.

## Usage

1. Start the app.
2. Open `Select translation languages...` in the tray menu if needed.
3. Open `Change hotkey...` in the tray menu if you want a different shortcut.
4. Type or select text in a supported host.
5. Press your configured hotkey.
6. Wait for the text to be replaced with the translation.

Tray menu:

- `Enable hotkey translation`
- `Select translation languages...`
- `Change hotkey...`
- `Open config file`
- `About`
- `Exit`

## Tests

Run the full test suite:

```powershell
dotnet test command-to-translate.slnx
```

The repository also includes CI in `.github/workflows/dotnet.yml` for restore, build, and test on `main`.

## Logs

Logs are written to:

```text
%APPDATA%\command-to-translate\logs\
```

## Limitations

- Only supported languages in the built-in catalog can be selected
- Source and target languages must be different
- Windows only
- In some hosts, capture is limited to text before the cursor
- In Electron/TUI terminals, only text typed in the current session is available in the buffer
