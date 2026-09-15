[CmdletBinding()]
param([string]$InstallPath = (Join-Path $env:ProgramFiles 'KCxWare'))

$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this uninstaller from an elevated PowerShell window.'
}

& schtasks.exe /Delete /F /TN 'KCxWare Apply Armed Mode' 2>$null
foreach ($shortcutPath in @(
    (Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'KCxWare.lnk'),
    (Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'KCxWare.lnk')
)) {
    if (Test-Path -LiteralPath $shortcutPath) { Remove-Item -LiteralPath $shortcutPath -Force }
}

if (Test-Path -LiteralPath $InstallPath) {
    $resolvedInstallPath = (Resolve-Path -LiteralPath $InstallPath).Path
    $programFilesPath = [System.IO.Path]::GetFullPath($env:ProgramFiles)
    if (-not $resolvedInstallPath.StartsWith($programFilesPath, [System.StringComparison]::OrdinalIgnoreCase) -or
        [System.IO.Path]::GetFileName($resolvedInstallPath) -ne 'KCxWare') {
        throw "Refusing to remove unexpected path: $resolvedInstallPath"
    }
    Remove-Item -LiteralPath $resolvedInstallPath -Recurse -Force
}

Write-Output 'KCxWare application files and shortcuts were removed. Recovery state under ProgramData was preserved.'
