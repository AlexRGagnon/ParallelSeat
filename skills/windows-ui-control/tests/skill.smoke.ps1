#Requires -Version 5.1
<#
.SYNOPSIS
  Light smoke asserts for windows-ui-control (path resolve, lease reuse, JSON arrays).
.EXAMPLE
  powershell -NoProfile -File .\tests\skill.smoke.ps1
#>
[CmdletBinding()]
param(
    [string]$ParallelSeatRoot,
    [string]$UiPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$failed = 0

if (-not $UiPath) {
    $UiPath = Join-Path $PSScriptRoot '..\scripts\ui.ps1'
}

if (-not $ParallelSeatRoot) {
    if ($env:PARALLEALSEAT_ROOT -and (Test-Path $env:PARALLEALSEAT_ROOT)) {
        $ParallelSeatRoot = $env:PARALLEALSEAT_ROOT
    } else {
        # skills/windows-ui-control/tests -> repo root
        $ParallelSeatRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
    }
}

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if ($Condition) {
        Write-Host "PASS: $Message"
    } else {
        Write-Host "FAIL: $Message"
        $script:failed++
    }
}

$UiPath = (Resolve-Path $UiPath).Path
Assert-True (Test-Path $UiPath) "ui.ps1 exists at $UiPath"

function Invoke-UiJson {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$UiArgs)
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $out = & powershell -NoProfile -ExecutionPolicy Bypass -File $UiPath @UiArgs 2>&1
    $exit = $LASTEXITCODE
    $ErrorActionPreference = $prev
    $text = ($out | ForEach-Object { "$_" }) -join "`n"
    $obj = $null
    try { $obj = $text | ConvertFrom-Json } catch { $obj = $null }
    return @{ exit = $exit; text = $text; json = $obj }
}

# --- Path / bin resolve via ps-status ---
$st = Invoke-UiJson ps-status -ParallelSeatRoot $ParallelSeatRoot
Assert-True ($null -ne $st.json) "ps-status returns JSON"
Assert-True ($null -ne $st.json.data) "ps-status has data"
$binPath = $st.json.data.binPath
Assert-True (-not [string]::IsNullOrWhiteSpace([string]$binPath)) "binPath present ($binPath)"

# Prefer installed bin when present
$installedCli = Join-Path $env:LOCALAPPDATA 'ParallelSeat\bin\ParallelSeat.Cli.exe'
$installedHost = Join-Path $env:LOCALAPPDATA 'ParallelSeat\bin\ParallelSeat.Host.exe'
if ((Test-Path $installedCli) -and (Test-Path $installedHost)) {
    $cli = [string]$st.json.data.cliPath
    $hostP = [string]$st.json.data.hostPath
    Assert-True ($cli -like '*\ParallelSeat\bin\ParallelSeat.Cli.exe') "cliPath prefers LocalAppData bin ($cli)"
    Assert-True ($hostP -like '*\ParallelSeat\bin\ParallelSeat.Host.exe') "hostPath prefers LocalAppData bin ($hostP)"
}

# --- Lease reuse ---
$e1 = Invoke-UiJson ps-ensure -ParallelSeatRoot $ParallelSeatRoot
Assert-True ($e1.json.ok -eq $true) "ps-ensure #1 ok ($($e1.json.message))"
$lease1 = [string]$e1.json.data.leaseId
Assert-True (-not [string]::IsNullOrWhiteSpace($lease1)) "leaseId from ensure #1"

$e2 = Invoke-UiJson ps-ensure -ParallelSeatRoot $ParallelSeatRoot
Assert-True ($e2.json.ok -eq $true) "ps-ensure #2 ok"
$lease2 = [string]$e2.json.data.leaseId
Assert-True ($lease1 -eq $lease2) "lease reused ($lease1)"

$e3 = Invoke-UiJson ps-ensure -NewLease -ParallelSeatRoot $ParallelSeatRoot
Assert-True ($e3.json.ok -eq $true) "ps-ensure -NewLease ok"
$lease3 = [string]$e3.json.data.leaseId
Assert-True ($lease3 -ne $lease1) "NewLease distinct ($lease3)"
$countAfterNew = [int]$e3.json.data.leaseCount
Assert-True ($countAfterNew -ge 2) "leaseCount >= 2 after NewLease ($countAfterNew)"

# Stop both leases
$s1 = Invoke-UiJson ps-stop -LeaseId $lease3 -ParallelSeatRoot $ParallelSeatRoot
Assert-True ($s1.json.ok -eq $true) "ps-stop lease3"
$s2 = Invoke-UiJson ps-stop -LeaseId $lease1 -ParallelSeatRoot $ParallelSeatRoot
Assert-True ($s2.json.ok -eq $true) "ps-stop lease1"

# --- Arrays: find with zero or one window still array ---
$find = Invoke-UiJson find -Process '__no_such_process_windows_ui_control__'
Assert-True ($null -ne $find.json) "find returns JSON"
Assert-True ($null -ne $find.json.data) "find has data"
$winProp = $find.json.data.PSObject.Properties['windows']
Assert-True ($null -ne $winProp) "find windows property exists"
$windowsVal = $winProp.Value
$winType = if ($null -eq $windowsVal) { 'null' } else { $windowsVal.GetType().FullName }
$countVal = 0
try { $countVal = [int]$find.json.data.count } catch { $countVal = -1 }
Assert-True ($countVal -eq 0) "find nonexistent process count=0 ($countVal)"
Assert-True ($windowsVal -is [System.Array]) "empty windows is System.Array (type=$winType)"
Assert-True ($find.text -match '"windows"\s*:\s*\[') "raw JSON windows is array bracket (empty)"

# Single-window array check if notepad available
$np = Get-Process notepad -ErrorAction SilentlyContinue | Select-Object -First 1
$startedNp = $false
if (-not $np) {
    try {
        Start-Process notepad
        Start-Sleep -Milliseconds 800
        $startedNp = $true
        $np = Get-Process notepad -ErrorAction SilentlyContinue | Select-Object -First 1
    } catch {}
}
if ($np) {
    $find1 = Invoke-UiJson find -Process notepad
    Assert-True ($find1.json.ok -eq $true) "find notepad ok"
    $c1 = 0
    try { $c1 = [int]$find1.json.data.count } catch { $c1 = 0 }
    Assert-True ($c1 -ge 1) "find notepad count >= 1"
    Assert-True ($find1.text -match '"windows"\s*:\s*\[') "raw JSON windows is array bracket"
}

if ($startedNp) {
    Get-Process notepad -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

if ($failed -gt 0) {
    Write-Host "FAILED: $failed assertion(s)"
    exit 1
}
Write-Host 'All smoke asserts passed.'
exit 0
