# Download a Piper TTS voice model (sherpa-onnx bundle) for local TTS notifications.
# Run from PowerShell on Windows. Requires tar (built into Windows 10+).
#
# Usage:
#   pwsh download-tts-model.ps1                              # default voice
#   pwsh download-tts-model.ps1 -Voice en_US-amy-medium      # specific voice
#   pwsh download-tts-model.ps1 -TargetDir C:\models\tts     # custom directory
#   pwsh download-tts-model.ps1 -List                        # list known voices
#   pwsh download-tts-model.ps1 -Force                       # re-download

param(
    [string]$Voice = "en_US-lessac-medium",
    [string]$TargetDir = "",
    [switch]$List,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$BaseUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models"

# Voices auto-assigned by Command Central (slot 1 -> first, slot 2 -> second, ...)
$KnownVoices = @(
    "en_US-lessac-medium",
    "en_US-amy-medium",
    "en_US-arctic-medium",
    "en_US-danny-low",
    "en_US-joe-medium",
    "en_US-kathleen-low",
    "en_US-kusal-medium",
    "en_US-libritts_r-medium",
    "en_US-ryan-medium"
)

if ($List) {
    Write-Host "Voices auto-assigned per slot (download more for distinct per-instance voices):"
    $KnownVoices | ForEach-Object { Write-Host "  $_" }
    Write-Host ""
    Write-Host "Full catalog: https://github.com/k2-fsa/sherpa-onnx/releases/tag/tts-models (vits-piper-*)"
    exit 0
}

if (-not $TargetDir) {
    $projectRoot = Split-Path $PSScriptRoot -Parent
    $TargetDir = Join-Path $projectRoot "models\tts"
}

$bundle = "vits-piper-$Voice"
$destDir = Join-Path $TargetDir $bundle
$url = "$BaseUrl/$bundle.tar.bz2"

if ((Test-Path (Join-Path $destDir "$Voice.onnx")) -and -not $Force) {
    Write-Host "Voice '$Voice' already present at $destDir (use -Force to re-download)"
    exit 0
}

if (-not (Get-Command tar -ErrorAction SilentlyContinue)) {
    Write-Error "tar not found. It ships with Windows 10+; alternatively run the WSL script: bash scripts/download-tts-model.sh"
    exit 1
}

New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null
$archive = Join-Path ([IO.Path]::GetTempPath()) "$bundle.tar.bz2"

try {
    Write-Host "Downloading $bundle.tar.bz2 ..."
    Invoke-WebRequest -Uri $url -OutFile $archive

    Write-Host "Extracting to $TargetDir ..."
    tar -xjf $archive -C $TargetDir
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Extraction failed (tar exit code $LASTEXITCODE)"
        exit 1
    }
}
catch {
    Write-Error "Download failed: $url`nCheck the voice name with -List, or browse: https://github.com/k2-fsa/sherpa-onnx/releases/tag/tts-models"
    exit 1
}
finally {
    if (Test-Path $archive) { Remove-Item $archive -Force }
}

# Verify the expected layout
$modelOk = Test-Path (Join-Path $destDir "$Voice.onnx")
$tokensOk = Test-Path (Join-Path $destDir "tokens.txt")
$dataOk = Test-Path (Join-Path $destDir "espeak-ng-data")

if ($modelOk -and $tokensOk -and $dataOk) {
    Write-Host ""
    Write-Host "Done. Voice '$Voice' installed:"
    Write-Host "  $destDir"
    Write-Host ""
    Write-Host "The daemon picks it up on next start (Tts:NotificationEngine = SherpaOnnx)."
}
else {
    Write-Error "Extracted bundle is missing expected files in $destDir (need $Voice.onnx, tokens.txt, espeak-ng-data/)"
    exit 1
}
