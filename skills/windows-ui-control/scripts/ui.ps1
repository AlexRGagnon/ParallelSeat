#Requires -Version 5.1
<#
.SYNOPSIS
  Windows UI control CLI for Cursor agent skill windows-ui-control.
.EXAMPLE
  pwsh -NoProfile -File ui.ps1 find -Name "Notepad"
  pwsh -NoProfile -File ui.ps1 shot -Target window -Name "Notepad"
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet(
        'help', 'monitors', 'find', 'focus', 'bounds', 'tree', 'invoke',
        'keys', 'type', 'click', 'move', 'shot', 'wait', 'point', 'clip',
        'launch', 'panic', 'ps-status', 'ps-ensure', 'ps-stop'
    )]
    [string]$Command = 'help',

    [ValidateSet('SideBySide', 'FocusSteal')]
    [string]$Mode = 'SideBySide',

    [string]$Name,
    [string]$Title,
    [string]$Process,
    [string]$AutomationId,
    [string]$ControlType,
    [string]$Keys,
    [string]$Text,
    [string]$Path,
    [string]$FilePath,
    [string]$Arguments,
    [string]$WorkingDirectory,
    [string]$ParallelSeatRoot,
    [string]$SeatId = 'windows-ui-control',
    [string]$LeaseId,
    [switch]$Force,
    [switch]$NewLease,

    [ValidateSet('screen', 'primary', 'window', 'region')]
    [string]$Target = 'window',

    [ValidateSet('left', 'right', 'middle', 'double')]
    [string]$Button = 'left',

    [ValidateSet('invoke', 'toggle', 'expand', 'collapse', 'select', 'setvalue')]
    [string]$Pattern = 'invoke',

    [int]$X,
    [int]$Y,
    [int]$Width,
    [int]$Height,
    [int]$Hwnd,
    [int]$Depth = 6,
    [int]$MaxNodes = 200,
    [int]$TimeoutSec = 30,
    [int]$IntervalMs = 250,
    [double]$OffsetX = 0,
    [double]$OffsetY = 0,
    [switch]$Regex,
    [switch]$WhatIf,
    [switch]$ClientArea,
    [switch]$NoStopOnPanic
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$SkillRoot = Split-Path $PSScriptRoot -Parent
$TmpDir = Join-Path $SkillRoot 'tmp'
if (-not (Test-Path $TmpDir)) { New-Item -ItemType Directory -Force -Path $TmpDir | Out-Null }

function Resolve-ParallelSeatRoot {
    param([string]$Explicit)
    if ($Explicit) {
        if (Test-Path (Join-Path $Explicit 'ParallelSeat.sln')) { return (Resolve-Path $Explicit).Path }
        if (Test-Path $Explicit) { return (Resolve-Path $Explicit).Path }
        # Explicit override that does not resolve: do not fall back to guesses.
        return $null
    }
    if ($env:PARALLEALSEAT_ROOT) {
        $envRoot = $env:PARALLEALSEAT_ROOT
        if (Test-Path $envRoot) { return (Resolve-Path $envRoot).Path }
    }
    $configPath = Join-Path $SkillRoot 'config.local.json'
    if (Test-Path $configPath) {
        try {
            $cfg = Get-Content -Raw -Path $configPath | ConvertFrom-Json
            if ($cfg.parallelSeatRoot -and (Test-Path $cfg.parallelSeatRoot)) {
                return (Resolve-Path $cfg.parallelSeatRoot).Path
            }
        } catch {}
    }
    $guesses = @(
        (Join-Path $env:USERPROFILE 'source\repos\ParallelSeat'),
        (Join-Path $env:USERPROFILE 'Source\Repos\ParallelSeat'),
        (Join-Path $env:USERPROFILE 'repos\ParallelSeat')
    )
    foreach ($g in $guesses) {
        if ($g -and (Test-Path (Join-Path $g 'ParallelSeat.sln'))) { return (Resolve-Path $g).Path }
    }
    return $null
}

$ParallelSeatRoot = Resolve-ParallelSeatRoot -Explicit $ParallelSeatRoot
$ParallelSeatDir = Join-Path $env:LOCALAPPDATA 'ParallelSeat'
$ParallelSeatBinDir = Join-Path $ParallelSeatDir 'bin'
$ParallelSeatSessionPath = Join-Path $ParallelSeatDir 'session.json'
$ParallelSeatAgentHostPath = Join-Path $ParallelSeatDir 'agent-host.json'
$ParallelSeatLeasesDir = Join-Path $ParallelSeatDir 'leases'
$script:ParallelSeatBuiltAttempted = $false
$script:ParallelSeatInstallAttempted = $false
$script:ActiveLeaseId = $null
$script:LastEnsureStartedByUs = $false

# Window title vs control name:
# - find/focus/bounds/shot/keys/type: -Name is window title if -Title omitted
# - invoke/click/wait: window via -Title/-Process/-Hwnd; -Name is the control
# - tree: window via -Title/-Process/-Hwnd/-Name; with -Process, -Name also filters nodes
$WindowTitleFilter = $Title
if (-not $WindowTitleFilter) {
    if ($Command -in @('find', 'focus', 'bounds', 'shot', 'keys', 'type')) {
        $WindowTitleFilter = $Name
    } elseif ($Command -eq 'tree' -and -not $Process -and -not $Hwnd) {
        $WindowTitleFilter = $Name
    } elseif ($Command -in @('invoke', 'click', 'wait') -and -not $Process -and -not $Hwnd) {
        # Need a window selector; if only -Name given without process, treat as window title
        # and require AutomationId/ControlType for the control (see command body).
        if ($AutomationId -or $ControlType) { $WindowTitleFilter = $Name }
    }
}

function ConvertTo-JsonArray {
    param($Value)
    if ($null -eq $Value) { return , [object[]]@() }
    # Single hashtable/OrderedDictionary/PSCustomObject must not be enumerated by @()
    if ($Value -is [System.Collections.IDictionary] -or $Value -is [System.Management.Automation.PSCustomObject]) {
        return , [object[]](, $Value)
    }
    if ($Value -is [System.Array] -and $Value.Rank -eq 1 -and $Value -isnot [string]) {
        # Unary comma: prevent PowerShell from unwrapping a single-element Object[] on return
        return , [object[]]$Value
    }
    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) {
        $list = New-Object System.Collections.Generic.List[object]
        foreach ($item in $Value) { $list.Add($item) | Out-Null }
        return , $list.ToArray()
    }
    return , [object[]](, $Value)
}

function Merge-ResultData {
    param(
        [object]$Data = $null,
        [string]$Code = $null
    )
    $od = [ordered]@{}
    if ($null -ne $Data) {
        if ($Data -is [System.Collections.IDictionary]) {
            foreach ($k in @($Data.Keys)) { $od[$k] = $Data[$k] }
        } else {
            foreach ($p in $Data.PSObject.Properties) {
                if ($p.Name -and -not $od.Contains($p.Name)) { $od[$p.Name] = $p.Value }
            }
        }
    }
    if ($Code) { $od['code'] = $Code }
    # OrderedDictionary.Count is unreliable when values are 0 / empty arrays — use Keys.
    if (@($od.Keys).Count -eq 0) { return $null }
    return $od
}

function Resolve-ActionStatusCode {
    param($StatusRaw)
    if ($null -eq $StatusRaw) { return $null }
    $s = [string]$StatusRaw
    switch -Regex ($s) {
        '^(4|RejectedBySafety)$' { return 'RejectedBySafety' }
        '^(3|Unsupported)$' { return 'Unsupported' }
        '^(7|ConflictPaused)$' { return 'ConflictPaused' }
        '^(0|Succeeded)$' { return $null }
        default { return $null }
    }
}

function Write-Result {
    param(
        [bool]$Ok,
        [string]$Cmd,
        [object]$Window = $null,
        [object]$TargetObj = $null,
        [string]$PathOut = $null,
        [string]$Message = $null,
        [object]$Data = $null,
        [string]$Code = $null
    )
    $dataOut = Merge-ResultData -Data $Data -Code $Code
    $obj = [ordered]@{
        ok      = $Ok
        command = $Cmd
        mode    = $Mode
        window  = $Window
        target  = $TargetObj
        path    = $PathOut
        message = $Message
        whatIf  = [bool]$WhatIf
        data    = $dataOut
    }
    $json = $obj | ConvertTo-Json -Depth 12 -Compress:$false
    Write-Output $json
    if ($Ok) { exit 0 } else { exit 1 }
}

function Test-InstalledParallelSeatBin {
    $cli = Join-Path $ParallelSeatBinDir 'ParallelSeat.Cli.exe'
    $hostExe = Join-Path $ParallelSeatBinDir 'ParallelSeat.Host.exe'
    return ((Test-Path $cli) -and (Test-Path $hostExe))
}

function Get-ParallelSeatExeCandidates {
    param([ValidateSet('Cli', 'Host')][string]$Which)
    $proj = if ($Which -eq 'Cli') { 'ParallelSeat.Cli' } else { 'ParallelSeat.Host' }
    $exe = "$proj.exe"
    $list = New-Object System.Collections.Generic.List[string]
    if (Test-InstalledParallelSeatBin) {
        $list.Add((Join-Path $ParallelSeatBinDir $exe)) | Out-Null
    }
    if ($ParallelSeatRoot) {
        $list.Add((Join-Path $ParallelSeatRoot "src\$proj\bin\Release\net8.0-windows\$exe")) | Out-Null
        $list.Add((Join-Path $ParallelSeatRoot "src\$proj\bin\Debug\net8.0-windows\$exe")) | Out-Null
    }
    if (-not (Test-InstalledParallelSeatBin)) {
        $installed = Join-Path $ParallelSeatBinDir $exe
        if (Test-Path $installed) { $list.Add($installed) | Out-Null }
    }
    return , $list.ToArray()
}

