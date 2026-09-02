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

function Remove-StartupFlagForUser {
    param([string]$Sid)

    try {
        [Microsoft.Win32.Registry]::Users.DeleteSubKey("$Sid\Software\SOLIDWORKS\AddInsStartup\$addinGuid", $false)
    }
    catch {
    }
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
    Write-Host "Opened an Administrator PowerShell window. Approve the UAC prompt to finish unregistering the add-in."
    return
}

Remove-StartupFlagForUser -Sid $UserSid

if (-not $AssemblyPath) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $AssemblyPath = Join-Path $repoRoot "src\bin\x64\$Configuration\SwPrototypeExporter.dll"
}

if (-not (Test-Path -LiteralPath $AssemblyPath)) {
    throw "Assembly not found: $AssemblyPath. Pass -AssemblyPath to the DLL that was registered."
}

$regAsm = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
if (-not (Test-Path -LiteralPath $regAsm)) {
    throw "RegAsm.exe not found: $regAsm"
}

& $regAsm $AssemblyPath /unregister
