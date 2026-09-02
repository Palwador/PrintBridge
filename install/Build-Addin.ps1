param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$solidWorks = Get-Process -Name SLDWORKS -ErrorAction SilentlyContinue
if ($solidWorks) {
    throw "Close SOLIDWORKS before rebuilding. It is currently holding the add-in DLL open."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src\SwPrototypeExporter.csproj"
$msBuild = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"

if (-not (Test-Path -LiteralPath $msBuild)) {
    throw "MSBuild.exe not found: $msBuild"
}

& $msBuild $projectPath /p:Configuration=$Configuration /p:Platform=x64