function Get-ParallelSeatCliPath {
    foreach ($c in (Get-ParallelSeatExeCandidates -Which Cli)) {
        if ($c -and (Test-Path $c)) { return $c }
    }
    return $null
}

function Get-ParallelSeatHostPath {
    foreach ($c in (Get-ParallelSeatExeCandidates -Which Host)) {
        if ($c -and (Test-Path $c)) { return $c }
    }
    return $null
}

function Install-ParallelSeatAgentBin {
    if ($script:ParallelSeatInstallAttempted) { return $false }
    $script:ParallelSeatInstallAttempted = $true
    if (-not $ParallelSeatRoot) { return $false }
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) { return $false }
    $installScript = Join-Path $ParallelSeatRoot 'scripts\Install-AgentHost.ps1'
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        if (Test-Path $installScript) {
            & $installScript 2>&1 | Out-Null
            return ($LASTEXITCODE -eq 0 -and (Test-InstalledParallelSeatBin))
        }
        if (-not (Test-Path $ParallelSeatBinDir)) {
            New-Item -ItemType Directory -Force -Path $ParallelSeatBinDir | Out-Null
        }
        $hostProj = Join-Path $ParallelSeatRoot 'src\ParallelSeat.Host\ParallelSeat.Host.csproj'
        $cliProj = Join-Path $ParallelSeatRoot 'src\ParallelSeat.Cli\ParallelSeat.Cli.csproj'
        if (-not (Test-Path $hostProj) -or -not (Test-Path $cliProj)) { return $false }
        & dotnet publish $hostProj -c Release -o $ParallelSeatBinDir --verbosity quiet 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { return $false }
        & dotnet publish $cliProj -c Release -o $ParallelSeatBinDir --verbosity quiet 2>&1 | Out-Null
        return ($LASTEXITCODE -eq 0 -and (Test-InstalledParallelSeatBin))
    } finally {
        $ErrorActionPreference = $prevEap
    }
}

function Ensure-ParallelSeatBuilt {
    if ($script:ParallelSeatBuiltAttempted) { return }
    $script:ParallelSeatBuiltAttempted = $true
    if (-not $ParallelSeatRoot) { return }
    $sln = Join-Path $ParallelSeatRoot 'ParallelSeat.sln'
    if (-not (Test-Path $sln)) { return }
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) { return }
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & dotnet build $sln -c Release --verbosity quiet 2>&1 | Out-Null
    $ErrorActionPreference = $prevEap
}

