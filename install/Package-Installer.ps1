param(
    [string]$Version
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$assemblyInfoPath = Join-Path $repoRoot "src\AssemblyInfo.cs"
$installerScriptPath = Join-Path $repoRoot "installer\PrintBridge.iss"
$distPath = Join-Path $repoRoot "dist"

if (-not $Version) {
    $assemblyInfo = Get-Content -LiteralPath $assemblyInfoPath -Raw
    if ($assemblyInfo -match 'AssemblyFileVersion\("([^"]+)"\)') {
        $Version = $Matches[1] -replace '\.0$', ''
    }
    else {
        $Version = "0.1.0"
    }
}

& (Join-Path $PSScriptRoot "Build-Addin.ps1") -Configuration Release

$innoCandidates = @()
$isccCommand = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
if ($isccCommand) {
    $innoCandidates += $isccCommand.Source
}

if (${env:ProgramFiles(x86)}) {
    $innoCandidates += (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe")
}

if ($env:ProgramFiles) {
    $innoCandidates += (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
}

$iscc = $innoCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "Inno Setup 6 compiler (ISCC.exe) was not found. Install Inno Setup 6, then run this script again."
}

New-Item -ItemType Directory -Force -Path $distPath | Out-Null

& $iscc "/DAppVersion=$Version" $installerScriptPath

$installerPath = Join-Path $distPath "PrintBridgeSetup-$Version.exe"
Write-Host "Installer created: $installerPath"
