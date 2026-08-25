# Windows UI Control — Reference

## Modes

### SideBySide (default)

- Discovery (`find`, `tree`, `shot`, …) stays in `ui.ps1` (local UIA / Win32).
- Mutating `invoke` with `-Pattern invoke|setvalue` calls **ParallelSeat.Host** over named-pipe JSON-RPC (`ParallelSeat.Cli rpc`).
- `invoke` **auto-ensures** Host (lease + start if needed). Discovery does not.
- Does **not** call `SetForegroundWindow`, `SetCursorPos`, or `SendInput`.
- Host session: `%LOCALAPPDATA%\ParallelSeat\session.json` (pipe + token + host PID).
- Statuses from Host (in `data.action.status`): `Succeeded` (0), `RejectedBySafety` (4), `Unsupported` (3), `ConflictPaused` (7), `Failed` (1), …
- Surface remains **invoke / setvalue only**; other patterns need FocusSteal.

### FocusSteal

- Classic skill behavior: activate window, then UIA / SendKeys / mouse.
- Required for `focus`, `keys`, `type`, `click`, `move` (`data.code` = `FocusStealRequired` if omitted).
- Also used when SideBySide patterns are insufficient and the user opts in.
- Stock UIA after focus may still move FG — that is expected for this mode; do not claim otherwise.

## Host lifecycle

| Command | Behavior |
|---|---|
| `ps-ensure` | Resolve Cli/Host (installed bin → build/install once if needed). Healthy Host → acquire/reuse lease. Else start `Host --headless`, wait for `diagnostics.get_status`, write `agent-host.json` if we started it. |
| `ps-status` | `hostRunning`, `rpcHealthy`, leases, paths including `binPath` (never starts Host). |
| `ps-stop` | Release lease; stop Host only if last lease **and** skill-managed. `-Force` stops managed Host even with leases. May set `data.code` = `LeaseNone` when nothing to release. |
| `panic` | Release modifiers + same lease/stop policy as `ps-stop`. `-NoStopOnPanic`: modifiers only. |