function Ensure-ParallelSeatToolsAvailable {
    <#
    .SYNOPSIS
      Resolve Cli/Host, building and installing to LocalAppData when possible.
    .OUTPUTS
      Hashtable: ok, cliPath, hostPath, code, message, installed
    #>
    $cli = Get-ParallelSeatCliPath
    $hostExe = Get-ParallelSeatHostPath
    $installed = $false
    if ($cli -and $hostExe) {
        return @{
            ok = $true
            cliPath = $cli
            hostPath = $hostExe
            code = $null
            message = $null
            installed = $false
        }
    }
    Ensure-ParallelSeatBuilt
    $cli = Get-ParallelSeatCliPath
    $hostExe = Get-ParallelSeatHostPath
    if (-not ($cli -and $hostExe)) {
        $installed = Install-ParallelSeatAgentBin
        $cli = Get-ParallelSeatCliPath
        $hostExe = Get-ParallelSeatHostPath
    }
    if ($cli -and $hostExe) {
        return @{
            ok = $true
            cliPath = $cli
            hostPath = $hostExe
            code = $null
            message = $(if ($installed) { "Published Host+Cli to $ParallelSeatBinDir" } else { $null })
            installed = $installed
        }
    }
    $code = 'CliMissing'
    $msg = "ParallelSeat.Cli.exe / ParallelSeat.Host.exe not found."
    if (-not $ParallelSeatRoot) {
        $code = 'BuildMissing'
        $msg = "ParallelSeat root not found and LocalAppData bin incomplete. Set PARALLEALSEAT_ROOT or run scripts/Install-AgentHost.ps1. Discovery still works; use -Mode FocusSteal for input, or install ParallelSeat."
    } elseif (-not $cli -and -not $hostExe) {
        $code = 'BuildMissing'
        $msg = "ParallelSeat build outputs missing under '$ParallelSeatRoot' and install to '$ParallelSeatBinDir' failed. Run: pwsh -File `"$ParallelSeatRoot\scripts\Install-AgentHost.ps1`". Or use -Mode FocusSteal."
    } elseif (-not $cli) {
        $code = 'CliMissing'
        $msg = "ParallelSeat.Cli.exe not found. Install with scripts/Install-AgentHost.ps1 or build Release. Use -Mode FocusSteal until then."
    } else {
        $code = 'BuildMissing'
        $msg = "ParallelSeat.Host.exe not found. Install with scripts/Install-AgentHost.ps1 or build Release. Use -Mode FocusSteal until then."
    }
    return @{
        ok = $false
        cliPath = $cli
        hostPath = $hostExe
        code = $code
        message = $msg
        installed = $false
    }
}

function Get-ParallelSeatSession {
    if (-not (Test-Path $ParallelSeatSessionPath)) { return $null }
    try {
        return Get-Content -Raw -Path $ParallelSeatSessionPath | ConvertFrom-Json
    } catch {
        return $null
    }
}

function Test-ProcessAlive {
    param([int]$ProcessId)
    if ($ProcessId -le 0) { return $false }
    try {
        $p = Get-Process -Id $ProcessId -ErrorAction Stop
        return $null -ne $p
    } catch {
        return $false
    }
}

function Clear-StaleParallelSeatSession {
    $session = Get-ParallelSeatSession
    if (-not $session) {
        if (Test-Path $ParallelSeatSessionPath) {
            Remove-Item -Force -ErrorAction SilentlyContinue $ParallelSeatSessionPath
        }
        return $true
    }
    $pidVal = 0
    try { $pidVal = [int]$session.hostProcessId } catch { $pidVal = 0 }
    if (-not (Test-ProcessAlive -ProcessId $pidVal)) {
        Remove-Item -Force -ErrorAction SilentlyContinue $ParallelSeatSessionPath
        return $true
    }
    return $false
}

function Get-ParallelSeatAgentHost {
    if (-not (Test-Path $ParallelSeatAgentHostPath)) { return $null }
    try {
        return Get-Content -Raw -Path $ParallelSeatAgentHostPath | ConvertFrom-Json
    } catch {
        return $null
    }
}

function Write-ParallelSeatAgentHost {
    param([int]$HostProcessId)
    if (-not (Test-Path $ParallelSeatDir)) {
        New-Item -ItemType Directory -Force -Path $ParallelSeatDir | Out-Null
    }
    $obj = [ordered]@{
        managedBySkill = $true
        hostProcessId  = $HostProcessId
        startedAtUtc   = [DateTime]::UtcNow.ToString('o')
    }
    ($obj | ConvertTo-Json -Compress) | Set-Content -Path $ParallelSeatAgentHostPath -Encoding UTF8
}

function Clear-ParallelSeatAgentHost {
    Remove-Item -Force -ErrorAction SilentlyContinue $ParallelSeatAgentHostPath
}

function Sync-ParallelSeatAgentHost {
    $ah = Get-ParallelSeatAgentHost
    if (-not $ah) { return $null }
    $pidVal = 0
    try { $pidVal = [int]$ah.hostProcessId } catch { $pidVal = 0 }
    if (-not (Test-ProcessAlive -ProcessId $pidVal)) {
        Clear-ParallelSeatAgentHost
        return $null
    }
    return $ah
}

function Ensure-ParallelSeatLeasesDir {
    if (-not (Test-Path $ParallelSeatLeasesDir)) {
        New-Item -ItemType Directory -Force -Path $ParallelSeatLeasesDir | Out-Null
    }
}

$ParallelSeatLastLeasePath = Join-Path $ParallelSeatDir 'last-lease.json'
$script:ParallelSeatLeaseTtlHours = 6

function Get-ParallelSeatLeaseFiles {
    Ensure-ParallelSeatLeasesDir
    Get-ChildItem -Path $ParallelSeatLeasesDir -Filter '*.json' -ErrorAction SilentlyContinue
}

function Read-ParallelSeatLease {
    param([string]$Path)
    try {
        return Get-Content -Raw -Path $Path | ConvertFrom-Json
    } catch {
        return $null
    }
}

function Test-ParallelSeatLeaseExpired {
    param($Lease)
    if (-not $Lease) { return $true }
    $exp = Get-JsonProp $Lease 'expiresAtUtc'
    if (-not $exp) { return $false }
    try {
        return ([DateTime]::UtcNow -gt [DateTime]::Parse([string]$exp).ToUniversalTime())
    } catch {
        return $false
    }
}

function Prune-DeadParallelSeatLeases {
    foreach ($f in (Get-ParallelSeatLeaseFiles)) {
        $lease = Read-ParallelSeatLease -Path $f.FullName
        if (-not $lease -or (Test-ParallelSeatLeaseExpired $lease)) {
            Remove-Item -Force -ErrorAction SilentlyContinue $f.FullName
        }
    }
}

function Get-ParallelSeatLeaseCount {
    Prune-DeadParallelSeatLeases
    return @(Get-ParallelSeatLeaseFiles).Count
}

function Write-ParallelSeatLastLease {
    param([string]$Id)
    if (-not (Test-Path $ParallelSeatDir)) {
        New-Item -ItemType Directory -Force -Path $ParallelSeatDir | Out-Null
    }
    $obj = [ordered]@{ leaseId = $Id; updatedAtUtc = [DateTime]::UtcNow.ToString('o') }
    ($obj | ConvertTo-Json -Compress) | Set-Content -Path $ParallelSeatLastLeasePath -Encoding UTF8
}

function Read-ParallelSeatLastLeaseId {
    if (-not (Test-Path $ParallelSeatLastLeasePath)) { return $null }
    try {
        $o = Get-Content -Raw -Path $ParallelSeatLastLeasePath | ConvertFrom-Json
        return [string](Get-JsonProp $o 'leaseId')
    } catch {
        return $null
    }
}

function New-ParallelSeatLease {
    Ensure-ParallelSeatLeasesDir
    $id = [Guid]::NewGuid().ToString('N')
    $path = Join-Path $ParallelSeatLeasesDir "$id.json"
    $expires = [DateTime]::UtcNow.AddHours($script:ParallelSeatLeaseTtlHours).ToString('o')
    $obj = [ordered]@{
        leaseId       = $id
        holder        = 'windows-ui-control'
        holderPid     = $PID
        acquiredAtUtc = [DateTime]::UtcNow.ToString('o')
        expiresAtUtc  = $expires
    }
    ($obj | ConvertTo-Json -Compress) | Set-Content -Path $path -Encoding UTF8
    $script:ActiveLeaseId = $id
    $env:PARALLEALSEAT_LEASE_ID = $id
    Write-ParallelSeatLastLease -Id $id
    return $id
}

function Update-ParallelSeatLeaseExpiry {
    param([string]$Id)
    $path = Join-Path $ParallelSeatLeasesDir "$Id.json"
    if (-not (Test-Path $path)) { return $false }
    $lease = Read-ParallelSeatLease -Path $path
    if (-not $lease) { return $false }
    $expires = [DateTime]::UtcNow.AddHours($script:ParallelSeatLeaseTtlHours).ToString('o')
    $obj = [ordered]@{
        leaseId       = $Id
        holder        = 'windows-ui-control'
        holderPid     = $PID
        acquiredAtUtc = $(if (Get-JsonProp $lease 'acquiredAtUtc') { Get-JsonProp $lease 'acquiredAtUtc' } else { [DateTime]::UtcNow.ToString('o') })
        expiresAtUtc  = $expires
    }
    ($obj | ConvertTo-Json -Compress) | Set-Content -Path $path -Encoding UTF8
    $script:ActiveLeaseId = $Id
    $env:PARALLEALSEAT_LEASE_ID = $Id
    Write-ParallelSeatLastLease -Id $Id
    return $true
}

function Resolve-OrReuse-ParallelSeatLease {
    param([switch]$ForceNew)
    Prune-DeadParallelSeatLeases
    if (-not $ForceNew) {
        $existing = $null
        if ($LeaseId) { $existing = $LeaseId }
        elseif ($env:PARALLEALSEAT_LEASE_ID) { $existing = $env:PARALLEALSEAT_LEASE_ID }
        else { $existing = Read-ParallelSeatLastLeaseId }
        if ($existing) {
            $path = Join-Path $ParallelSeatLeasesDir "$existing.json"
            if ((Test-Path $path) -and -not (Test-ParallelSeatLeaseExpired (Read-ParallelSeatLease -Path $path))) {
                [void](Update-ParallelSeatLeaseExpiry -Id $existing)
                return $existing
            }
        }
    }
    return New-ParallelSeatLease
}

function Resolve-ParallelSeatLeaseId {
    if ($LeaseId) { return $LeaseId }
    if ($script:ActiveLeaseId) { return $script:ActiveLeaseId }
    if ($env:PARALLEALSEAT_LEASE_ID) { return $env:PARALLEALSEAT_LEASE_ID }
    $last = Read-ParallelSeatLastLeaseId
    if ($last) {
        $path = Join-Path $ParallelSeatLeasesDir "$last.json"
        if (Test-Path $path) { return $last }
    }
    # Fall back to any remaining lease (e.g. after last-lease pointer was cleared)
    Prune-DeadParallelSeatLeases
    $files = @(Get-ParallelSeatLeaseFiles | Sort-Object LastWriteTime)
    if ($files.Count -gt 0) {
        $lease = Read-ParallelSeatLease -Path $files[-1].FullName
        $id = Get-JsonProp $lease 'leaseId'
        if ($id) { return [string]$id }
        return [IO.Path]::GetFileNameWithoutExtension($files[-1].Name)
    }
    return $null
}

function Remove-ParallelSeatLease {
    param([string]$Id)
    if (-not $Id) { return $false }
    $path = Join-Path $ParallelSeatLeasesDir "$Id.json"
    if (Test-Path $path) {
        Remove-Item -Force -ErrorAction SilentlyContinue $path
        if ($script:ActiveLeaseId -eq $Id) { $script:ActiveLeaseId = $null }
        if ($env:PARALLEALSEAT_LEASE_ID -eq $Id) { Remove-Item Env:\PARALLEALSEAT_LEASE_ID -ErrorAction SilentlyContinue }
        $last = Read-ParallelSeatLastLeaseId
        if ($last -eq $Id) {
            Remove-Item -Force -ErrorAction SilentlyContinue $ParallelSeatLastLeasePath
            # Retarget last-lease to another remaining lease if any
            $files = @(Get-ParallelSeatLeaseFiles | Sort-Object LastWriteTime)
            if ($files.Count -gt 0) {
                $lease = Read-ParallelSeatLease -Path $files[-1].FullName
                $next = Get-JsonProp $lease 'leaseId'
                if (-not $next) { $next = [IO.Path]::GetFileNameWithoutExtension($files[-1].Name) }
                Write-ParallelSeatLastLease -Id $next
            }
        }
        return $true
    }
    return $false
}

function Get-JsonProp {
    param($Obj, [string]$Name)
    if ($null -eq $Obj) { return $null }
    $prop = $Obj.PSObject.Properties[$Name]
    if ($null -eq $prop) { return $null }
    return $prop.Value
}

function Test-JsonRpcError {
    param($Resp)
    $err = Get-JsonProp $Resp 'error'
    if ($null -eq $err) { return $null }
    $msg = Get-JsonProp $err 'message'
    if ($msg) { return [string]$msg }
    return [string]$err
}

function Invoke-ParallelSeatRpcRaw {
    param(
        [Parameter(Mandatory)][string]$Method,
        [hashtable]$Params = @{},
        [switch]$RequireHost
    )
    $tools = Ensure-ParallelSeatToolsAvailable
    $cli = $tools.cliPath
    if (-not $cli) {
        throw "CLI_MISSING: $($tools.message)"
    }
    if ($RequireHost -and -not (Test-ParallelSeatHostPidAlive)) {
        throw "HOST_UNREACHABLE: ParallelSeat.Host is not running (missing/stale session)."
    }
    # -InputObject avoids PowerShell wrapping a single hashtable as a JSON array.
    # Pass via @file so Windows does not strip quotes from JSON on the command line.
    $json = ConvertTo-Json -InputObject $Params -Compress -Depth 8
    if (-not $json) { $json = '{}' }
    $tmp = Join-Path $env:TEMP ("paralleleseat-rpc-" + [Guid]::NewGuid().ToString('N') + '.json')
    try {
        # UTF8 no BOM for System.Text.Json
        $utf8 = New-Object System.Text.UTF8Encoding $false
        [System.IO.File]::WriteAllText($tmp, $json, $utf8)
        $prevEap = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        $out = & $cli rpc $Method "@$tmp" 2>&1
        $exit = $LASTEXITCODE
        $ErrorActionPreference = $prevEap
        $text = ($out | ForEach-Object { "$_" }) -join "`n"
        $text = $text.Trim()
        if (-not $text) { throw "Empty response from ParallelSeat.Cli rpc $Method (exit=$exit)" }
        try {
            return ($text | ConvertFrom-Json)
        } catch {
            throw "Invalid JSON from ParallelSeat.Cli (exit=$exit): $text"
        }
    } finally {
        Remove-Item -Force -ErrorAction SilentlyContinue $tmp
    }
}

function Test-ParallelSeatHostPidAlive {
    $session = Get-ParallelSeatSession
    if (-not $session) { return $false }
    try {
        return (Test-ProcessAlive -ProcessId ([int]$session.hostProcessId))
    } catch {
        return $false
    }
}

function Test-ParallelSeatHostHealthy {
    if (-not (Test-ParallelSeatHostPidAlive)) { return $false }
    try {
        $resp = Invoke-ParallelSeatRpcRaw -Method 'diagnostics.get_status' -Params @{}
        if (Test-JsonRpcError $resp) { return $false }
        return $true
    } catch {
        return $false
    }
}

function Test-ParallelSeatHostRunning {
    # Back-compat name: PID alive (ps-status also reports rpcHealthy separately)
    Test-ParallelSeatHostPidAlive
}

function Start-ParallelSeatHostProcess {
    $tools = Ensure-ParallelSeatToolsAvailable
    $hostExe = $tools.hostPath
    if (-not $hostExe) {
        throw "BUILD_MISSING: $($tools.message)"
    }
    Clear-StaleParallelSeatSession | Out-Null
    $p = Start-Process -FilePath $hostExe -ArgumentList @('--headless') -WindowStyle Hidden -PassThru
    return $p
}

function Wait-ParallelSeatHostHealthy {
    param([int]$TimeoutSec = 20)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (Test-ParallelSeatHostHealthy) { return $true }
        Start-Sleep -Milliseconds 250
    }
    return $false
}

function Stop-ParallelSeatHostManaged {
    $session = Get-ParallelSeatSession
    $pidVal = 0
    if ($session) {
        try { $pidVal = [int]$session.hostProcessId } catch { $pidVal = 0 }
    }
    if ($pidVal -gt 0 -and (Test-ProcessAlive -ProcessId $pidVal)) {
        try {
            [void](Invoke-ParallelSeatRpcRaw -Method 'host.shutdown' -Params @{})
        } catch {
            try { Stop-Process -Id $pidVal -Force -ErrorAction SilentlyContinue } catch {}
        }
        $deadline = (Get-Date).AddSeconds(8)
        while ((Get-Date) -lt $deadline -and (Test-ProcessAlive -ProcessId $pidVal)) {
            Start-Sleep -Milliseconds 150
        }
        if (Test-ProcessAlive -ProcessId $pidVal) {
            try { Stop-Process -Id $pidVal -Force -ErrorAction SilentlyContinue } catch {}
        }
    }
    Clear-ParallelSeatAgentHost
    Remove-Item -Force -ErrorAction SilentlyContinue $ParallelSeatSessionPath
}

