param(
    [string]$Version = "0.1.23",
    [switch]$SkipSmokeTests
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

if (-not $SkipSmokeTests) {
    Write-Host "Running smoke tests..." -ForegroundColor Cyan
    dotnet run --project tools\SmokeTests\SmokeTests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw "Smoke tests failed." }
}

$dist = Join-Path $root "dist\MistShell-$Version-win-x64"
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }

Write-Host "Publishing $Version..." -ForegroundColor Cyan
dotnet publish src\MistShell\MistShell.csproj `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false `
    -o $dist
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

$data = Join-Path $dist "data"
if (Test-Path $data) { Remove-Item $data -Recurse -Force }

Copy-Item README.md, LICENSE, THIRD-PARTY-NOTICES.md, CHANGELOG.md $dist -Force
if (Test-Path docs) { Copy-Item docs $dist -Recurse -Force }

$zip = Join-Path $root "dist\MistShell-$Version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $dist "*") -DestinationPath $zip -CompressionLevel Optimal

Write-Host "Done: $zip" -ForegroundColor Green
