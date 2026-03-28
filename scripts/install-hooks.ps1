# Install/uninstall Command Central hooks into Claude Code settings.
# Run from PowerShell on Windows. Delegates to WSL bash script if settings.json
# is a cross-filesystem symlink that PowerShell can't follow.
#
# Usage:
#   pwsh install-hooks.ps1                    # Install hooks
#   pwsh install-hooks.ps1 -Action check      # Check if hooks are installed
#   pwsh install-hooks.ps1 -Action uninstall  # Remove hooks
#   pwsh install-hooks.ps1 -Port 8080         # Use custom daemon port

param(
    [string]$SettingsPath = "$env:USERPROFILE\.claude\settings.json",
    [string]$DaemonHost = "localhost",
    [int]$Port = 9000,
    [ValidateSet("install", "check", "uninstall")]
    [string]$Action = "install"
)

$ErrorActionPreference = "Stop"

# --- WSL delegation ---

function Invoke-WslFallback {
    param([string]$Reason)

    Write-Host "$Reason Delegating to WSL bash script..."

    if (-not (Get-Command wsl -ErrorAction SilentlyContinue)) {
        Write-Error "Cannot read settings file and WSL is not available. No fallback possible."
        exit 1
    }

    # Resolve the bash script path relative to this script
    $bashScript = Join-Path $PSScriptRoot "install-hooks.sh"
    if (-not (Test-Path $bashScript)) {
        Write-Error "Bash script not found at $bashScript"
        exit 1
    }

    $wslPath = wsl wslpath -u ($bashScript -replace '\\', '/')
    $wslArgs = @()
    if ($Action -eq "check") { $wslArgs += "--check" }
    if ($Action -eq "uninstall") { $wslArgs += "--uninstall" }
    if ($Port -ne 9000) { $wslArgs += "--port"; $wslArgs += "$Port" }

    wsl bash $wslPath @wslArgs
    exit $LASTEXITCODE
}

function Test-CanReadFile {
    param([string]$Path)

    if (-not (Test-Path $Path)) { return $true }  # File doesn't exist yet — we can create it

    try {
        $item = Get-Item $Path -Force
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            # It's a symlink — try reading through it
            $null = Get-Content $Path -Raw -ErrorAction Stop
        }
        return $true
    } catch {
        return $false
    }
}

# Check upfront if we can work with the settings file
if (-not (Test-CanReadFile $SettingsPath)) {
    Invoke-WslFallback "Settings file is a cross-filesystem symlink."
}

# --- Hook template ---

$hookTemplatePath = Join-Path $PSScriptRoot "..\hooks\claude-hooks.json"
if (-not (Test-Path $hookTemplatePath)) {
    Write-Error "Hook template not found at $hookTemplatePath"
    exit 1
}

# --- Helpers ---

function Get-Settings {
    if (Test-Path $SettingsPath) {
        try {
            return Get-Content $SettingsPath -Raw -ErrorAction Stop | ConvertFrom-Json
        } catch {
            Invoke-WslFallback "Failed to read settings file."
        }
    }
    return [PSCustomObject]@{}
}

function Save-Settings {
    param($Settings)

    $settingsDir = Split-Path $SettingsPath
    if (-not (Test-Path $settingsDir)) {
        New-Item -ItemType Directory -Path $settingsDir -Force | Out-Null
    }

    try {
        $Settings | ConvertTo-Json -Depth 10 | Set-Content $SettingsPath -Encoding UTF8 -ErrorAction Stop
    } catch {
        Invoke-WslFallback "Failed to write settings file."
    }
}

function Test-IsCcHook {
    param($Entry)

    foreach ($hook in $Entry.hooks) {
        if ($hook.command -and $hook.command -match "localhost:\d+/hooks/") {
            return $true
        }
    }
    return $false
}

# --- Actions ---

function Invoke-Check {
    $settings = Get-Settings
    $hooks = $settings.hooks
    $expected = @("SessionStart", "Stop", "Notification", "UserPromptSubmit", "SessionEnd")
    $installed = @()
    $missing = @()

    foreach ($event in $expected) {
        $entries = $hooks.$event
        $found = $false
        if ($entries) {
            foreach ($entry in $entries) {
                if (Test-IsCcHook $entry) { $found = $true; break }
            }
        }
        if ($found) { $installed += $event } else { $missing += $event }
    }

    if ($missing.Count -gt 0) {
        Write-Host "Missing hooks: $($missing -join ', ')"
        if ($installed.Count -gt 0) {
            Write-Host "Installed hooks: $($installed -join ', ')"
        }
        exit 1
    } else {
        Write-Host "All hooks installed: $($installed -join ', ')"
    }
}

function Invoke-Install {
    $hookTemplate = Get-Content $hookTemplatePath -Raw
    $hookTemplate = $hookTemplate -replace "localhost:9000", "${DaemonHost}:${Port}"
    $newHooks = ($hookTemplate | ConvertFrom-Json).hooks

    # Backup existing settings
    if (Test-Path $SettingsPath) {
        Copy-Item $SettingsPath "$SettingsPath.bak" -Force
        Write-Host "Backed up settings to $SettingsPath.bak"
    }

    $settings = Get-Settings
    if (-not $settings.hooks) {
        $settings | Add-Member -NotePropertyName "hooks" -NotePropertyValue ([PSCustomObject]@{}) -Force
    }

    # For each hook event: remove existing CC hooks, add ours
    foreach ($event in ($newHooks | Get-Member -MemberType NoteProperty).Name) {
        $existingEntries = @($settings.hooks.$event)
        $filtered = @($existingEntries | Where-Object { $_ -and -not (Test-IsCcHook $_) })
        $filtered += @($newHooks.$event)
        $settings.hooks | Add-Member -NotePropertyName $event -NotePropertyValue $filtered -Force
    }

    Save-Settings $settings
    Write-Host "Hooks installed to $SettingsPath"
    Write-Host "Daemon endpoint: http://${DaemonHost}:${Port}"

    # Health check
    try {
        $null = Invoke-RestMethod -Uri "http://${DaemonHost}:${Port}/health" -TimeoutSec 2 -ErrorAction Stop
        Write-Host "Daemon is reachable at ${DaemonHost}:${Port}"
    } catch {
        Write-Host "WARNING: Daemon not reachable at ${DaemonHost}:${Port}"
        Write-Host "  Start the daemon before launching Claude Code sessions."
    }
}

function Invoke-Uninstall {
    $settings = Get-Settings
    if (-not $settings.hooks) {
        Write-Host "No hooks found in settings."
        return
    }

    $removed = 0
    foreach ($event in @(($settings.hooks | Get-Member -MemberType NoteProperty).Name)) {
        $entries = @($settings.hooks.$event)
        $originalCount = $entries.Count
        $filtered = @($entries | Where-Object { $_ -and -not (Test-IsCcHook $_) })
        $removed += $originalCount - $filtered.Count

        if ($filtered.Count -eq 0) {
            $settings.hooks.PSObject.Properties.Remove($event)
        } else {
            $settings.hooks | Add-Member -NotePropertyName $event -NotePropertyValue $filtered -Force
        }
    }

    $remainingHooks = ($settings.hooks | Get-Member -MemberType NoteProperty).Name
    if (-not $remainingHooks) {
        $settings.PSObject.Properties.Remove("hooks")
    }

    Save-Settings $settings
    Write-Host "Removed $removed Command Central hook entries from $SettingsPath"
}

# --- Main ---

switch ($Action) {
    "check"     { Invoke-Check }
    "install"   { Invoke-Install }
    "uninstall" { Invoke-Uninstall }
}
