#Requires -Version 5.1
<#
.SYNOPSIS
  Build a portable zip to install windows-ui-control on another Windows machine.
.DESCRIPTION
  Creates dist/windows-ui-control-package.zip containing the skill, installer scripts,
  and optionally pre-published ParallelSeat Host+Cli binaries (no .NET SDK needed on target).

.PARAMETER Configuration
  dotnet publish configuration when -BundleBinaries is set.

.PARAMETER BundleBinaries
  Run Install-AgentHost.ps1 and include %LOCALAPPDATA%\ParallelSeat\bin in the package.

.PARAMETER OutputZip
  Path for the zip file. Default: dist/windows-ui-control-package.zip

.EXAMPLE
  pwsh -File .\scripts\Package-WindowsUiControlSkill.ps1 -BundleBinaries
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$BundleBinaries,
    [string]$OutputZip,
    [string]$RepoRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) {
    $RepoRoot = Split-Path $PSScriptRoot -Parent
}
$RepoRoot = (Resolve-Path $RepoRoot).Path

$skillSrc = Join-Path $RepoRoot 'skills\windows-ui-control'
if (-not (Test-Path (Join-Path $skillSrc 'SKILL.md'))) {
    throw "Skill not found at '$skillSrc'. Ensure skills/windows-ui-control exists."
}

$distDir = Join-Path $RepoRoot 'dist'
$stageDir = Join-Path $distDir 'windows-ui-control-package'
if (Test-Path $stageDir) {
    Remove-Item -LiteralPath $stageDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $stageDir | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $stageDir 'skills') | Out-Null

Copy-Item -LiteralPath $skillSrc -Destination (Join-Path $stageDir 'skills\windows-ui-control') -Recurse -Force
# Drop runtime artifacts from skill tmp/
$stageTmp = Join-Path $stageDir 'skills\windows-ui-control\tmp'
if (Test-Path $stageTmp) {
    Get-ChildItem -LiteralPath $stageTmp -File | Where-Object { $_.Name -ne '.gitignore' } | Remove-Item -Force
}
Copy-Item -LiteralPath (Join-Path $RepoRoot 'scripts\Install-WindowsUiControlSkill.ps1') -Destination $stageDir -Force
$scriptsStage = Join-Path $stageDir 'scripts'
New-Item -ItemType Directory -Force -Path $scriptsStage | Out-Null
Copy-Item -LiteralPath (Join-Path $RepoRoot 'scripts\Install-AgentHost.ps1') -Destination $scriptsStage -Force

$readme = @"
# windows-ui-control portable package

Install on another Windows 11 machine:

```powershell
# Extract zip, then from the extracted folder:
pwsh -File .\Install-WindowsUiControlSkill.ps1 -UseBundledBinaries
```

With Cursor only:
```powershell
pwsh -File .\Install-WindowsUiControlSkill.ps1 -Agent Cursor -UseBundledBinaries
```

If the target machine has .NET 8 SDK and you also cloned ParallelSeat:
```powershell
pwsh -File .\Install-WindowsUiControlSkill.ps1 -InstallHost -WriteConfigLocal
```

Verify:
```powershell
powershell -NoProfile -File `"`$env:USERPROFILE\.cursor\skills\windows-ui-control\scripts\ui.ps1`" ps-status
```
"@
Set-Content -LiteralPath (Join-Path $stageDir 'README-INSTALL.txt') -Value $readme -Encoding UTF8

if ($BundleBinaries) {
  if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet SDK required to bundle binaries. Install .NET 8 SDK or omit -BundleBinaries.'
  }
  $hostInstaller = Join-Path $RepoRoot 'scripts\Install-AgentHost.ps1'
  & $hostInstaller -RepoRoot $RepoRoot -Configuration $Configuration
  if ($LASTEXITCODE -ne 0) { throw "Install-AgentHost.ps1 failed (exit $LASTEXITCODE)" }

  $binSrc = Join-Path $env:LOCALAPPDATA 'ParallelSeat\bin'
  $binDst = Join-Path $stageDir 'bin'
  New-Item -ItemType Directory -Force -Path $binDst | Out-Null
  Copy-Item -LiteralPath (Join-Path $binSrc 'ParallelSeat.Host.exe') -Destination $binDst -Force
  Copy-Item -LiteralPath (Join-Path $binSrc 'ParallelSeat.Cli.exe') -Destination $binDst -Force
  Write-Host "Bundled binaries from $binSrc"
}

if (-not $OutputZip) {
    $OutputZip = Join-Path $distDir 'windows-ui-control-package.zip'
}
if (-not (Test-Path $distDir)) {
    New-Item -ItemType Directory -Force -Path $distDir | Out-Null
}
if (Test-Path $OutputZip) {
    Remove-Item -LiteralPath $OutputZip -Force
}

Compress-Archive -LiteralPath $stageDir -DestinationPath $OutputZip -Force

Write-Host @"
Package ready:
  $OutputZip

On target machine: extract and run Install-WindowsUiControlSkill.ps1
$(if ($BundleBinaries) { '  with -UseBundledBinaries (binaries included)' } else { '  add -InstallHost if SDK+repo available, or rebuild package with -BundleBinaries' })
"@
exit 0
