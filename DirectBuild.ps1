param(
    [string]$Output = (Join-Path $PSScriptRoot 'RGB Panel Direct.exe'),
    [string]$Module = (Join-Path $PSScriptRoot 'SmbusPIIX4.bin'),
    [string]$License = (Join-Path $PSScriptRoot 'LICENSE'),
    [string]$EmbeddedSource
)

$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$files = @('AssemblyInfo.cs', 'DirectPanel.cs', 'PanelVisuals.cs', 'ColorEditor.cs', 'LightingEffect.cs', 'LightingHardware.cs', 'MemoryBus.cs', 'WindowsHid.cs')
$sources = $files | ForEach-Object { Join-Path $PSScriptRoot $_ }
$notices = Join-Path $PSScriptRoot 'DirectNotices.txt'
$manifest = Join-Path $PSScriptRoot 'DirectPanel.manifest'
$icon = Join-Path $PSScriptRoot 'RgbPanel.ico'
$resources = @(
    "/resource:$Module,RgbPanel.SmbusPIIX4.bin",
    "/resource:$License,RgbPanel.GPL-2.0.txt",
    "/resource:$notices,RgbPanel.Notices.txt"
)
if ($EmbeddedSource) {
    $resources += "/resource:$EmbeddedSource,RgbPanel.Source.zip"
}

& $compiler /nologo /warnaserror /optimize+ /utf8output /codepage:65001 /langversion:5 /target:winexe /platform:x64 /main:RgbPanel.DirectProgram "/win32manifest:$manifest" "/win32icon:$icon" "/out:$Output" /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:Accessibility.dll $resources $sources
if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed (exit code $LASTEXITCODE)."
}

