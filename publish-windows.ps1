# Publish Klub100Generator as a self-contained folder for Windows.
# Creates a versioned zip ready for distribution.
#
# Output: bin\Release\net9.0-windows10.0.19041.0\win-x64\publish\
#         bin\Release\Klub100Generator-vX.Y.Z-win-x64.zip
#
# The zip contains:
#   Klub100Generator.exe    (self-contained, launches the app)
#   *.dll                   (.NET runtime + WinUI 3 + MAUI)
#   tools\ffmpeg.exe        (~185MB)
#   tools\yt-dlp.exe        (~18MB)
#   tools\deno.exe          (~93MB)
#
# Recipients just unzip and double-click the exe - no .NET install needed.
# Note: WinUI 3 does not support single-file publish, so a folder is required.

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
    Write-Host "WARNING: tools\ folder not found. yt-dlp and ffmpeg must be on the recipient's PATH." -ForegroundColor Yellow
}

$exeSize = [math]::Round((Get-Item $exePath).Length / 1MB, 1)
$totalSize = [math]::Round((Get-ChildItem $publishDir -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB, 1)

Write-Host ""
Write-Host "Publish succeeded!" -ForegroundColor Green
Write-Host "  Version:   v$version" -ForegroundColor Yellow
Write-Host "  Exe size:  $exeSize MB" -ForegroundColor Yellow
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
Write-Host "Recipients just unzip and run Klub100Generator.exe - no .NET install needed." -ForegroundColor Cyan
