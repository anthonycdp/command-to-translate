param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src\CommandToTranslate.csproj"
$installerScript = Join-Path $repoRoot "installer\CommandToTranslate.iss"
$publishDir = Join-Path $repoRoot "artifacts\publish\$Runtime"
$installerOutputDir = Join-Path $repoRoot "artifacts\installer"

function Resolve-IsccPath {
    $command = Get-Command "iscc.exe" -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $candidatePaths = @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )

    foreach ($candidatePath in $candidatePaths) {
        if (Test-Path $candidatePath) {
            return $candidatePath
        }
    }

    $registryPaths = @(
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*"
    )

    foreach ($registryPath in $registryPaths) {
        $installLocation = Get-ItemProperty $registryPath -ErrorAction SilentlyContinue |
            Where-Object { $_.DisplayName -like "Inno Setup*" } |
            Select-Object -First 1 -ExpandProperty InstallLocation

        if (-not [string]::IsNullOrWhiteSpace($installLocation)) {
            $resolvedPath = Join-Path $installLocation "ISCC.exe"
            if (Test-Path $resolvedPath) {
                return $resolvedPath
            }
        }
    }

    throw "Inno Setup compiler (iscc.exe) was not found in PATH or standard install locations."
}

[xml]$projectXml = Get-Content $projectPath
$appVersion = $projectXml.Project.PropertyGroup.Version

if ([string]::IsNullOrWhiteSpace($appVersion)) {
    throw "Could not resolve <Version> from $projectPath."
}

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
New-Item -ItemType Directory -Force -Path $installerOutputDir | Out-Null

dotnet publish $projectPath `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -o $publishDir

$isccPath = Resolve-IsccPath

& $isccPath `
    "/DAppVersion=$appVersion" `
    "/DPublishDir=$publishDir" `
    "/DOutputDir=$installerOutputDir" `
    $installerScript
