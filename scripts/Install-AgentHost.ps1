#Requires -Version 5.1
<#
.SYNOPSIS
  Publish ParallelSeat.Host + ParallelSeat.Cli to %LOCALAPPDATA%\ParallelSeat\bin for agent skill use.
.DESCRIPTION
  Does not start the Host UI. Agents start Host headless via windows-ui-control `ps-ensure`.
.EXAMPLE
  pwsh -File .\scripts\Install-AgentHost.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$OutputDir,
    [string]$RepoRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) {
    $RepoRoot = Split-Path $PSScriptRoot -Parent
}
if (-not $OutputDir) {
    $OutputDir = Join-Path $env:LOCALAPPDATA 'ParallelSeat\bin'
}

$sln = Join-Path $RepoRoot 'ParallelSeat.sln'
$hostProj = Join-Path $RepoRoot 'src\ParallelSeat.Host\ParallelSeat.Host.csproj'
$cliProj = Join-Path $RepoRoot 'src\ParallelSeat.Cli\ParallelSeat.Cli.csproj'

if (-not (Test-Path $sln)) {
    throw "ParallelSeat.sln not found under '$RepoRoot'."
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet SDK not found on PATH.'
}

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
}

Write-Host "Publishing Host → $OutputDir"
& dotnet publish $hostProj -c $Configuration -o $OutputDir --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw "dotnet publish Host failed (exit $LASTEXITCODE)" }

Write-Host "Publishing Cli → $OutputDir"
& dotnet publish $cliProj -c $Configuration -o $OutputDir --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw "dotnet publish Cli failed (exit $LASTEXITCODE)" }

$hostExe = Join-Path $OutputDir 'ParallelSeat.Host.exe'
$cliExe = Join-Path $OutputDir 'ParallelSeat.Cli.exe'
if (-not (Test-Path $hostExe) -or -not (Test-Path $cliExe)) {
    throw "Publish succeeded but expected exes missing under '$OutputDir'."
}

Write-Host @"
Installed:
  $hostExe
  $cliExe

Skill resolve order prefers this bin when both exes exist.
Start Host via: ui.ps1 ps-ensure (headless; no WPF UI required).
"@
exit 0
