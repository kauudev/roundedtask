$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$outDir = Join-Path $root "bin"
$srcDir = Join-Path $root "src"
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$wpfRefDir = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\WPF"

if (-not (Test-Path $csc)) {
    throw "C# compiler not found: $csc"
}

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$sources = Get-ChildItem -Path $srcDir -Filter *.cs | Sort-Object Name | ForEach-Object { $_.FullName }
$outExe = Join-Path $outDir "RoundedTask.exe"
$manifest = Join-Path $root "src\app.manifest"
$icon = Join-Path $root "assets\roundedtask.ico"

& $csc `
    /nologo `
    /target:winexe `
    "/out:$outExe" `
    "/win32manifest:$manifest" `
    "/win32icon:$icon" `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    /reference:System.Xml.dll `
    /reference:System.Xml.Linq.dll `
    "/reference:$(Join-Path $wpfRefDir 'UIAutomationClient.dll')" `
    "/reference:$(Join-Path $wpfRefDir 'UIAutomationTypes.dll')" `
    "/reference:$(Join-Path $wpfRefDir 'WindowsBase.dll')" `
    $sources

if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

$assetsDir = Join-Path $root "assets"
if (Test-Path $assetsDir) {
    $outAssetsDir = Join-Path $outDir "assets"
    if (Test-Path $outAssetsDir) {
        Remove-Item -LiteralPath $outAssetsDir -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $outAssetsDir | Out-Null
    Copy-Item -Path (Join-Path $assetsDir "*") -Destination $outAssetsDir -Recurse -Force
}

Write-Host "Built bin\RoundedTask.exe"
