# Installer Localization and Desktop Icon

**Status**: Approved
**Date**: 2026-03-24

## Summary

Add multi-language support to the Inno Setup installer using official translations, matching the languages supported by the application. Also add an optional desktop icon during installation.

## Requirements

1. Installer available in all 8 languages the application supports
2. Language auto-detected from Windows settings, with option to change
3. Desktop icon as optional task, checked by default

## Supported Languages

| Code     | Language                | Inno Setup File                           |
|----------|-------------------------|-------------------------------------------|
| en-US    | English (US)            | `compiler:Default.isl`                    |
| pt-BR    | Portuguese (Brazil)     | `compiler:Languages\BrazilianPortuguese.isl` |
| es-ES    | Spanish                 | `compiler:Languages\Spanish.isl`          |
| fr-FR    | French                  | `compiler:Languages\French.isl`           |
| de-DE    | German                  | `compiler:Languages\German.isl`           |
| ja-JP    | Japanese                | `compiler:Languages\Japanese.isl`         |
| zh-Hans  | Mandarin (Simplified)   | `compiler:Languages\ChineseSimplified.isl`|
| it-IT    | Italian                 | `compiler:Languages\Italian.isl`          |

## Implementation

### File: `installer/CommandToTranslate.iss`

#### 1. Update `[Languages]` section

Replace the single English entry with all 8 languages:

```iss
[Languages]
Name: "english";           MessagesFile: "compiler:Default.isl"
Name: "brazilian";         MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "spanish";           MessagesFile: "compiler:Languages\Spanish.isl"
Name: "french";            MessagesFile: "compiler:Languages\French.isl"
Name: "german";            MessagesFile: "compiler:Languages\German.isl"
Name: "japanese";          MessagesFile: "compiler:Languages\Japanese.isl"
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "italian";           MessagesFile: "compiler:Languages\Italian.isl"
```

Inno Setup automatically detects the Windows language and pre-selects the matching installer language.

#### 2. Add desktop icon task to `[Tasks]` section

```iss
[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce
Name: "startup"; Description: "Start with Windows"; GroupDescription: "Additional options:"; Flags: checkedonce
Name: "launchapp"; Description: "Launch command-to-translate"; GroupDescription: "Post-install:"; Flags: checkedonce
```

- `{cm:CreateDesktopIcon}` and `{cm:AdditionalIcons}` are built-in translated messages
- `checkedonce` means checked by default on first install, remembers user choice on updates

#### 3. Add desktop icon to `[Icons]` section

```iss
[Icons]
Name: "{autoprograms}\command-to-translate"; Filename: "{app}\command-to-translate.exe"
Name: "{autodesktop}\command-to-translate"; Filename: "{app}\command-to-translate.exe"; Tasks: desktopicon
```

- `{autodesktop}` resolves to the user's desktop folder
- `Tasks: desktopicon` ensures icon is only created when the task is selected

## Trade-offs

- **Using official translations**: Task descriptions like "Start with Windows" remain in English. This is acceptable as these are self-explanatory and the user already has context from the application. Custom translations can be added later via `[CustomMessages]` if needed.
