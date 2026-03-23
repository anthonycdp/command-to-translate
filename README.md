# command-to-translate

Windows tray app for translating text on demand from `pt-BR` to `en-US` using Ollama.

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
- `Hotkey.Modifiers` / `Hotkey.Key`: global hotkey
- `Ui.ShowNotifications`: enables tray notifications
- `Ui.NotifyOnError`: enables error notifications

## Usage

1. Start the app.
2. Type or select Portuguese text in a supported host.
3. Press `Ctrl + Shift + T` or your configured hotkey.
4. Wait for the text to be replaced with the translation.

Tray menu:

- `Enable hotkey translation`
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

- Translation direction is fixed to `pt-BR -> en-US`
- Windows only
- In some hosts, capture is limited to text before the cursor
- In Electron/TUI terminals, only text typed in the current session is available in the buffer
