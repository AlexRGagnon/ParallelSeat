# Feasibility Experiments

| ID | Objective | Pass | Fail consequence |
|----|-----------|------|------------------|
| E1 | UIA SetValue on inactive Window B while human types in A | FG HWND + cursor unchanged; B value + shared state update | Strict SetValue unsupported for that stack |
| E2 | UIA Invoke on inactive Window B | Same + command executes | Same |
| E3 | Same top-level window, separate controls | Document which patterns work without focus move | Narrow secondary scenario |
| E4 | Classic Win32 child HWND ops | Allowlisted messages without activation | Restrict Win32 provider |
| E5 | Overlay multi-monitor/DPI | Never activates | Change overlay hosting |
| E6 | Destroy/recreate control | Controlled stale + re-resolve | Strengthen selectors |
| E7 | Human enters AI target mid-queue | Pause; no human suppression | Fix conflict policy |
| E8 | Modal dialog | Document unavailable siblings; no bypass | Explicit Unsupported |

Automated coverage lives in `ParallelSeat.UiTests`, `ParallelSeat.SafetyTests`, and `ParallelSeat.IntegrationTests`.
