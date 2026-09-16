[CmdletBinding()]
param(
    [string]$SourcePath = (Join-Path $PSScriptRoot '..\publish\win-x64'),
    [string]$InstallPath = (Join-Path $env:ProgramFiles 'KCxWare')
)

$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this installer from an elevated PowerShell window.'
}

$source = (Resolve-Path -LiteralPath $SourcePath).Path
foreach ($required in @('KCxWare.exe', 'KCxWare.Helper.exe', 'Assets\1.png', 'Assets\kcxparade.mp4')) {
    if (-not (Test-Path -LiteralPath (Join-Path $source $required))) { throw "Missing published file: $required" }
}

New-Item -ItemType Directory -Force -Path $InstallPath | Out-Null
Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $InstallPath -Recurse -Force

$shell = New-Object -ComObject WScript.Shell
$shortcuts = @(
    (Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'KCxWare.lnk'),
    (Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'KCxWare.lnk')
)
foreach ($shortcutPath in $shortcuts) {
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = Join-Path $InstallPath 'KCxWare.exe'
    $shortcut.WorkingDirectory = $InstallPath
    $shortcut.IconLocation = (Join-Path $InstallPath 'KCxWare.exe') + ',0'
    $shortcut.Description = 'KCxWare PC Mode Control'
    $shortcut.Save()
}

Write-Output "KCxWare installed to $InstallPath"