function Get-ParallelSeatStatusData {
    param(
        [string]$Lease = $null,
        [bool]$StartedByUs = $false
    )
    Prune-DeadParallelSeatLeases
    $session = Get-ParallelSeatSession
    $cli = Get-ParallelSeatCliPath
    $hostPath = Get-ParallelSeatHostPath
    $pidAlive = Test-ParallelSeatHostPidAlive
    $rpcHealthy = $false
    if ($pidAlive) {
        try { $rpcHealthy = Test-ParallelSeatHostHealthy } catch { $rpcHealthy = $false }
    }
    $ah = Sync-ParallelSeatAgentHost
    $managed = $false
    if ($ah -and $ah.managedBySkill) { $managed = [bool]$ah.managedBySkill }
    return [ordered]@{
        mode             = $Mode
        hostRunning      = $pidAlive
        rpcHealthy       = $rpcHealthy
        startedByUs      = $StartedByUs
        leaseId          = $Lease
        leaseCount       = (Get-ParallelSeatLeaseCount)
        managedBySkill   = $managed
        pid              = if ($session) { $session.hostProcessId } else { $null }
        sessionPath      = $ParallelSeatSessionPath
        session          = $session
        agentHostPath    = $ParallelSeatAgentHostPath
        cliPath          = $cli
        hostPath         = $hostPath
        binPath          = $ParallelSeatBinDir
        parallelSeatRoot = $ParallelSeatRoot
    }
}

function Invoke-ParallelSeatEnsure {
    Prune-DeadParallelSeatLeases
    [void](Sync-ParallelSeatAgentHost)

    $tools = Ensure-ParallelSeatToolsAvailable
    if (-not $tools.ok) {
        return @{
            ok = $false
            startedByUs = $false
            leaseId = $null
            message = $tools.message
            code = $tools.code
            data = (Merge-ResultData -Data (Get-ParallelSeatStatusData) -Code $tools.code)
        }
    }

    if (Test-ParallelSeatHostHealthy) {
        $id = Resolve-OrReuse-ParallelSeatLease -ForceNew:$NewLease
        $script:LastEnsureStartedByUs = $false
        $status = Get-ParallelSeatStatusData -Lease $id -StartedByUs:$false
        if ($tools.installed -and $tools.message) {
            $status = Merge-ResultData -Data $status
            $status['installMessage'] = $tools.message
        }
        return @{
            ok = $true
            startedByUs = $false
            leaseId = $id
            data = $status
        }
    }

    # Stale or down: clear dead session / ownership, then start
    Clear-StaleParallelSeatSession | Out-Null
    if (-not (Test-ParallelSeatHostPidAlive)) {
        Clear-ParallelSeatAgentHost
    }

    # Another instance may have come up between checks
    if (Test-ParallelSeatHostHealthy) {
        $id = Resolve-OrReuse-ParallelSeatLease -ForceNew:$NewLease
        $script:LastEnsureStartedByUs = $false
        return @{
            ok = $true
            startedByUs = $false
            leaseId = $id
            data = (Get-ParallelSeatStatusData -Lease $id -StartedByUs:$false)
        }
    }

    try {
        $proc = Start-ParallelSeatHostProcess
    } catch {
        $code = 'HostUnreachable'
        $msg = $_.Exception.Message
        if ($msg -match '(?i)not found|BuildMissing') { $code = 'BuildMissing' }
        return @{
            ok = $false
            startedByUs = $false
            leaseId = $null
            message = $msg
            code = $code
            data = (Merge-ResultData -Data (Get-ParallelSeatStatusData) -Code $code)
        }
    }

    # Exit code 2 = already running (mutex); wait for health instead of failing
    Start-Sleep -Milliseconds 400
    if (-not (Wait-ParallelSeatHostHealthy -TimeoutSec 20)) {
        $code = 'HostUnreachable'
        $msg = "Could not start or reach ParallelSeat.Host (startedPid=$($proc.Id)). Discovery still works; use -Mode FocusSteal or fix Host."
        return @{
            ok = $false
            startedByUs = $false
            leaseId = $null
            message = $msg
            code = $code
            data = (Merge-ResultData -Data (Get-ParallelSeatStatusData) -Code $code)
        }
    }

    $session = Get-ParallelSeatSession
    $hostPid = if ($session) { [int]$session.hostProcessId } else { [int]$proc.Id }
    $ourProcessStillUp = $false
    try {
        $ourProcessStillUp = (-not $proc.HasExited) -or ([int]$proc.Id -eq $hostPid -and (Test-ProcessAlive -ProcessId $hostPid))
    } catch {
        $ourProcessStillUp = Test-ProcessAlive -ProcessId $hostPid
    }
    # Only claim skill ownership when this ensure launched the living Host (not mutex-collision with an existing instance).
    $startedByUs = $false
    if ($ourProcessStillUp -and ([int]$proc.Id -eq $hostPid)) {
        Write-ParallelSeatAgentHost -HostProcessId $hostPid
        $startedByUs = $true
    } elseif (-not (Get-ParallelSeatAgentHost)) {
        # Existing Host (human or other); leave unmanaged
        $startedByUs = $false
    } else {
        $startedByUs = $false
    }

    $id = Resolve-OrReuse-ParallelSeatLease -ForceNew:$NewLease
    $script:LastEnsureStartedByUs = $startedByUs
    $status = Get-ParallelSeatStatusData -Lease $id -StartedByUs:$startedByUs
    if ($tools.installed -and $tools.message) {
        $status = Merge-ResultData -Data $status
        $status['installMessage'] = $tools.message
    }
    return @{
        ok = $true
        startedByUs = $startedByUs
        leaseId = $id
        data = $status
    }
}

function Invoke-ParallelSeatStop {
    param([switch]$ForceStop)
    Prune-DeadParallelSeatLeases
    $id = Resolve-ParallelSeatLeaseId
    $released = $false
    if ($id) {
        $released = Remove-ParallelSeatLease -Id $id
    }
    $remaining = Get-ParallelSeatLeaseCount
    $ah = Sync-ParallelSeatAgentHost
    $managed = $ah -and $ah.managedBySkill
    $stopped = $false
    $leftRunning = $true

    if ($ForceStop -and $managed) {
        Stop-ParallelSeatHostManaged
        # Drop remaining leases when forcing stop of skill-managed host
        foreach ($f in (Get-ParallelSeatLeaseFiles)) {
            Remove-Item -Force -ErrorAction SilentlyContinue $f.FullName
        }
        $stopped = $true
        $leftRunning = Test-ParallelSeatHostPidAlive
        $remaining = 0
    } elseif ($remaining -eq 0 -and $managed) {
        Stop-ParallelSeatHostManaged
        $stopped = $true
        $leftRunning = Test-ParallelSeatHostPidAlive
    } else {
        $leftRunning = Test-ParallelSeatHostPidAlive
    }

    return @{
        ok = $true
        releasedLeaseId = $id
        released = $released
        leaseCount = $remaining
        stoppedHost = $stopped
        hostRunning = $leftRunning
        managedBySkill = [bool]$managed
        data = (Get-ParallelSeatStatusData -Lease $null -StartedByUs:$false)
    }
}

function Invoke-ParallelSeatRpc {
    param(
        [Parameter(Mandatory)][string]$Method,
        [hashtable]$Params = @{}
    )
    return Invoke-ParallelSeatRpcRaw -Method $Method -Params $Params -RequireHost
}

function Assert-FocusStealAllowed {
    param([string]$ActionName)
    if ($Mode -eq 'SideBySide') {
        Write-Result -Ok:$false -Cmd $Command -Code FocusStealRequired -Message @"
SideBySide refuses '$ActionName' because it steals native focus/cursor. Use ParallelSeat-backed 'invoke' for semantic actions, or pass -Mode FocusSteal to opt into classic focus/SendInput behavior.
"@
    }
}

# --- Native helpers ----------------------------------------------------------

$Win32Code = @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class UiNative {
    public const int MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const int MOUSEEVENTF_LEFTUP = 0x0004;
    public const int MOUSEEVENTF_RIGHTDOWN = 0x0008;
    public const int MOUSEEVENTF_RIGHTUP = 0x0010;
    public const int MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    public const int MOUSEEVENTF_MIDDLEUP = 0x0040;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const byte VK_SHIFT = 0x10;
    public const byte VK_CONTROL = 0x11;
    public const byte VK_MENU = 0x12;
    public const byte VK_LWIN = 0x5B;
    public const byte VK_RWIN = 0x5C;

    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] public static extern void mouse_event(int dwFlags, int dx, int dy, int dwData, int dwExtraInfo);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int nIndex);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MONITORINFO {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    public static string GetTitle(IntPtr hWnd) {
        int len = GetWindowTextLength(hWnd);
        if (len <= 0) return "";
        var sb = new StringBuilder(len + 1);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static void ReleaseModifiers() {
        keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_RWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
        mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
        mouse_event(MOUSEEVENTF_MIDDLEUP, 0, 0, 0, 0);
    }

    public static void ClickAt(int x, int y, string button) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(30);
        if (button == "right") {
            mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
            mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
        } else if (button == "middle") {
            mouse_event(MOUSEEVENTF_MIDDLEDOWN, 0, 0, 0, 0);
            mouse_event(MOUSEEVENTF_MIDDLEUP, 0, 0, 0, 0);
        } else if (button == "double") {
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
            System.Threading.Thread.Sleep(40);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
        } else {
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
        }
    }
}
'@

