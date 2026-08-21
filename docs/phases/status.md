# Phases 1–8 Status

| Phase | Status | Evidence |
|-------|--------|----------|
| 0 | Complete | ADRs, feasibility, experiments, .NET 8 SDK, git |
| 1 | Complete | TestHarness shared state + SameProcess tests |
| 2 | Complete | Seat FSM + Overlay + E-stop |
| 3 | Complete | UIA provider + UiTests (adapter path for WPF no-focus) |
| 4 | Complete | Win32 provider + unsupported for windowless WPF |
| 5 | Complete | ActionRouter + strict rejection unit tests |
| 6 | Complete | Named-pipe IPC, CLI, Python AgentSdk |
| 7 | Complete | Conflict monitor, safety + soak tests |
| 8 | Complete | CompatibilityProbe + G7/G8 decision doc |

## Agent lifecycle (MVP shipped)

- `scripts/Install-AgentHost.ps1` publishes Host + Cli to `%LOCALAPPDATA%\ParallelSeat\bin`.
- Cursor skill **windows-ui-control** owns headless `ps-ensure` / lease / `ps-stop` (no manual Host UI required).
- SideBySide agent surface remains `invoke` / `setvalue`; FocusSteal is the escape hatch.

## Verification

```powershell
dotnet build ParallelSeat.sln -c Release
dotnet test ParallelSeat.sln -c Release
pwsh -File .\scripts\Install-AgentHost.ps1
```

## Known finding

WPF stock UIA SetValue/Invoke may change foreground; strict mode rejects. Harness uses `InProcessHarnessAdapter` (Tier A) for side-by-side proof. See `docs/testing/experiment-wpf-notes.md`.
