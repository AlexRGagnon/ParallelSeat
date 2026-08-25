---
name: windows-ui-control
description: >-
  Drives Windows desktop UI via UI Automation, ParallelSeat SideBySide (no
  focus/cursor steal), SendInput/SendKeys, mouse fallbacks, and screenshots.
  Use when the user asks to control a desktop app, click menus/buttons, type
  into dialogs, take screen/window captures, find window positions, or
  automate any Windows GUI that has no usable CLI.
---

# Windows UI Control

General-purpose desktop control for the **local** agent. Capability lives in
`scripts/ui.ps1`; this skill is the playbook.

## Modes

| Mode | Default | Behavior |
|---|---|---|
| **SideBySide** | yes | Discovery local; mutating `invoke` via ParallelSeat Host (strict: no `SetForegroundWindow` / native cursor / `SendInput`) |
| **FocusSteal** | opt-in | Classic path: focus window, then UIA / SendKeys / mouse |

Pass `-Mode FocusSteal` only when SideBySide cannot do the job (keys, type, click, move, or apps that reject no-focus UIA).

## When to use

- User asks to click, type, focus, compile-via-UI, dismiss dialogs, or drive any Windows app
- No reliable file/CLI equivalent exists (prefer APIs/files when they do)
- Need screenshots or window geometry to decide where to act
- Human and agent should share the same running app instance without stealing the human’s focus (SideBySide)

## Hard rules

1. Only target processes/windows the user named for this turn (or clearly implied).
2. Default **SideBySide**. Prefer `invoke` (ParallelSeat) over focus/keys/mouse.
3. Escalation when FocusSteal is allowed: **UIA Invoke → keys → mouse click**. Never jump to mouse first.
4. After every action: **verify** (tree/state/screenshot) before the next step.
5. Refuse UAC / secure desktop. Never automate password fields (`IsPassword`).
6. On error or abort: `panic` (releases modifiers **and** releases Host lease / stops skill-managed Host per policy). Use `-NoStopOnPanic` only when debugging and you must leave Host/lease up.
7. Prefer dry-run (`-WhatIf`) when the action is destructive or ambiguous.
8. Cloud agents cannot drive this machine — local Shell only.
9. Surface ParallelSeat statuses honestly: `RejectedBySafety`, `RequiresForeground`, `Unsupported` — do not silently fall back to FocusSteal unless the user opted in.
10. Do **not** expand SideBySide beyond `invoke` / `setvalue`. Everything else is FocusSteal or refuse.

## Prerequisites (SideBySide)

Agents own Host lifecycle — **do not ask the human to start ParallelSeat.Host**.

1. Prefer installed bin: `%LOCALAPPDATA%\ParallelSeat\bin\` (`ParallelSeat.Host.exe` + `ParallelSeat.Cli.exe`) via ParallelSeat `scripts/Install-AgentHost.ps1`.
2. Or ParallelSeat source (`$env:PARALLEALSEAT_ROOT` / skill `config.local.json` / common guesses) so `ps-ensure` can build + install once.
3. Use `ps-ensure` (or just `invoke`, which auto-ensures). Check with `ps-status`.

Resolve order for tools: `-ParallelSeatRoot` → `$env:PARALLEALSEAT_ROOT` → `config.local.json` → **installed bin** (if both exes exist) → repo build outputs → fail with `BuildMissing` / `CliMissing` (no machine-specific paths).

## ParallelSeat unavailable (degraded)

When Cli/Host cannot be resolved after build/install attempt:

| Still works | Fails with `data.code` |
|---|---|
| `find`, `tree`, `shot`, `bounds`, `monitors`, `wait`, `point`, `clip`, `launch` | `ps-ensure`, SideBySide `invoke` → `CliMissing` / `BuildMissing` / `HostUnreachable` |

**Do not loop `ps-ensure`.** Either:

1. Install: `pwsh -File <ParallelSeat>\scripts\Install-AgentHost.ps1`, or
2. Continue with **`-Mode FocusSteal`** for input (`invoke`/`type`/`keys`/`click`) after reporting the code.

Discovery never requires Host.

## Toolkit

Skill root: `~/.cursor/skills/windows-ui-control/`

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "$env:USERPROFILE\.cursor\skills\windows-ui-control\scripts\ui.ps1" <command> [options]
```