try {
    Add-Type -TypeDefinition $Win32Code -ErrorAction Stop | Out-Null
} catch {
    if ($_.Exception.Message -notmatch 'already exists') { throw }
}

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms | Out-Null

function Get-RectObject {
    param([int]$Left, [int]$Top, [int]$Right, [int]$Bottom)
    return [ordered]@{
        x      = $Left
        y      = $Top
        width  = [Math]::Max(0, $Right - $Left)
        height = [Math]::Max(0, $Bottom - $Top)
        right  = $Right
        bottom = $Bottom
    }
}

function Get-AutomationRectObject {
    param($Rect)
    if ($null -eq $Rect) { return $null }
    return [ordered]@{
        x      = [int]$Rect.Left
        y      = [int]$Rect.Top
        width  = [int]$Rect.Width
        height = [int]$Rect.Height
        right  = [int]($Rect.Left + $Rect.Width)
        bottom = [int]($Rect.Top + $Rect.Height)
    }
}

function Get-WindowInfo {
    param([IntPtr]$Handle)
    if ($Handle -eq [IntPtr]::Zero) { return $null }
    $rect = New-Object UiNative+RECT
    [void][UiNative]::GetWindowRect($Handle, [ref]$rect)
    $procId = [uint32]0
    [void][UiNative]::GetWindowThreadProcessId($Handle, [ref]$procId)
    $procName = $null
    try { $procName = (Get-Process -Id $procId -ErrorAction SilentlyContinue).ProcessName } catch {}
    return [ordered]@{
        title   = [UiNative]::GetTitle($Handle)
        pid     = [int]$procId
        process = $procName
        hwnd    = ('0x{0:X}' -f $Handle.ToInt64())
        hwndInt = $Handle.ToInt64()
        bounds  = Get-RectObject $rect.Left $rect.Top $rect.Right $rect.Bottom
    }
}

function Test-TitleMatch {
    param([string]$WindowTitle, [string]$PatternText, [switch]$UseRegex)
    if ([string]::IsNullOrWhiteSpace($PatternText)) { return $true }
    if ($UseRegex) { return $WindowTitle -match $PatternText }
    return $WindowTitle -like ("*{0}*" -f $PatternText)
}

function Find-TopWindows {
    $list = New-Object System.Collections.Generic.List[object]
    $callback = [UiNative+EnumWindowsProc] {
        param([IntPtr]$hWnd, [IntPtr]$lParam)
        if (-not [UiNative]::IsWindowVisible($hWnd)) { return $true }
        $title = [UiNative]::GetTitle($hWnd)
        if ([string]::IsNullOrWhiteSpace($title)) { return $true }
        $info = Get-WindowInfo -Handle $hWnd
        $list.Add($info) | Out-Null
        return $true
    }
    [void][UiNative]::EnumWindows($callback, [IntPtr]::Zero)

    $filtered = New-Object System.Collections.Generic.List[object]
    foreach ($info in $list) {
        $ok = $true
        if ($WindowTitleFilter) { $ok = Test-TitleMatch -WindowTitle $info.title -PatternText $WindowTitleFilter -UseRegex:$Regex }
        if ($ok -and $Process) { $ok = $info.process -and ($info.process -like $Process -or $info.process -eq $Process) }
        if ($ok -and $Hwnd) { $ok = $info.hwndInt -eq $Hwnd }
        if ($ok) { $filtered.Add($info) | Out-Null }
    }
    # Unary comma: prevent PowerShell from unwrapping a single OrderedDictionary into its values
    return , $filtered.ToArray()
}

function Get-TargetWindow {
    $wins = Find-TopWindows
    if ($wins.Count -eq 0) {
        Write-Result -Ok:$false -Cmd $Command -Code NoWindow -Message "No matching window (Name/Title='$Name$Title', Process='$Process')"
    }
    return $wins[0]
}

function Focus-WindowHandle {
    param([IntPtr]$Handle)
    if ([UiNative]::IsIconic($Handle)) {
        [void][UiNative]::ShowWindow($Handle, 9) # SW_RESTORE
    }
    [void][UiNative]::SetForegroundWindow($Handle)
    Start-Sleep -Milliseconds 120
}

function Get-UiaWindowElement {
    param([IntPtr]$Handle)
    return [System.Windows.Automation.AutomationElement]::FromHandle($Handle)
}

function Get-ControlTypeValue {
    param([string]$TypeName)
    if ([string]::IsNullOrWhiteSpace($TypeName)) { return $null }
    $prop = [System.Windows.Automation.ControlType].GetProperty($TypeName, [System.Reflection.BindingFlags]'Public,Static,IgnoreCase')
    if (-not $prop) { throw "Unknown ControlType '$TypeName'" }
    return $prop.GetValue($null)
}

function Find-Controls {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$CtrlName,
        [string]$AutoId,
        [string]$TypeName,
        [int]$Limit = 50
    )
    $condition = [System.Windows.Automation.Condition]::TrueCondition
    $conditions = @()
    if ($CtrlName) {
        $conditions += New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $CtrlName)
    }
    if ($AutoId) {
        $conditions += New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $AutoId)
    }
    if ($TypeName) {
        $ct = Get-ControlTypeValue -TypeName $TypeName
        $conditions += New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $ct)
    }
    if ($conditions.Count -eq 1) {
        $condition = $conditions[0]
    } elseif ($conditions.Count -gt 1) {
        $condition = New-Object System.Windows.Automation.AndCondition($conditions)
    }

    $found = $Root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
    $results = @()
    foreach ($el in $found) {
        $results += $el
        if ($results.Count -ge $Limit) { break }
    }
    return $results
}

function Get-ElementInfo {
    param([System.Windows.Automation.AutomationElement]$Element)
    if ($null -eq $Element) { return $null }
    $ct = $Element.Current.ControlType
    $typeName = if ($ct) { $ct.ProgrammaticName -replace '^ControlType\.', '' } else { '' }
    $isPassword = $false
    try { $isPassword = [bool]$Element.Current.IsPassword } catch {}
    return [ordered]@{
        name          = $Element.Current.Name
        automationId  = $Element.Current.AutomationId
        controlType   = $typeName
        className     = $Element.Current.ClassName
        frameworkId   = $Element.Current.FrameworkId
        isEnabled     = $Element.Current.IsEnabled
        isOffscreen   = $Element.Current.IsOffscreen
        isPassword    = $isPassword
        bounds        = Get-AutomationRectObject $Element.Current.BoundingRectangle
    }
}

function Invoke-ElementPattern {
    param(
        [System.Windows.Automation.AutomationElement]$Element,
        [string]$Which,
        [string]$ValueText
    )
    switch ($Which.ToLowerInvariant()) {
        'invoke' {
            $p = $Element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
            $p.Invoke()
        }
        'toggle' {
            $p = $Element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
            $p.Toggle()
        }
        'expand' {
            $p = $Element.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
            $p.Expand()
        }
        'collapse' {
            $p = $Element.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
            $p.Collapse()
        }
        'select' {
            $p = $Element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
            $p.Select()
        }
        'setvalue' {
            $p = $Element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
            if ($Element.Current.IsPassword) { throw 'Refusing to write password field' }
            $p.SetValue($ValueText)
        }
        default { throw "Unknown pattern '$Which'" }
    }
}

function Get-TreeNodes {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [int]$MaxDepth,
        [int]$NodeLimit
    )
    $nodes = New-Object System.Collections.Generic.List[object]
    $stack = New-Object System.Collections.Generic.Stack[object]
    $stack.Push(@{ El = $Root; Depth = 0 })

    while ($stack.Count -gt 0 -and $nodes.Count -lt $NodeLimit) {
        $item = $stack.Pop()
        $el = $item.El
        $d = $item.Depth
        $info = Get-ElementInfo $el
        $info['depth'] = $d
        if ($Name -and $d -gt 0) {
            if (-not ($info.name -like ("*{0}*" -f $Name) -or $info.name -eq $Name)) {
                # still walk children; filtering applied at end optionally
            }
        }
        $nodes.Add($info) | Out-Null
        if ($d -ge $MaxDepth) { continue }
        try {
            $children = $el.FindAll(
                [System.Windows.Automation.TreeScope]::Children,
                [System.Windows.Automation.Condition]::TrueCondition)
            # push in reverse for natural order
            for ($i = $children.Count - 1; $i -ge 0; $i--) {
                $stack.Push(@{ El = $children.Item($i); Depth = $d + 1 })
            }
        } catch {}
    }
    return $nodes
}

function Filter-TreeNodes {
    param([System.Collections.IEnumerable]$Nodes)
    $filterByName = $false
    if ($Name -and ($Process -or $Hwnd -or $Title)) { $filterByName = $true }
    $out = @()
    foreach ($n in $Nodes) {
        $ok = $true
        if ($filterByName -and $n.depth -gt 0) {
            $ok = ($n.name -like ("*{0}*" -f $Name) -or $n.name -eq $Name)
        }
        if ($ok -and $AutomationId) { $ok = ($n.automationId -eq $AutomationId) }
        if ($ok -and $ControlType) { $ok = ($n.controlType -eq $ControlType) }
        if ($ok) { $out += $n }
    }
    if (($filterByName -or $AutomationId -or $ControlType) -and $out.Count -ge 0) { return $out }
    return @($Nodes)
}

