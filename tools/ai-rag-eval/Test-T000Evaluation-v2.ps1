[CmdletBinding()]
param(
    [string]$FixturePath,
    [string]$RunnerPath
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RunnerPath)) {
    $RunnerPath = Join-Path $PSScriptRoot 'Invoke-T000Evaluation-v2.ps1'
}
if ([string]::IsNullOrWhiteSpace($FixturePath)) {
    $FixturePath = Join-Path $PSScriptRoot '..\..\Project-Document\06-logs\ai-evaluation\t0-00-cases.json'
}

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $RunnerPath -FixturePath $FixturePath -SelfTest
if ($LASTEXITCODE -ne 0) {
    throw "T0-00 v2 self-test failed with exit code $LASTEXITCODE."
}

Write-Output 'T0-00 v2 self-test passed.'