(Use `pwsh` if PowerShell 7+ is installed.)

All commands print **one JSON object** to stdout (plus optional screenshot files under `tmp/`). Failures may include stable `data.code`.

### Commands

| Command | Purpose | SideBySide |
|---|---|---|
| `help` | List commands | ok |
| `ps-ensure` | Start Host if needed; acquire lease | ok |
| `ps-status` | Host / lease / CLI paths | ok |
| `ps-stop` | Release lease; stop Host if last + skill-managed | ok |
| `monitors` | Display bounds + DPI | ok |
| `find` | List/match top-level windows (`data.windows` always array) | ok |
| `bounds` | Window rectangle | ok |
| `tree` | UIA control dump (`data.nodes` always array) | ok |
| `shot` | Screenshot | ok |
| `wait` / `point` / `clip` / `launch` | Observe / clipboard / start | ok |
| `invoke` | Semantic action (Invoke / SetValue) | **via ParallelSeat** (auto-ensure) |
| `focus` / `keys` / `type` / `click` / `move` | Focus + input injection | **requires `-Mode FocusSteal`** (`FocusStealRequired`) |
| `panic` | Release modifiers + lease/stop per policy (`-NoStopOnPanic` = modifiers only) | ok |

### Common options

- `-Mode SideBySide|FocusSteal` — default SideBySide
- `-Name` / `-Title` / `-Process` / `-Hwnd` — window targeting
- `-AutomationId` / `-ControlType` — control filters
- `-Pattern invoke|setvalue|…` — invoke pattern (`SideBySide` supports `invoke` and `setvalue` only)
- `-Text` — value for `setvalue` / `type`
- `-WhatIf` — resolve only
- `-ParallelSeatRoot` / `-SeatId` / `-LeaseId` / `-NewLease` / `-Force` — Host wiring overrides
- `-NoStopOnPanic` — with `panic`: modifiers only; leave Host/lease alone

### Stable `data.code` values

| Code | Meaning |
|---|---|
| `HostUnreachable` | Host not running / RPC unhealthy after ensure |
| `BuildMissing` | Host exe / repo build unavailable |
| `CliMissing` | `ParallelSeat.Cli.exe` missing |
| `RejectedBySafety` | Strict SideBySide refused (often FG steal from stock UIA) |
| `ConflictPaused` | Seat paused for human conflict |
| `Unsupported` | Pattern not available SideBySide |
| `FocusStealRequired` | Command needs `-Mode FocusSteal` |
| `NoWindow` | No matching top-level window |
| `ControlNotFound` | Control selector missed |
| `LeaseNone` | No lease present (e.g. stop with nothing to release) |

## Host lease policy