function Resolve-ControlInWindow {
    param([object]$Win)
    $handle = [IntPtr]$Win.hwndInt
    $root = Get-UiaWindowElement -Handle $handle
    $ctrls = Find-Controls -Root $root -CtrlName $Name -AutoId $AutomationId -TypeName $ControlType -Limit 20
    # If Name was used as window title filter too, allow control search without reusing window Name when AutomationId set
    if ($ctrls.Count -eq 0 -and $Title -and $Name) {
        $ctrls = Find-Controls -Root $root -CtrlName $null -AutoId $AutomationId -TypeName $ControlType -Limit 20
    }
    if ($ctrls.Count -eq 0) {
        Write-Result -Ok:$false -Cmd $Command -Window $Win -Code ControlNotFound -Message "Control not found (Name='$Name', AutomationId='$AutomationId', ControlType='$ControlType')"
    }
    return $ctrls[0]
}

function Save-Bitmap {
    param([System.Drawing.Bitmap]$Bitmap, [string]$OutPath)
    if (-not $OutPath) {
        $OutPath = Join-Path $TmpDir ("shot-{0:yyyyMMdd-HHmmss-fff}.png" -f (Get-Date))
    } else {
        $dir = Split-Path $OutPath -Parent
        if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    }
    $Bitmap.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
    return $OutPath
}

function Capture-Region {
    param([int]$Left, [int]$Top, [int]$W, [int]$H, [string]$OutPath)
    if ($W -le 0 -or $H -le 0) { throw "Invalid capture size ${W}x${H}" }
    $bmp = New-Object System.Drawing.Bitmap $W, $H
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.CopyFromScreen($Left, $Top, 0, 0, (New-Object System.Drawing.Size $W, $H))
        return Save-Bitmap -Bitmap $bmp -OutPath $OutPath
    } finally {
        $g.Dispose()
        $bmp.Dispose()
    }
}

# --- Commands ----------------------------------------------------------------

