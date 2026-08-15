# Publish Klub100Generator as a self-contained folder for Windows.
# Creates a versioned zip ready for distribution.
#
# The zip contains:
#   Start Klub100Generator.bat   (double-click this to launch)
#   Klub100Generator\            (subfolder with all app files)
#     Klub100Generator.exe
#     *.dll
#     tools\ffmpeg.exe, ffprobe.exe, yt-dlp.exe, deno.exe
#
# Recipients just unzip and double-click the bat file - no .NET install needed.

param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)

$project = "Klub100Generator.csproj"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# Read version from csproj
$csprojContent = Get-Content (Join-Path $scriptDir $project) -Raw
$versionMatch = [regex]::Match($csprojContent, '<ApplicationDisplayVersion>(.*?)</ApplicationDisplayVersion>')
$version = if ($versionMatch.Success) { $versionMatch.Groups[1].Value } else { "unknown" }

Write-Host "Publishing $project v$version ($Configuration, $Runtime, self-contained)..." -ForegroundColor Cyan

dotnet publish $project `
    -f net9.0-windows10.0.19041.0 `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:WindowsPackageType=None

if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed!" -ForegroundColor Red
    exit 1
}

$publishDir = Join-Path $scriptDir "bin\$Configuration\net9.0-windows10.0.19041.0\$Runtime\publish"
$exePath = Join-Path $publishDir "Klub100Generator.exe"

if (-not (Test-Path $exePath)) {
    Write-Host "Exe not found at expected location: $exePath" -ForegroundColor Red
    exit 1
}

# Copy tools/ folder alongside the exe
$toolsSource = Join-Path $scriptDir "tools"
$toolsDest = Join-Path $publishDir "tools"
if (Test-Path $toolsSource) {
    if (Test-Path $toolsDest) { Remove-Item $toolsDest -Recurse -Force }
    Copy-Item -Path $toolsSource -Destination $toolsDest -Recurse
    Write-Host "Copied tools\ to publish directory." -ForegroundColor Cyan
} else {
    Write-Host "WARNING: tools\ folder not found." -ForegroundColor Yellow
}

# Restructure: move everything into a Klub100Generator\ subfolder
# and create a launcher bat file at the top level
$appSubfolder = Join-Path $publishDir "Klub100Generator"
$tempDir = Join-Path $publishDir "_temp_move"

# Move all files into the subfolder
New-Item -ItemType Directory -Path $appSubfolder -Force | Out-Null
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

# Move everything except the subfolder itself and temp dir
Get-ChildItem $publishDir -File | Where-Object { $_.Directory.Name -eq $publishDir.Split('\')[-1] } | ForEach-Object {
    Move-Item $_.FullName -Destination $appSubfolder -Force
}
Get-ChildItem $publishDir -Directory | Where-Object { $_.Name -notin @("Klub100Generator", "_temp_move") } | ForEach-Object {
    Move-Item $_.FullName -Destination $appSubfolder -Force
}

Remove-Item $tempDir -Force -ErrorAction SilentlyContinue

# Create launcher bat file
$batPath = Join-Path $publishDir "Start Klub100Generator.bat"
$batContent = @"
@echo off
cd /d "%~dp0Klub100Generator"
start Klub100Generator.exe
"@
Set-Content -Path $batPath -Value $batContent -Encoding ASCII
Write-Host "Created launcher: Start Klub100Generator.bat" -ForegroundColor Cyan

$totalSize = [math]::Round((Get-ChildItem $publishDir -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB, 1)

Write-Host ""
Write-Host "Publish succeeded!" -ForegroundColor Green
Write-Host "  Version:   v$version" -ForegroundColor Yellow
Write-Host "  Total:     $totalSize MB (including tools)" -ForegroundColor Yellow
Write-Host "  Output:    $publishDir" -ForegroundColor Yellow
Write-Host ""

# Create a versioned zip for distribution
$zipName = "Klub100Generator-v$version-$Runtime.zip"
$zipPath = Join-Path $scriptDir "bin\$Configuration\$zipName"
Write-Host "Creating zip: $zipName" -ForegroundColor Cyan

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -CompressionLevel Optimal

$zipSize = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host ""
Write-Host "Done!" -ForegroundColor Green
Write-Host "  Zip:  $zipPath" -ForegroundColor Yellow
Write-Host "  Size: $zipSize MB" -ForegroundColor Yellow
Write-Host ""
Write-Host "Recipients unzip and double-click 'Start Klub100Generator.bat' - no .NET install needed." -ForegroundColor Cyan