Lease files: `%LOCALAPPDATA%\ParallelSeat\leases\<leaseId>.json` (6h TTL).  
Last lease: `%LOCALAPPDATA%\ParallelSeat\last-lease.json` (default for `ps-stop` / ensure reuse).  
Ownership: `%LOCALAPPDATA%\ParallelSeat\agent-host.json` (`managedBySkill`).  
Installed tools: `%LOCALAPPDATA%\ParallelSeat\bin\` via ParallelSeat `scripts/Install-AgentHost.ps1`.

`ps-ensure` reuses the last non-expired lease by default. Use `-NewLease` for an extra concurrent lease. Do not prune on holder PID — each script run is a new process.

**Lease hygiene:** one ensure per UI task, one stop at end; discovery does not ensure.

## Status → next action

| `data.code` / status | Agent next step |
|---|---|
| Succeeded / `ok: true` | Verify, continue |
| `RejectedBySafety` | Report; ask or `-Mode FocusSteal` |
| `Unsupported` | Same; do not invent new SideBySide patterns |
| `ConflictPaused` | Pause; human conflict |
| `HostUnreachable` | Do not loop ensure; install/fix Host or FocusSteal |
| `CliMissing` / `BuildMissing` | `Install-AgentHost.ps1` or FocusSteal-only |
| `FocusStealRequired` | Add `-Mode FocusSteal` if allowed |
| `NoWindow` / `ControlNotFound` | Re-target |
| `LeaseNone` | Informational on stop with no lease |

## Coordinate space

- All click/`bounds`/`shot` coordinates are **screen coordinates** in the virtual desktop (origin can be negative on multi-monitor).
- High-DPI: Win32 cursor APIs use physical pixels; UIA bounding rectangles are generally in the same screen space Windows reports for the cursor. Prefer **clicking via control** (`click -Name`) over raw math when DPI is mixed.
- Use `monitors` before assuming (0,0) is the top-left of the user's "main" work area.

## Escalation ladder

1. **SideBySide invoke** — ParallelSeat UIA SetValue / Invoke (strict safety)
2. **FocusSteal UIA pattern** — local Invoke/Toggle/Expand/Select/SetValue after focus
3. **Keys** — accelerators (`{ENTER}`, `{ESC}`, `{F5}`, `%f`) — FocusSteal only
4. **Mouse** — control-center click; raw `x,y` last — FocusSteal only
5. **Vision** — `shot` + re-`tree`; do not invent click points without bounds or a visible cue

Never silently escalate from SideBySide to FocusSteal. Report `RejectedBySafety` / Host errors and wait for explicit `-Mode FocusSteal` or user direction.

## JSON result shape

Every command returns:

```json
{
  "ok": true,
  "command": "invoke",
  "mode": "SideBySide",
  "target": { "name": "...", "automationId": "", "controlType": "Button", "bounds": { "x": 0, "y": 0, "width": 0, "height": 0 } },
  "window": { "title": "...", "pid": 0, "hwnd": "0x0", "bounds": {} },
  "path": null,
  "message": null,
  "whatIf": false,
  "data": {}
}
```

`data.code` (string) appears on many failures (and some non-success Host action statuses).

`data.windows` / `data.nodes` are **always JSON arrays** (even for a single match).

Lifecycle fields often appear in `data`: `hostRunning`, `rpcHealthy`, `startedByUs`, `leaseId`, `leaseCount`, `managedBySkill`, `pid`, `sessionPath`, `cliPath`, `hostPath`, `binPath`, `parallelSeatRoot`.

SideBySide `invoke` includes `data.transport = "ParallelSeat"`, `data.action` (Host `ActionResult`), plus `leaseId` / `startedByUs`. `ok` is true only when status is Succeeded (string or enum 0).

On failure: `ok: false` and `message` explains why. Exit code `0` only when `ok` is true.

## ParallelSeat wiring

| Item | Location |
|---|---|
| Installed bin | `%LOCALAPPDATA%\ParallelSeat\bin\ParallelSeat.{Host,Cli}.exe` |
| Host | started by `ps-ensure` / invoke (`--headless`) |
| Session | `%LOCALAPPDATA%\ParallelSeat\session.json` |
| CLI (repo fallback) | `$ParallelSeatRoot\src\ParallelSeat.Cli\bin\Release\net8.0-windows\ParallelSeat.Cli.exe` |
| Root resolve | `-ParallelSeatRoot` → `$env:PARALLEALSEAT_ROOT` → `config.local.json` → installed bin → common guesses |
| Install | `pwsh -File <repo>\scripts\Install-AgentHost.ps1` |
| Check | `ui.ps1 ps-status` / `ps-ensure` |

RPC sequence for SideBySide invoke:

1. ensure Host + lease
2. `seat.destroy` (errors soft-ignored unless catastrophic) / `seat.create` (seat id default `windows-ui-control`)
3. `seat.attach` `{ processId }`
4. `element.find` `{ automationId|name, topLevelHwnd }`
5. `keyboard.set_text` or `pointer.click`
6. (agent later) `ps-stop` / `host.shutdown` when last managed lease ends

## Useful Win32 / SendKeys chords (FocusSteal)

| Keys | Meaning |
|---|---|
| `{F5}` | F5 |
| `{ESC}` | Escape |
| `{ENTER}` | Enter |
| `{TAB}` | Tab |
| `^s` | Ctrl+S |
| `^c` / `^v` | Copy / paste |
| `%{F4}` | Alt+F4 |
| `+{TAB}` | Shift+Tab |
| `{RELEASE}` | Internal: release modifiers (also `panic`) |

Prefer SideBySide `invoke -Pattern setvalue` over `type` when the control supports ValuePattern without focus steal. Expect `RejectedBySafety` on stock UIA for some apps (e.g. Notepad).

## Tree dumps

- Default `-Depth 6` / `-MaxNodes 200` — raise only when needed.
- Filter early: `-Name`, `-AutomationId`, `-ControlType Button|MenuItem|Edit|...`
- Huge apps (browsers, VS): dump a subtree by focusing the relevant pane first (FocusSteal) or by AutomationId filters without focus.

## Screenshots

- Files default to `tmp/shot-yyyyMMdd-HHmmss.png` under the skill root.
- After `shot`, use the Read tool on the PNG path.
- Redact: do not capture when a password field is focused; refuse `type`/`setvalue` into `IsPassword`.

## Troubleshooting

| Symptom | Fix |
|---|---|
| `ps-status` hostRunning false | `ui.ps1 ps-ensure` (builds/installs if needed) |
| invoke RejectedBySafety | Stock UIA steals FG — report; FocusSteal if allowed (see `playbooks/notepad.md`) |
| `CliMissing` / `BuildMissing` | `Install-AgentHost.ps1` or set `PARALLEALSEAT_ROOT`; or FocusSteal-only |
| Host start failure | Elevation mismatch, antivirus, or stale session — `ps-ensure` clears dead PID |
| Stale session after crash | `ps-ensure` deletes dead PID session and restarts |
| `ps-stop` leaves Host up | Other leases remain, or Host was human-started (no `agent-host.json`) |
| Invoke returns ok but UI unchanged | `shot`, re-`tree`; check modal overlay |
| Keys go elsewhere | FocusSteal: `focus` again; close other topmost dialogs |
| Click misses | FocusSteal: `monitors` + `bounds`; restore if minimized |
| Empty UIA names | `shot` + `point`; try AutomationId |
| Access denied / no tree | Same integrity level as the target app |

## Playbooks

Put app-specific recipes in `playbooks/<app>.md`. Keep them short: window titles, menu paths, verify signals. Core skill stays app-agnostic. See `playbooks/session-checklist.md` and `playbooks/notepad.md`.
