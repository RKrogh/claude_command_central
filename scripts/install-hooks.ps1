# Install Command Central hooks into Claude Code settings
# Run from PowerShell on Windows

param(
    [string]$SettingsPath = "$env:USERPROFILE\.claude\settings.json",
    [string]$Host = "localhost",
    [int]$Port = 9000
)

$ErrorActionPreference = "Stop"

$hookTemplate = Get-Content "$PSScriptRoot\..\hooks\claude-hooks.json" -Raw
$hookTemplate = $hookTemplate -replace "localhost:9000", "${Host}:${Port}"
$hookConfig = $hookTemplate | ConvertFrom-Json

if (Test-Path $SettingsPath) {
    $settings = Get-Content $SettingsPath -Raw | ConvertFrom-Json
    Write-Host "Existing settings found at $SettingsPath"
} else {
    $settings = [PSCustomObject]@{}
    Write-Host "Creating new settings at $SettingsPath"
}

# Merge hooks into existing settings
$settings | Add-Member -NotePropertyName "hooks" -NotePropertyValue $hookConfig.hooks -Force

$settingsDir = Split-Path $SettingsPath
if (-not (Test-Path $settingsDir)) {
    New-Item -ItemType Directory -Path $settingsDir -Force | Out-Null
}

$settings | ConvertTo-Json -Depth 10 | Set-Content $SettingsPath -Encoding UTF8
Write-Host "Hooks installed successfully to $SettingsPath"
Write-Host "Daemon endpoint: http://${Host}:${Port}"
