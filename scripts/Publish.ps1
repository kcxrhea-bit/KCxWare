[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\publish\win-x64')
)

$ErrorActionPreference = 'Stop'
$resolvedRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$publishPath = [System.IO.Path]::GetFullPath($OutputPath)
if (-not $publishPath.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Publish output must remain inside the KCxWare repository.'
}

New-Item -ItemType Directory -Force -Path $publishPath | Out-Null
dotnet publish (Join-Path $resolvedRoot 'src\KCxWare\KCxWare.csproj') -c Release -r win-x64 --self-contained false --no-restore -o $publishPath
if ($LASTEXITCODE -ne 0) { throw 'KCxWare publish failed.' }
dotnet publish (Join-Path $resolvedRoot 'src\KCxWare.Helper\KCxWare.Helper.csproj') -c Release -r win-x64 --self-contained false --no-restore -o $publishPath
if ($LASTEXITCODE -ne 0) { throw 'KCxWare.Helper publish failed.' }

$required = @('KCxWare.exe', 'KCxWare.Helper.exe', 'Assets\1.png')
foreach ($relativePath in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishPath $relativePath))) {
        throw "Published artifact is missing $relativePath."
    }
}

Write-Output "Published KCxWare to $publishPath"
