param(
  [Parameter(Mandatory=$true)][string]$Configuration,
  [Parameter(Mandatory=$true)][string]$PublishDir,
  [Parameter(Mandatory=$true)][string]$OutDir
)

$ErrorActionPreference = "Stop"

function Ensure-File([string]$path) {
  if (-not (Test-Path $path)) {
    throw "Missing required file: $path"
  }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$toolsSrc = Join-Path $repoRoot "tools"
$presetsSrc = Join-Path $repoRoot "presets"
$licensesSrc = Join-Path $repoRoot "licenses"
$readmeSrc = Join-Path $repoRoot "README.txt"
$licenseSrc = Join-Path $repoRoot "LICENSE.txt"

Ensure-File (Join-Path $toolsSrc "ffmpeg.exe")
Ensure-File (Join-Path $toolsSrc "ffprobe.exe")
Ensure-File (Join-Path $toolsSrc "dvdauthor.exe")
Ensure-File (Join-Path $toolsSrc "xorriso.exe")
Ensure-File (Join-Path $presetsSrc "presets.json")
Ensure-File $readmeSrc
Ensure-File $licenseSrc
Ensure-File (Join-Path $licensesSrc "THIRD_PARTY_NOTICES.txt")

if (Test-Path $OutDir) { Remove-Item -Recurse -Force $OutDir }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $OutDir "tools") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $OutDir "presets") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $OutDir "licenses") | Out-Null

Copy-Item -Recurse -Force -Path (Join-Path $PublishDir "*") -Destination $OutDir
Copy-Item -Force -Path (Join-Path $toolsSrc "*") -Destination (Join-Path $OutDir "tools")
Copy-Item -Force -Path (Join-Path $presetsSrc "*") -Destination (Join-Path $OutDir "presets")
Copy-Item -Force -Path (Join-Path $licensesSrc "*") -Destination (Join-Path $OutDir "licenses")
Copy-Item -Force -Path $readmeSrc -Destination (Join-Path $OutDir "README.txt")
Copy-Item -Force -Path $licenseSrc -Destination (Join-Path $OutDir "LICENSE.txt")

# Zip
$zipPath = Join-Path $repoRoot ("artifacts\AVItoDVDISO_" + $Configuration + ".zip")
$zipDir = Split-Path -Parent $zipPath
New-Item -ItemType Directory -Force -Path $zipDir | Out-Null
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($OutDir, $zipPath)


Write-Host "Bundled to: $zipPath"