State under `%LOCALAPPDATA%\ParallelSeat\`:

| File | Meaning |
|---|---|
| `session.json` | Host pipe / token / PID (written by Host) |
| `agent-host.json` | Present only if **this skill** started Host |
| `leases/<id>.json` | Active agent leases (refcount) |
| `bin\*.exe` | Optional installed Host + Cli |

### Lease hygiene (Cursor agents)

1. **One `ps-ensure` per UI task** (or rely on `invoke` auto-ensure). Do not re-ensure between every find/tree.
2. **Reuse** the default lease (`last-lease.json`); use `-NewLease` only for concurrent agents.
3. **One `ps-stop` at end** of the UI task (or `panic` on abort).
4. Discovery (`find` / `tree` / `shot` / …) does **not** ensure and does **not** need a lease.

### When not to `ps-ensure`

- Pure discovery / screenshots / bounds / monitors
- After Host already healthy this task (check `ps-status` if unsure)
- In degraded mode when Cli/Host missing — switch to FocusSteal or install once instead of looping ensure

### Ensure / stop rules

1. `ps-ensure` acquires or **reuses** the last lease. If Host is unhealthy/missing, starts `ParallelSeat.Host --headless`, waits for RPC health, writes `agent-host.json`. May auto-build/install into LocalAppData when SDK + repo are available.
2. SideBySide `invoke` calls ensure automatically (discovery commands do **not**).
3. `ps-stop` releases this caller’s lease (or `-LeaseId` / `$env:PARALLEALSEAT_LEASE_ID` / last-lease).
4. Host is stopped only when **no leases remain** and `agent-host.json` says skill-managed. Human-started Host is left running.
5. Concurrent agents: pass `-NewLease` so each gets a distinct lease; the last `ps-stop` stops a skill-managed Host.
6. Leases expire after 6 hours (TTL); dead PID is **not** used for prune (each `ui.ps1` invocation is a short-lived process).
7. Stale session (dead Host PID) is cleared and Host restarted on ensure.
8. End every UI task with `ps-stop` (or `panic` on abort).

## Status → next action

| Observation | Next action |
|---|---|
| `ok: true`, SideBySide invoke Succeeded | Verify (tree/shot/wait), continue |
| `data.code` / status `RejectedBySafety` | Report honestly; ask user or `-Mode FocusSteal` — do not silent-fallback |
| `Unsupported` | Same; SideBySide surface is invoke/setvalue only |
| `ConflictPaused` | Wait / ask human; do not hammer invoke |
| `HostUnreachable` / ensure fail | Fix install/build once; else FocusSteal; **do not loop ensure** |
| `CliMissing` / `BuildMissing` | Run `Install-AgentHost.ps1` or FocusSteal discovery+input |
| `FocusStealRequired` | Re-run with `-Mode FocusSteal` if user allowed |
| `NoWindow` / `ControlNotFound` | Re-find / re-tree; fix selectors |
| `LeaseNone` on stop | Fine if nothing was ensured; otherwise check `ps-status` |

## Agent loop

```
1. ps-ensure            → once per UI task (skip if discovery-only)
2. find / monitors      → what exists, where
3. tree | shot          → plan targets (no focus steal)
4. invoke (SideBySide)  → ParallelSeat semantic action (auto-ensure)
5. if RejectedBySafety / Unsupported → ask user or -Mode FocusSteal
6. wait / re-verify     → only then continue
7. ps-stop              → once at end
8. panic on failure     → modifiers + lease/stop (-NoStopOnPanic to debug)
```

Checklist:

```
UI Task:
- [ ] Target app/window identified
- [ ] One ps-ensure (skip if discovery-only; or rely on invoke auto-ensure)
- [ ] Action via invoke (not focus/keys first)
- [ ] Verified (state or screenshot)
- [ ] FocusSteal only if user allowed / SideBySide failed honestly
- [ ] One ps-stop (or panic if aborted)
```

## Screenshots

- `shot -Target screen|primary|window|region`
- After capture, **Read** the image file
- Keep shots small when possible

## Safety classes

| Class | Examples | Behavior |
|---|---|---|
| Safe | find, tree, shot, bounds, point, clip get, ps-status | Free |
| SideBySide input | invoke via ParallelSeat | Prefer; may return RejectedBySafety |
| FocusSteal input | focus, keys, type, click, move | Explicit `-Mode FocusSteal` |
| Destructive | close window, kill process, overwrite files in dialogs | Confirm unless user already ordered it |
| Forbidden | UAC, password fields, unrelated apps | Refuse |

## Honest failure modes

| Status / symptom | Meaning | Agent action |
|---|---|---|
| `RejectedBySafety` | Strict SideBySide refused (often FG steal from stock UIA) | Report; ask or use FocusSteal |
| `Unsupported` | Pattern/provider cannot do it SideBySide | Same |
| Host start / RPC failure | Build missing, elevation mismatch, timeout | Install/build once or FocusSteal; do not fake success |
| Elevation mismatch | Agent/Host integrity ≠ target app | Relaunch matching elevation or refuse |

**Stock UIA note:** Stock UIA `SetValue` / `Invoke` often steals foreground on some apps (including Notepad). That is **not** “fixed for all apps”; ParallelSeat strict mode reports `RejectedBySafety`. Harness/adapters may prove SideBySide where available. Never claim stock UIA never steals FG.

## Playbooks

- First-task loop: [playbooks/session-checklist.md](playbooks/session-checklist.md)
- Notepad (SBS → FocusSteal): [playbooks/notepad.md](playbooks/notepad.md)
- Optional app recipes: [playbooks/](playbooks/). Use the general toolkit first.

## Additional

- Geometry, patterns, JSON schema, troubleshooting: [reference.md](reference.md)
- ParallelSeat root / bin: `-ParallelSeatRoot`, `$env:PARALLEALSEAT_ROOT`, `config.local.json`, `%LOCALAPPDATA%\ParallelSeat\bin`, then common repo guesses
