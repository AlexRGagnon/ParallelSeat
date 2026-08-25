# Session checklist (app-agnostic)

First UI task this session:

1. `ui.ps1 ps-ensure` — **once** (Host up + lease). Skip if discovery-only.
2. `ui.ps1 find -Process <name>` (or `-Name` / `-Title`) — confirm the window (`data.windows` is always an array).
3. `ui.ps1 tree -Process <name>` and/or `shot -Target window` — pick controls.
4. `ui.ps1 invoke -Process <name> -Name "<control>"` (or `-AutomationId`) — SideBySide default (`invoke`/`setvalue` only).
5. Re-`tree` / `shot` / `wait` — verify before the next mutation.
6. If `RejectedBySafety` / `Unsupported` / `data.code` — report honestly; only then ask for `-Mode FocusSteal`.
7. `ui.ps1 ps-stop` **once** when the UI work is done (or `panic` on abort; `-NoStopOnPanic` to leave Host up while debugging).

Do not ask the human to start ParallelSeat.Host. Discovery (`find`/`tree`/`shot`) does not require Host and does not call ensure.

If `CliMissing` / `BuildMissing` / `HostUnreachable`: do not loop ensure — install via `Install-AgentHost.ps1` or continue FocusSteal-only.
