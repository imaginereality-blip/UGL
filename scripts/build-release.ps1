# UGL release build script.
#
# Publishes a self-contained, single-file portable build and packages it into a
# release ZIP. Run from the repo root (where UGL.sln lives).
#
# Usage:
#   .\scripts\build-release.ps1 -Version 0.1.0

param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"

$publishDir = "publish\UGL"
$zipName    = "UGL-portable-v$Version.zip"
$zipPath    = "publish\$zipName"

Write-Host "== Cleaning previous publish output ==" -ForegroundColor Cyan
if (Test-Path "publish") { Remove-Item "publish" -Recurse -Force }

Write-Host "== Publishing (self-contained, single-file, win-x64) ==" -ForegroundColor Cyan
dotnet publish src\UGL.App\UGL.App.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:Version=$Version `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed - aborting before packaging anything." -ForegroundColor Red
    exit 1
}

# Strip anything that shouldn't ship in a public release. This covers two distinct
# risks in one pass: UGL.App.csproj currently copies a personal dev/test config\
# folder (..\..\config\, i.e. the repo root) into every build output via a
# <Content Include> rule meant for local dev convenience - that must never end up
# in a release. It also covers the case where this publish folder was ever
# run/tested from directly before packaging, which would have let
# AppFolderScaffolder create roms\/bios\/media\/etc. with real content in them.
# Strip only these specific known folder names (the same list GitHubUpdateService
# treats as user data when applying an update) - deliberately not a broad "anything
# unexpected" filter, since LibVLC (audio/video playback) ships native .dll files
# and a plugins\ folder here that a naive extension allowlist wouldn't recognize as
# legitimate and could silently delete.
$userDataFolders = "config", "roms", "emulators", "bios", "bezels", "addons", "retroarch", "logs", "media"
foreach ($folder in $userDataFolders) {
    $path = Join-Path $publishDir $folder
    if (Test-Path $path) {
        Write-Host "== Removing $folder\ from publish output ==" -ForegroundColor Yellow
        Remove-Item $path -Recurse -Force
    }
}

Write-Host "== Packaging $zipName ==" -ForegroundColor Cyan
Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -Force

Write-Host ""
Write-Host "Done: $zipPath" -ForegroundColor Green
Write-Host "Upload this file as the release asset on GitHub." -ForegroundColor Green
