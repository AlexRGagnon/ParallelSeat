#Requires -Version 5.1
<#
.SYNOPSIS
  Install the windows-ui-control agent skill for Cursor and/or Codex.
.DESCRIPTION
  Copies skills/windows-ui-control into the user's agent skills directory.
  Optionally publishes ParallelSeat Host+Cli (dotnet SDK + repo) or copies
  pre-built binaries from a package layout (bundled bin next to this script's package root).

.PARAMETER Agent
  Cursor (~\.cursor\skills), Codex (~\.agents\skills), or Both.

.PARAMETER InstallHost
  Publish ParallelSeat.Host + ParallelSeat.Cli to %LOCALAPPDATA%\ParallelSeat\bin.
  Requires .NET SDK and the ParallelSeat repo (or -RepoRoot).

.PARAMETER UseBundledBinaries
  Copy bin\ParallelSeat.{Host,Cli}.exe from the package (see Package-WindowsUiControlSkill.ps1).
  Skips dotnet publish; useful on machines without the SDK.

.PARAMETER SkillSource
  Override skill folder to copy. Default: <RepoRoot>\skills\windows-ui-control

.PARAMETER RepoRoot
  ParallelSeat repo root (for InstallHost and optional config.local.json). Default: parent of scripts\.

.PARAMETER Force
  Replace an existing skill install.

.EXAMPLE
  pwsh -File .\scripts\Install-WindowsUiControlSkill.ps1

.EXAMPLE
  pwsh -File .\scripts\Install-WindowsUiControlSkill.ps1 -Agent Cursor -InstallHost

.EXAMPLE
  # From an extracted zip package (bundled binaries)
  pwsh -File .\Install-WindowsUiControlSkill.ps1 -UseBundledBinaries
#>
[CmdletBinding()]
param(
    [ValidateSet('Cursor', 'Codex', 'Both')]
    [string]$Agent = 'Both',

    [switch]$InstallHost,
    [switch]$UseBundledBinaries,
    [switch]$WriteConfigLocal,
    [switch]$Force,

    [string]$SkillSource,
    [string]$RepoRoot,
    [string]$PackageRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-AgentSkillDestinations {
    param([string]$AgentChoice)
    $dests = @()
    if ($AgentChoice -eq 'Cursor' -or $AgentChoice -eq 'Both') {
        $dests += Join-Path $env:USERPROFILE '.cursor\skills\windows-ui-control'
    }
    if ($AgentChoice -eq 'Codex' -or $AgentChoice -eq 'Both') {
        $dests += Join-Path $env:USERPROFILE '.agents\skills\windows-ui-control'
    }
    return $dests
}

function Copy-SkillTree {
    param(
        [string]$Source,
        [string]$Destination,
        [bool]$Overwrite
    )
    if (-not (Test-Path $Source)) {
        throw "Skill source not found: '$Source'"
    }
    if ((Test-Path $Destination) -and -not $Overwrite) {
        throw "Skill already exists at '$Destination'. Pass -Force to replace."
    }
    $parent = Split-Path $Destination -Parent
    if (-not (Test-Path $parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }
    if (Test-Path $Destination) {
        Remove-Item -LiteralPath $Destination -Recurse -Force
    }
    Copy-Item -LiteralPath $Source -Destination $Destination -Recurse -Force
}

function Install-BundledBinaries {
    param([string]$BundledBinDir, [string]$OutputDir)
    $hostExe = Join-Path $BundledBinDir 'ParallelSeat.Host.exe'
    $cliExe = Join-Path $BundledBinDir 'ParallelSeat.Cli.exe'
    if (-not (Test-Path $hostExe) -or -not (Test-Path $cliExe)) {
        throw "Bundled bin incomplete under '$BundledBinDir' (need ParallelSeat.Host.exe and ParallelSeat.Cli.exe)."
    }
    if (-not (Test-Path $OutputDir)) {
        New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
    }
    Copy-Item -LiteralPath $hostExe -Destination (Join-Path $OutputDir 'ParallelSeat.Host.exe') -Force
    Copy-Item -LiteralPath $cliExe -Destination (Join-Path $OutputDir 'ParallelSeat.Cli.exe') -Force
}

function Write-ConfigLocalExample {
    param(
        [string]$SkillDest,
        [string]$ParallelSeatRootPath
    )
    $configPath = Join-Path $SkillDest 'config.local.json'
    if (Test-Path $configPath) { return }
    $escaped = $ParallelSeatRootPath -replace '\\', '\\'
    $json = "{`n  `"parallelSeatRoot`": `"$escaped`"`n}`n"
    Set-Content -LiteralPath $configPath -Value $json -Encoding UTF8
}

# Resolve package / repo roots
$scriptDir = $PSScriptRoot
if (-not $RepoRoot) {
    $RepoRoot = Split-Path $scriptDir -Parent
}
$RepoRoot = (Resolve-Path $RepoRoot).Path

if (-not $PackageRoot) {
    # Package layout: <package>/Install-WindowsUiControlSkill.ps1 at root, or scripts/ under repo
    if (Test-Path (Join-Path (Split-Path $scriptDir -Parent) 'skills\windows-ui-control\SKILL.md')) {
        $PackageRoot = Split-Path $scriptDir -Parent
    } elseif (Test-Path (Join-Path $scriptDir 'skills\windows-ui-control\SKILL.md')) {
        $PackageRoot = $scriptDir
    } else {
        $PackageRoot = $RepoRoot
    }
}
$PackageRoot = (Resolve-Path $PackageRoot).Path

if (-not $SkillSource) {
    $candidates = @(
        (Join-Path $RepoRoot 'skills\windows-ui-control'),
        (Join-Path $PackageRoot 'skills\windows-ui-control')
    )
    foreach ($c in $candidates) {
        if (Test-Path (Join-Path $c 'SKILL.md')) {
            $SkillSource = $c
            break
        }
    }
}
if (-not $SkillSource -or -not (Test-Path (Join-Path $SkillSource 'SKILL.md'))) {
    throw "windows-ui-control skill not found. Clone ParallelSeat or pass -SkillSource."
}
$SkillSource = (Resolve-Path $SkillSource).Path

$binDir = Join-Path $env:LOCALAPPDATA 'ParallelSeat\bin'
$repoHasSln = Test-Path (Join-Path $RepoRoot 'ParallelSeat.sln')

Write-Host "Skill source: $SkillSource"
Write-Host "Agent target: $Agent"

$destinations = Get-AgentSkillDestinations -AgentChoice $Agent
foreach ($dest in $destinations) {
    Write-Host "Installing skill → $dest"
    Copy-SkillTree -Source $SkillSource -Destination $dest -Overwrite $Force.IsPresent
    if ($WriteConfigLocal -and $repoHasSln) {
        Write-ConfigLocalExample -SkillDest $dest -ParallelSeatRootPath $RepoRoot
        Write-Host "  wrote config.local.json (parallelSeatRoot -> $RepoRoot)"
    }
}

if ($UseBundledBinaries) {
    $bundledCandidates = @(
        (Join-Path $PackageRoot 'bin'),
        (Join-Path $RepoRoot 'dist\windows-ui-control-package\bin')
    )
    $bundled = $null
    foreach ($b in $bundledCandidates) {
        if (Test-Path (Join-Path $b 'ParallelSeat.Host.exe')) {
            $bundled = $b
            break
        }
    }
    if (-not $bundled) {
        throw "UseBundledBinaries set but no bin\ with Host+Cli found under package."
    }
    Write-Host "Copying bundled binaries → $binDir"
    Install-BundledBinaries -BundledBinDir $bundled -OutputDir $binDir
}

if ($InstallHost) {
    if (-not $repoHasSln) {
        throw "InstallHost requires ParallelSeat.sln under '$RepoRoot'."
    }
    $hostInstaller = Join-Path $RepoRoot 'scripts\Install-AgentHost.ps1'
    if (-not (Test-Path $hostInstaller)) {
        throw "Missing $hostInstaller"
    }
    Write-Host "Publishing Host+Cli via Install-AgentHost.ps1"
    & $hostInstaller -RepoRoot $RepoRoot
    if ($LASTEXITCODE -ne 0) { throw "Install-AgentHost.ps1 failed (exit $LASTEXITCODE)" }
}

$hostExe = Join-Path $binDir 'ParallelSeat.Host.exe'
$cliExe = Join-Path $binDir 'ParallelSeat.Cli.exe'
$binReady = (Test-Path $hostExe) -and (Test-Path $cliExe)

Write-Host @"

Done.

Skill paths:
$(($destinations | ForEach-Object { "  $_" }) -join "`n")

ParallelSeat bin ($binDir):
  Host: $(if (Test-Path $hostExe) { 'yes' } else { 'missing — run -InstallHost or -UseBundledBinaries' })
  Cli:  $(if (Test-Path $cliExe) { 'yes' } else { 'missing' })

Smoke test (from repo):
  pwsh -File "$SkillSource\tests\skill.smoke.ps1"

Quick check:
  powershell -NoProfile -File "$($destinations[0])\scripts\ui.ps1" ps-status
"@

if (-not $binReady -and -not $repoHasSln) {
    Write-Warning 'SideBySide invoke needs Host+Cli in LocalAppData\bin or a local ParallelSeat clone with dotnet SDK.'
}

exit 0
