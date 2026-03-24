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
- Ollama running locally (installed automatically by the installer)
- a translation model installed in Ollama (downloaded automatically by the installer)

Default settings:

- Endpoint: `http://127.0.0.1:11434`
- Model: `translategemma`

## Installation

The Windows installer handles all dependencies automatically:

1. **Ollama installation** — If Ollama is not installed, the installer downloads and installs it silently.
2. **Model download** — The `translategemma` model is downloaded during installation.
3. **Startup configuration** — Two scheduled tasks are created:
   - `LoadTranslateGemma` — Preloads the model at Windows logon (immediate)
   - `StartCommandToTranslate` — Starts the app with a 30-second delay, ensuring the model is ready

This startup orchestration eliminates race conditions where the app would start before the model is loaded in memory.

To build the installer:

```powershell
.\scripts\Build-Installer.ps1
```

This requires the Inno Setup compiler (`iscc.exe`) to be installed and available in `PATH`.

## Run (Development)

For development, start Ollama manually if needed:

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

Publish a self-contained release build:

```powershell
dotnet publish src\CommandToTranslate.csproj -c Release -r win-x64 --self-contained true -o artifacts\publish\win-x64
```

Published output:

```text
artifacts\publish\win-x64\
```

## Configuration

`config.toml` is created automatically on first start and lives in:

```text
%APPDATA%\command-to-translate\config.toml
```

If an older `config.toml` exists next to the executable, the app copies it to `%APPDATA%` on first start and keeps using the `%APPDATA%` copy afterwards.

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

1. Start the app (or let it start automatically with Windows).
2. Open `Select translation languages...` in the tray menu if needed.
3. Open `Change hotkey...` in the tray menu if you want a different shortcut.
4. Type or select text in a supported host.
5. Press your configured hotkey.
6. Wait for the text to be replaced with the translation.

When installed through the Windows installer:

- The app starts automatically with Windows via a scheduled task (not registry Run)
- A 30-second delay ensures the translation model is preloaded before the app starts
- The tray menu includes `Start with Windows` to toggle auto-start

Tray menu:

- `Enable hotkey translation`
- `Select translation languages...`
- `Change hotkey...`
- `Start with Windows`
- `Open config file`
- `About`
- `Exit`

## Tests

Run the full test suite:

```powershell
dotnet test command-to-translate.slnx
```

The repository also includes CI in `.github/workflows/dotnet.yml` for restore, build, and test on `main`.
Tagged releases and manual release runs use `.github/workflows/release.yml` to build a self-contained publish directory and a Windows installer artifact.

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
