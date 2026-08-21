# Experiment notes — WPF harness

## Observation

Stock WPF UI Automation `ValuePattern.SetValue` and `InvokePattern.Invoke` may change the native foreground window when targeting an inactive top-level window. ParallelSeat strict mode correctly rejects those outcomes (`RejectedBySafety`).

## Consequence

- Tier B (raw UIA) is not sufficient for inactive-window WPF button/value actions without focus side effects.
- The purpose-built harness therefore ships an `InProcessHarnessAdapter` (Tier A / application adapter) that updates controls on the WPF dispatcher without activation.
- Generic external WPF apps will need a similar adapter, improved UIA provider behavior, or an honest Tier E/F classification.

## Still proven by UiTests

- Shared same-process state
- Strict no-focus-steal measurement and rejection
- Adapter-based SetValue/Invoke without FG/cursor movement
- Nonactivating overlay
- Selector re-resolution after control recreation
