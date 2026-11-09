param([switch]$Test)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$outDir = Join-Path $PSScriptRoot 'out'
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x compiler not found. Build on Windows 10/11.' }
$sources = @('CampusReconnect.cs','NetworkTests.cs','PortalAuth.cs','LiveVerification.cs','RecoveryRuntime.cs','BuildInfo.cs')
& $compiler /nologo /target:winexe "/out:$outDir\CampusReconnect.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Web.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
if ($Test) {
    if (Test-Path -LiteralPath (Join-Path $outDir '设置.xml')) { throw 'Tests require a clean output directory without personal settings.' }
    $testProcess = Start-Process -FilePath (Join-Path $outDir 'CampusReconnect.exe') -ArgumentList '--self-test' -WindowStyle Hidden -PassThru
    if (-not $testProcess.WaitForExit(60000)) { $testProcess.Kill(); throw 'Tests timed out.' }
    $testProcess.Refresh()
    $report = Get-Content -LiteralPath (Join-Path $outDir '测试结果.txt') -Raw
    Write-Output $report
    if ($testProcess.ExitCode -ne 0 -or $report -match 'FAIL:' -or $report -notmatch 'PASS:') { throw 'Tests failed.' }
}
Write-Output 'Built out/CampusReconnect.exe'
