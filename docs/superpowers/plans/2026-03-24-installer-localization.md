# Installer Localization and Desktop Icon Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add multi-language support and optional desktop icon to the Inno Setup installer.

**Architecture:** Modify the single installer script file (`CommandToTranslate.iss`) to include 8 language translations using Inno Setup's built-in message files, and add an optional desktop icon task.

**Tech Stack:** Inno Setup 6

---

## File Structure

| File | Action | Purpose |
|------|--------|---------|
| `installer/CommandToTranslate.iss` | Modify | Add languages and desktop icon |

---

### Task 1: Add Multi-Language Support

**Files:**
- Modify: `installer/CommandToTranslate.iss:31-32`

- [ ] **Step 1: Update `[Languages]` section with all 8 languages**

Replace the current `[Languages]` section (lines 31-32) with:

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

- [ ] **Step 2: Verify installer compiles**

Run: `.\scripts\Build-Installer.ps1`
Expected: Build succeeds, installer created in `artifacts\installer\`

---

### Task 2: Add Desktop Icon Task

**Files:**
- Modify: `installer/CommandToTranslate.iss:34-36`

- [ ] **Step 1: Add desktop icon task to `[Tasks]` section**

Add as the first entry in `[Tasks]` section:

```iss
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce
```

The full `[Tasks]` section should be:

```iss
[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce
Name: "startup"; Description: "Start with Windows"; GroupDescription: "Additional options:"; Flags: checkedonce
Name: "launchapp"; Description: "Launch command-to-translate"; GroupDescription: "Post-install:"; Flags: checkedonce
```

- [ ] **Step 2: Verify installer compiles**

Run: `.\scripts\Build-Installer.ps1`
Expected: Build succeeds

---

### Task 3: Add Desktop Icon Entry

**Files:**
- Modify: `installer/CommandToTranslate.iss:41-42`

- [ ] **Step 1: Add desktop icon to `[Icons]` section**

Add a second line to the `[Icons]` section:

```iss
[Icons]
Name: "{autoprograms}\command-to-translate"; Filename: "{app}\command-to-translate.exe"
Name: "{autodesktop}\command-to-translate"; Filename: "{app}\command-to-translate.exe"; Tasks: desktopicon
```

- [ ] **Step 2: Verify installer compiles**

Run: `.\scripts\Build-Installer.ps1`
Expected: Build succeeds

---

### Task 4: Commit Changes

- [ ] **Step 1: Commit all installer changes**

```bash
git add installer/CommandToTranslate.iss
git commit -m "feat(installer): add multi-language support and desktop icon option

- Add 8 languages (en, pt-BR, es, fr, de, ja, zh-Hans, it)
- Add optional desktop icon task, checked by default
- Language auto-detected from Windows settings"
```