switch ($Command) {
    'help' {
        $data = [ordered]@{
            commands = @(
                'help', 'monitors', 'find', 'focus', 'bounds', 'tree', 'invoke',
                'keys', 'type', 'click', 'move', 'shot', 'wait', 'point', 'clip',
                'launch', 'panic', 'ps-status', 'ps-ensure', 'ps-stop'
            )
            modes = @('SideBySide (default)', 'FocusSteal')
            skillRoot = $SkillRoot
            parallelSeatRoot = $ParallelSeatRoot
            sessionPath = $ParallelSeatSessionPath
            examples  = @(
                'ui.ps1 ps-ensure',
                'ui.ps1 ps-status',
                'ui.ps1 find -Name Notepad',
                'ui.ps1 tree -Process notepad -Depth 4',
                'ui.ps1 invoke -Mode SideBySide -Process notepad -AutomationId "..." -Pattern setvalue -Text "60"',
                'ui.ps1 invoke -Mode SideBySide -Process notepad -Name "Add New Tab"',
                'ui.ps1 ps-stop',
                'ui.ps1 focus -Mode FocusSteal -Name Notepad',
                'ui.ps1 keys -Mode FocusSteal -Name Notepad -Keys "^s"',
                'ui.ps1 shot -Target window -Name Notepad'
            )
            notes = @(
                'SideBySide: mutating invoke auto-ensures ParallelSeat.Host (no SetForegroundWindow/SendInput).',
                'FocusSteal: classic focus + keys/type/click/move path (explicit opt-in).',
                'ps-ensure / ps-stop manage Host via skill leases; do not start Host manually.',
                'Install Host+Cli: ParallelSeat/scripts/Install-AgentHost.ps1 -> %LOCALAPPDATA%\ParallelSeat\bin',
                'panic -NoStopOnPanic releases modifiers only (leaves Host/lease for debugging).',
                'data.code on failures: HostUnreachable, BuildMissing, CliMissing, RejectedBySafety, ...',
                'ps-stop only stops Host when the last lease is released and Host was skill-managed.'
            )
        }
        Write-Result -Ok:$true -Cmd help -Data $data
    }

    'monitors' {
        $mons = New-Object System.Collections.Generic.List[object]
        $idx = 0
        $proc = [UiNative+MonitorEnumProc] {
            param([IntPtr]$hMonitor, [IntPtr]$hdc, [ref]$rect, [IntPtr]$data)
            $mi = New-Object UiNative+MONITORINFO
            $mi.cbSize = [System.Runtime.InteropServices.Marshal]::SizeOf([type][UiNative+MONITORINFO])
            [void][UiNative]::GetMonitorInfo($hMonitor, [ref]$mi)
            $primary = ($mi.dwFlags -band 1) -ne 0
            $mons.Add([ordered]@{
                index   = $idx
                primary = $primary
                bounds  = Get-RectObject $mi.rcMonitor.Left $mi.rcMonitor.Top $mi.rcMonitor.Right $mi.rcMonitor.Bottom
                work    = Get-RectObject $mi.rcWork.Left $mi.rcWork.Top $mi.rcWork.Right $mi.rcWork.Bottom
            }) | Out-Null
            $script:idx = $idx + 1
            return $true
        }
        # EnumDisplayMonitors callback with index via script scope is awkward in PS; do metrics fallback
        $vx = [UiNative]::GetSystemMetrics(76)  # SM_XVIRTUALSCREEN
        $vy = [UiNative]::GetSystemMetrics(77)
        $vw = [UiNative]::GetSystemMetrics(78)
        $vh = [UiNative]::GetSystemMetrics(79)
        $sw = [UiNative]::GetSystemMetrics(0)
        $sh = [UiNative]::GetSystemMetrics(1)
        $data = [ordered]@{
            virtual = Get-RectObject $vx $vy ($vx + $vw) ($vy + $vh)
            primary = Get-RectObject 0 0 $sw $sh
            screens  = [System.Windows.Forms.Screen]::AllScreens | ForEach-Object {
                $b = $_.Bounds
                [ordered]@{
                    device   = $_.DeviceName
                    primary  = $_.Primary
                    bounds   = Get-RectObject $b.X $b.Y ($b.X + $b.Width) ($b.Y + $b.Height)
                    working  = Get-RectObject $_.WorkingArea.X $_.WorkingArea.Y ($_.WorkingArea.Right) ($_.WorkingArea.Bottom)
                    bitsPerPixel = $_.BitsPerPixel
                }
            }
        }
        Write-Result -Ok:$true -Cmd monitors -Data $data
    }

    'find' {
        $wins = ConvertTo-JsonArray (Find-TopWindows)
        Write-Result -Ok:$true -Cmd find -Data ([ordered]@{ count = $wins.Count; windows = $wins })
    }

    'ps-status' {
        Write-Result -Ok:$true -Cmd ps-status -Data (Get-ParallelSeatStatusData -Lease (Resolve-ParallelSeatLeaseId))
    }

    'ps-ensure' {
        $result = Invoke-ParallelSeatEnsure
        if (-not $result.ok) {
            Write-Result -Ok:$false -Cmd ps-ensure -Message $result.message -Data $result.data -Code $result.code
        }
        Write-Result -Ok:$true -Cmd ps-ensure -Message $(if ($result.startedByUs) { 'Host started' } else { 'Host already healthy' }) -Data $result.data
    }

    'ps-stop' {
        $result = Invoke-ParallelSeatStop -ForceStop:$Force
        $code = $null
        if (-not $result.released -and -not $result.releasedLeaseId) { $code = 'LeaseNone' }
        $msg = if ($result.stoppedHost) {
            'Released lease and stopped skill-managed Host'
        } elseif ($result.released) {
            "Released lease; Host left running (leases=$($result.leaseCount), managedBySkill=$($result.managedBySkill))"
        } else {
            "No lease to release; Host running=$($result.hostRunning)"
        }
        Write-Result -Ok:$true -Cmd ps-stop -Message $msg -Code $code -Data ([ordered]@{
            releasedLeaseId = $result.releasedLeaseId
            released        = $result.released
            stoppedHost     = $result.stoppedHost
            hostRunning     = $result.hostRunning
            leaseCount      = $result.leaseCount
            managedBySkill  = $result.managedBySkill
            status          = $result.data
        })
    }

    'focus' {
        Assert-FocusStealAllowed -ActionName 'focus'
        $win = Get-TargetWindow
        if ($WhatIf) {
            Write-Result -Ok:$true -Cmd focus -Window $win -Message 'WhatIf: would focus'
        }
        Focus-WindowHandle ([IntPtr]$win.hwndInt)
        Write-Result -Ok:$true -Cmd focus -Window (Get-WindowInfo ([IntPtr]$win.hwndInt))
    }

    'bounds' {
        $win = Get-TargetWindow
        Write-Result -Ok:$true -Cmd bounds -Window $win -Data $win.bounds
    }

    'tree' {
        $win = Get-TargetWindow
        $root = Get-UiaWindowElement ([IntPtr]$win.hwndInt)
        $nodes = Get-TreeNodes -Root $root -MaxDepth $Depth -NodeLimit $MaxNodes
        $filtered = ConvertTo-JsonArray (Filter-TreeNodes -Nodes $nodes)
        Write-Result -Ok:$true -Cmd tree -Window $win -Data ([ordered]@{
            depth    = $Depth
            maxNodes = $MaxNodes
            count    = $filtered.Count
            nodes    = $filtered
        })
    }

    'invoke' {
        $wins = Find-TopWindows
        if ($wins.Count -eq 0) {
            Write-Result -Ok:$false -Cmd invoke -Code NoWindow -Message "No matching window (Title='$Title', Process='$Process'). For invoke, select the window with -Process or -Title and the control with -Name/-AutomationId."
        }
        $win = $wins[0]
        $handle = [IntPtr]$win.hwndInt
        $ctrlName = $Name
        if ($WindowTitleFilter -and $Name -eq $WindowTitleFilter -and -not $Process -and -not $Title) {
            $ctrlName = $null
        }
        if (-not $ctrlName -and -not $AutomationId -and -not $ControlType) {
            Write-Result -Ok:$false -Cmd invoke -Window $win -Code ControlNotFound -Message "Control selector required: -Name and/or -AutomationId/-ControlType (window via -Process/-Title)"
        }

        if ($Mode -eq 'SideBySide') {
            if ($Pattern -notin @('invoke', 'setvalue')) {
                Write-Result -Ok:$false -Cmd invoke -Window $win -Code Unsupported -Message "SideBySide currently supports -Pattern invoke|setvalue via ParallelSeat. Use -Mode FocusSteal for '$Pattern'."
            }
            if ($WhatIf) {
                Write-Result -Ok:$true -Cmd invoke -Window $win -Message "WhatIf: would ParallelSeat $Pattern (SideBySide)" -Data ([ordered]@{
                    seatId = $SeatId
                    pattern = $Pattern
                    automationId = $AutomationId
                    name = $ctrlName
                })
            }
            $ensure = Invoke-ParallelSeatEnsure
            if (-not $ensure.ok) {
                Write-Result -Ok:$false -Cmd invoke -Window $win -Message $ensure.message -Data $ensure.data -Code $ensure.code
            }
            try {
                # Soft-ignore destroy errors unless catastrophic (missing seat is normal)
                try {
                    $destroyed = Invoke-ParallelSeatRpc -Method 'seat.destroy' -Params @{ seatId = $SeatId }
                    $derr = Test-JsonRpcError $destroyed
                    if ($derr -and ($derr -match '(?i)catastrophic|access denied|pipe broken|fatal')) {
                        throw $derr
                    }
                } catch {
                    $dm = $_.Exception.Message
                    if ($dm -match '(?i)catastrophic|access denied|pipe broken|fatal|HOST_UNREACHABLE|CLI_MISSING') {
                        throw
                    }
                }
                $created = Invoke-ParallelSeatRpc -Method 'seat.create' -Params @{
                    seatId = $SeatId
                    strictNoFocusSteal = $true
                }
                $err = Test-JsonRpcError $created
                if ($err) { throw $err }
                $attached = Invoke-ParallelSeatRpc -Method 'seat.attach' -Params @{
                    seatId = $SeatId
                    processId = [int]$win.pid
                }
                $err = Test-JsonRpcError $attached
                if ($err) { throw $err }
                $findParams = @{
                    seatId = $SeatId
                    topLevelHwnd = [int64]$win.hwndInt
                }
                if ($AutomationId) { $findParams.automationId = $AutomationId }
                if ($ctrlName) { $findParams.name = $ctrlName }
                $found = Invoke-ParallelSeatRpc -Method 'element.find' -Params $findParams
                $err = Test-JsonRpcError $found
                if ($err) { throw $err }

                if ($Pattern -eq 'setvalue') {
                    if ($null -eq $Text) {
                        Write-Result -Ok:$false -Cmd invoke -Window $win -Message 'Text is required for setvalue'
                    }
                    $acted = Invoke-ParallelSeatRpc -Method 'keyboard.set_text' -Params @{
                        seatId = $SeatId
                        value = $Text
                    }
                } else {
                    $acted = Invoke-ParallelSeatRpc -Method 'pointer.click' -Params @{
                        seatId = $SeatId
                    }
                }
                $err = Test-JsonRpcError $acted
                if ($err) {
                    $code = $null
                    if ($err -match '(?i)not found|no element|control') { $code = 'ControlNotFound' }
                    elseif ($err -match '(?i)HOST_UNREACHABLE|not running') { $code = 'HostUnreachable' }
                    elseif ($err -match '(?i)CLI_MISSING') { $code = 'CliMissing' }
                    Write-Result -Ok:$false -Cmd invoke -Window $win -Message $err -Data $acted -Code $code
                }
                $resultObj = Get-JsonProp $acted 'result'
                $statusRaw = Get-JsonProp $resultObj 'status'
                $statusLabel = if ($null -eq $statusRaw) { 'Unknown' } else { [string]$statusRaw }
                $ok = ($statusLabel -eq '0') -or ($statusLabel -ieq 'Succeeded')
                $statusCode = Resolve-ActionStatusCode -StatusRaw $statusRaw
                $resultMsg = Get-JsonProp $resultObj 'message'
                $msg = if ($resultMsg) { [string]$resultMsg } else { "ParallelSeat $Pattern => $statusLabel" }
                Write-Result -Ok:$ok -Cmd invoke -Window $win -Message $msg -Code $statusCode -Data ([ordered]@{
                    transport   = 'ParallelSeat'
                    seatId      = $SeatId
                    pattern     = $Pattern
                    action      = $resultObj
                    find        = (Get-JsonProp $found 'result')
                    attach      = (Get-JsonProp $attached 'result')
                    leaseId     = $ensure.leaseId
                    startedByUs = $ensure.startedByUs
                    host        = $ensure.data
                })
            } catch {
                $em = $_.Exception.Message
                $code = $null
                if ($em -match '(?i)CLI_MISSING') { $code = 'CliMissing' }
                elseif ($em -match '(?i)BUILD_MISSING') { $code = 'BuildMissing' }
                elseif ($em -match '(?i)HOST_UNREACHABLE|not running|pipe') { $code = 'HostUnreachable' }
                elseif ($em -match '(?i)not found|no element') { $code = 'ControlNotFound' }
                Write-Result -Ok:$false -Cmd invoke -Window $win -Message $em -Code $code -Data ([ordered]@{
                    leaseId     = $ensure.leaseId
                    startedByUs = $ensure.startedByUs
                })
            }
        }

        # FocusSteal path (classic)
        Focus-WindowHandle $handle
        $root = Get-UiaWindowElement $handle
        $ctrls = Find-Controls -Root $root -CtrlName $ctrlName -AutoId $AutomationId -TypeName $ControlType
        if ($ctrls.Count -eq 0) {
            Write-Result -Ok:$false -Cmd invoke -Window $win -Code ControlNotFound -Message "Control not found"
        }
        $el = $ctrls[0]
        $info = Get-ElementInfo $el
        if ($info.isPassword -and $Pattern -eq 'setvalue') {
            Write-Result -Ok:$false -Cmd invoke -Window $win -TargetObj $info -Message 'Refusing password field'
        }
        if ($WhatIf) {
            Write-Result -Ok:$true -Cmd invoke -Window $win -TargetObj $info -Message "WhatIf: would $Pattern"
        }
        try {
            Invoke-ElementPattern -Element $el -Which $Pattern -ValueText $Text
            Start-Sleep -Milliseconds 80
            Write-Result -Ok:$true -Cmd invoke -Window $win -TargetObj (Get-ElementInfo $el) -Message "Applied $Pattern" -Data ([ordered]@{ transport = 'FocusStealUia' })
        } catch {
            Write-Result -Ok:$false -Cmd invoke -Window $win -TargetObj $info -Message $_.Exception.Message
        }
    }

    'keys' {
        Assert-FocusStealAllowed -ActionName 'keys'
        if (-not $Keys) { Write-Result -Ok:$false -Cmd keys -Message 'Keys is required' }
        if ($Keys -eq '{RELEASE}') {
            [UiNative]::ReleaseModifiers()
            Write-Result -Ok:$true -Cmd keys -Message 'Modifiers released'
        }
        $win = $null
        if ($Name -or $Title -or $Process -or $Hwnd) {
            $win = Get-TargetWindow
            if (-not $WhatIf) { Focus-WindowHandle ([IntPtr]$win.hwndInt) }
        }
        if ($WhatIf) {
            Write-Result -Ok:$true -Cmd keys -Window $win -Message "WhatIf: would send '$Keys'"
        }
        [System.Windows.Forms.SendKeys]::SendWait($Keys)
        Start-Sleep -Milliseconds 50
        Write-Result -Ok:$true -Cmd keys -Window $win -Data ([ordered]@{ keys = $Keys })
    }

    'type' {
        Assert-FocusStealAllowed -ActionName 'type'
        if ($null -eq $Text) { Write-Result -Ok:$false -Cmd type -Message 'Text is required' }
        $win = $null
        if ($Name -or $Title -or $Process -or $Hwnd) {
            $win = Get-TargetWindow
            if (-not $WhatIf) { Focus-WindowHandle ([IntPtr]$win.hwndInt) }
        }
        # Guard: if focused element is password, refuse
        try {
            $fg = [System.Windows.Automation.AutomationElement]::FocusedElement
            if ($fg -and $fg.Current.IsPassword) {
                Write-Result -Ok:$false -Cmd type -Window $win -Message 'Refusing to type into password field'
            }
        } catch {}
        if ($WhatIf) {
            Write-Result -Ok:$true -Cmd type -Window $win -Message 'WhatIf: would type text' -Data ([ordered]@{ length = $Text.Length })
        }
        # Escape SendKeys special chars
        $escaped = $Text -replace '([+\^%~\{\}\[\]\(\)])', '{$1}'
        [System.Windows.Forms.SendKeys]::SendWait($escaped)
        Write-Result -Ok:$true -Cmd type -Window $win -Data ([ordered]@{ length = $Text.Length })
    }

    'click' {
        Assert-FocusStealAllowed -ActionName 'click'
        $win = $null
        $targetInfo = $null
        $cx = $X; $cy = $Y
        if ($PSBoundParameters.ContainsKey('X') -and $PSBoundParameters.ContainsKey('Y') -and -not $Name -and -not $AutomationId) {
            # raw point
        } else {
            $win = Get-TargetWindow
            Focus-WindowHandle ([IntPtr]$win.hwndInt)
            if ($Name -or $AutomationId -or $ControlType) {
                $root = Get-UiaWindowElement ([IntPtr]$win.hwndInt)
                $ctrlName = $Name
                if ($WindowTitleFilter -and $Name -eq $WindowTitleFilter -and -not $Process -and -not $Title) {
                    $ctrlName = $null
                }
                $ctrls = Find-Controls -Root $root -CtrlName $ctrlName -AutoId $AutomationId -TypeName $ControlType
                if ($ctrls.Count -eq 0) {
                    Write-Result -Ok:$false -Cmd click -Window $win -Message 'Control not found for click'
                }
                $el = $ctrls[0]
                $targetInfo = Get-ElementInfo $el
                $b = $el.Current.BoundingRectangle
                $cx = [int]($b.Left + ($b.Width / 2) + $OffsetX)
                $cy = [int]($b.Top + ($b.Height / 2) + $OffsetY)
            } elseif (-not ($PSBoundParameters.ContainsKey('X') -and $PSBoundParameters.ContainsKey('Y'))) {
                Write-Result -Ok:$false -Cmd click -Window $win -Message 'Provide control -Name/-AutomationId (with -Process/-Title) or X,Y'
            }
        }
        if ($WhatIf) {
            Write-Result -Ok:$true -Cmd click -Window $win -TargetObj $targetInfo -Message "WhatIf: would $Button click at $cx,$cy" -Data ([ordered]@{ x = $cx; y = $cy; button = $Button })
        }
        [UiNative]::ClickAt([int]$cx, [int]$cy, $Button)
        Write-Result -Ok:$true -Cmd click -Window $win -TargetObj $targetInfo -Data ([ordered]@{ x = $cx; y = $cy; button = $Button })
    }

    'move' {
        Assert-FocusStealAllowed -ActionName 'move'
        if (-not ($PSBoundParameters.ContainsKey('X') -and $PSBoundParameters.ContainsKey('Y'))) {
            Write-Result -Ok:$false -Cmd move -Message 'X and Y required'
        }
        if ($WhatIf) {
            Write-Result -Ok:$true -Cmd move -Message "WhatIf: move to $X,$Y"
        }
        [void][UiNative]::SetCursorPos($X, $Y)
        if ($Button -and $PSBoundParameters.ContainsKey('Button') -and $Button -ne 'left') {
            # optional click after move only if explicitly wanting click — use click command instead
        }
        $pt = New-Object UiNative+POINT
        [void][UiNative]::GetCursorPos([ref]$pt)
        Write-Result -Ok:$true -Cmd move -Data ([ordered]@{ x = $pt.X; y = $pt.Y })
    }

    'shot' {
        $out = $Path
        switch ($Target) {
            'screen' {
                $vx = [UiNative]::GetSystemMetrics(76)
                $vy = [UiNative]::GetSystemMetrics(77)
                $vw = [UiNative]::GetSystemMetrics(78)
                $vh = [UiNative]::GetSystemMetrics(79)
                $file = Capture-Region -Left $vx -Top $vy -W $vw -H $vh -OutPath $out
                Write-Result -Ok:$true -Cmd shot -PathOut $file -Data ([ordered]@{ target = 'screen'; bounds = Get-RectObject $vx $vy ($vx+$vw) ($vy+$vh) })
            }
            'primary' {
                $screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
                $file = Capture-Region -Left $screen.X -Top $screen.Y -W $screen.Width -H $screen.Height -OutPath $out
                Write-Result -Ok:$true -Cmd shot -PathOut $file -Data ([ordered]@{ target = 'primary'; bounds = Get-RectObject $screen.X $screen.Y ($screen.X+$screen.Width) ($screen.Y+$screen.Height) })
            }
            'region' {
                if (-not ($PSBoundParameters.ContainsKey('X') -and $PSBoundParameters.ContainsKey('Y') -and $Width -and $Height)) {
                    Write-Result -Ok:$false -Cmd shot -Message 'region requires -X -Y -Width -Height'
                }
                $file = Capture-Region -Left $X -Top $Y -W $Width -H $Height -OutPath $out
                Write-Result -Ok:$true -Cmd shot -PathOut $file -Data ([ordered]@{ target = 'region'; bounds = Get-RectObject $X $Y ($X+$Width) ($Y+$Height) })
            }
            'window' {
                $win = Get-TargetWindow
                $handle = [IntPtr]$win.hwndInt
                if ($ClientArea) {
                    $cr = New-Object UiNative+RECT
                    [void][UiNative]::GetClientRect($handle, [ref]$cr)
                    $pt = New-Object UiNative+POINT
                    $pt.X = 0; $pt.Y = 0
                    [void][UiNative]::ClientToScreen($handle, [ref]$pt)
                    $w = $cr.Right - $cr.Left
                    $h = $cr.Bottom - $cr.Top
                    $file = Capture-Region -Left $pt.X -Top $pt.Y -W $w -H $h -OutPath $out
                } else {
                    $b = $win.bounds
                    $file = Capture-Region -Left $b.x -Top $b.y -W $b.width -H $b.height -OutPath $out
                }
                Write-Result -Ok:$true -Cmd shot -Window $win -PathOut $file -Data ([ordered]@{ target = 'window' })
            }
        }
    }

    'wait' {
        $deadline = (Get-Date).AddSeconds($TimeoutSec)
        $lastMsg = 'timeout'
        while ((Get-Date) -lt $deadline) {
            try {
                if ($ControlType -or $AutomationId -or ($Name -and ($Process -or $Title -or $Hwnd))) {
                    $wins = Find-TopWindows
                    if ($wins.Count -gt 0) {
                        $win = $wins[0]
                        if (-not ($AutomationId -or $ControlType -or $Name)) {
                            Write-Result -Ok:$true -Cmd wait -Window $win -Message 'Window present'
                        }
                        $root = Get-UiaWindowElement ([IntPtr]$win.hwndInt)
                        # When waiting for a control, Name is control name; window filtered by Process/Title
                        $ctrlName = $Name
                        if ($Title) { $ctrlName = $Name }
                        $ctrls = Find-Controls -Root $root -CtrlName $ctrlName -AutoId $AutomationId -TypeName $ControlType -Limit 5
                        if ($ctrls.Count -gt 0) {
                            $info = Get-ElementInfo $ctrls[0]
                            if ($ctrls[0].Current.IsEnabled -or -not $ControlType) {
                                Write-Result -Ok:$true -Cmd wait -Window $win -TargetObj $info -Message 'Control present'
                            }
                        }
                        $lastMsg = 'window found, control not ready'
                    } else {
                        $lastMsg = 'window not found'
                    }
                } else {
                    $wins = Find-TopWindows
                    if ($wins.Count -gt 0) {
                        Write-Result -Ok:$true -Cmd wait -Window $wins[0] -Message 'Window present'
                    }
                    $lastMsg = 'window not found'
                }
            } catch {
                $lastMsg = $_.Exception.Message
            }
            Start-Sleep -Milliseconds $IntervalMs
        }
        Write-Result -Ok:$false -Cmd wait -Message "Timeout after ${TimeoutSec}s: $lastMsg"
    }

    'point' {
        if (-not ($PSBoundParameters.ContainsKey('X') -and $PSBoundParameters.ContainsKey('Y'))) {
            $pt = New-Object UiNative+POINT
            [void][UiNative]::GetCursorPos([ref]$pt)
            $X = $pt.X; $Y = $pt.Y
        }
        $ptStruct = New-Object System.Windows.Point $X, $Y
        $el = [System.Windows.Automation.AutomationElement]::FromPoint($ptStruct)
        $info = Get-ElementInfo $el
        $win = $null
        try {
            $hw = New-Object IntPtr ($el.Current.NativeWindowHandle)
            if ($hw -ne [IntPtr]::Zero) { $win = Get-WindowInfo $hw }
        } catch {}
        Write-Result -Ok:$true -Cmd point -Window $win -TargetObj $info -Data ([ordered]@{ x = $X; y = $Y })
    }

    'clip' {
        if ($PSBoundParameters.ContainsKey('Text') -or $Text) {
            if ($WhatIf) {
                Write-Result -Ok:$true -Cmd clip -Message 'WhatIf: would set clipboard'
            }
            [System.Windows.Forms.Clipboard]::SetText($Text)
            Write-Result -Ok:$true -Cmd clip -Data ([ordered]@{ action = 'set'; length = $Text.Length })
        } else {
            $t = ''
            try { $t = [System.Windows.Forms.Clipboard]::GetText() } catch {}
            Write-Result -Ok:$true -Cmd clip -Data ([ordered]@{ action = 'get'; text = $t; length = $t.Length })
        }
    }

    'launch' {
        $exe = if ($FilePath) { $FilePath } else { $Path }
        if (-not $exe) { Write-Result -Ok:$false -Cmd launch -Message 'FilePath or Path to executable required' }
        if ($WhatIf) {
            Write-Result -Ok:$true -Cmd launch -Message "WhatIf: would start $exe"
        }
        $start = @{ FilePath = $exe }
        if ($Arguments) { $start['ArgumentList'] = $Arguments }
        if ($WorkingDirectory) { $start['WorkingDirectory'] = $WorkingDirectory }
        $p = Start-Process @start -PassThru
        Write-Result -Ok:$true -Cmd launch -Data ([ordered]@{ pid = $p.Id; process = $p.ProcessName; path = $exe })
    }

    'panic' {
        [UiNative]::ReleaseModifiers()
        $stop = $null
        if (-not $NoStopOnPanic) {
            $stop = Invoke-ParallelSeatStop
        }
        $msg = if ($NoStopOnPanic) {
            'Modifiers/mouse released; Host/lease left alone (-NoStopOnPanic)'
        } else {
            'Modifiers/mouse released; lease released per Host policy'
        }
        Write-Result -Ok:$true -Cmd panic -Message $msg -Data ([ordered]@{
            noStopOnPanic   = [bool]$NoStopOnPanic
            releasedLeaseId = if ($stop) { $stop.releasedLeaseId } else { $null }
            released        = if ($stop) { $stop.released } else { $false }
            stoppedHost     = if ($stop) { $stop.stoppedHost } else { $false }
            hostRunning     = if ($stop) { $stop.hostRunning } else { (Test-ParallelSeatHostPidAlive) }
            leaseCount      = if ($stop) { $stop.leaseCount } else { (Get-ParallelSeatLeaseCount) }
        })
    }
}
