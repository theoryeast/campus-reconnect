$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
& (Join-Path $PSScriptRoot 'build.ps1') -Test
$dist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
# Use a new staging directory and an explicit allowlist. Never copy a user's settings or logs.
$stage = Join-Path $dist ('stage-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'out\CampusReconnect.exe') -Destination $stage
foreach ($name in @('README.md','LICENSE','settings.example.xml')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $stage }
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $stage 'CampusReconnect.exe')).FileVersion
$zip = Join-Path $dist "CampusReconnect-v$version-win-x64.zip"
Compress-Archive -LiteralPath (Get-ChildItem -LiteralPath $stage -File).FullName -DestinationPath $zip -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt') -Encoding ascii
Write-Output $zip
