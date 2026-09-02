param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$AssemblyPath,

    [string]$UserSid
)

$ErrorActionPreference = "Stop"

$addinGuid = "{040C231A-2571-4FFC-894D-8D01C2530606}"
if (-not $UserSid) {
    $UserSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
}

function Set-StartupFlagForUser {
    param([string]$Sid)

    $startupKey = [Microsoft.Win32.Registry]::Users.CreateSubKey("$Sid\Software\SOLIDWORKS\AddInsStartup\$addinGuid")
    $startupKey.SetValue("", 1, [Microsoft.Win32.RegistryValueKind]::DWord)
    $startupKey.Close()
}

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $scriptPath = $MyInvocation.MyCommand.Path
    $arguments = @(
        "-NoExit",
        "-ExecutionPolicy", "Bypass",
        "-File", "`"$scriptPath`"",
        "-Configuration", $Configuration,
        "-UserSid", $UserSid
    )

    if ($AssemblyPath) {
        $arguments += @("-AssemblyPath", "`"$AssemblyPath`"")
    }

    Start-Process -FilePath "powershell.exe" -Verb RunAs -ArgumentList $arguments
    Write-Host "Opened an Administrator PowerShell window. Approve the UAC prompt to finish registering the add-in."
    return
}

if (-not $AssemblyPath) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $AssemblyPath = Join-Path $repoRoot "src\bin\x64\$Configuration\SwPrototypeExporter.dll"
}

if (-not (Test-Path -LiteralPath $AssemblyPath)) {
    throw "Assembly not found: $AssemblyPath. Build the project first."
}

$regAsm = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
if (-not (Test-Path -LiteralPath $regAsm)) {
    throw "RegAsm.exe not found: $regAsm"
}

& $regAsm $AssemblyPath /codebase

Set-StartupFlagForUser -Sid $UserSid
