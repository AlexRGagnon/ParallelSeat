# G7/G8 Decision Log

## G7 — First real application compatibility

**Target:** Windows 11 Notepad (Store / Win32 host)  
**PID probed:** 18852  
**When:** 2026-08-21  
**Evidence:** [probe-Notepad-18852.txt](probe-Notepad-18852.txt) / [probe-latest.txt](probe-latest.txt)

| Field | Result |
|-------|--------|
| Executable | `...\WindowsApps\Microsoft.WindowsNotepad_...\Notepad\Notepad.exe` |
| Framework ID | Win32 |
| Tier | **B — Full semantic UI Automation** |
| Elevated | No |
| Protected | No |
| RequiresForeground (declared) | No (unverified experimentally) |

**Observed patterns:** Invoke, Value, Text, Toggle, Selection/SelectionItem, Scroll, ExpandCollapse, Window, Transform, ItemContainer, VirtualizedItem.

**Notes from probe:**
- Stable AutomationIds present.
- Many elements are windowless (native HWND = 0) → **Win32 message provider will be limited**; prefer UIA.
- One top-level window in this sample.

### Go / no-go for ParallelSeat against Notepad

**GO for user-mode UIA with strict measurement** as a dry-run real target.

**Experimental evidence (2026-08-21):** see [notepad-experiments-1-2.md](../testing/notepad-experiments-1-2.md)

| Experiment | Outcome |
|------------|---------|
| Exp1 SetValue | Intermittent FG activation; strict mode rejected when FG changed; cursor never moved |
| Exp2 Invoke ("Add New Tab") | Passed without FG/cursor change on the successful re-run |

**Caveats:**
1. Tier B ≠ guaranteed inactive-window no-focus-steal. Notepad SetValue sometimes activates.
2. Not a substitute for NinjaTrader classification.
3. Bridge/driver not justified by this target.

### Recommended next action for this target

Optional: add TextPattern-based edit path and/or a Notepad-specific adapter if document edits must be FG-stable every time. Otherwise keep strict fail-closed and move on to the real trading target when ready.

## G8 — Bridge or driver

Status: **Rejected for MVP / rejected for Notepad.**  
Revisit only if a Tier D/E production target (e.g. custom-canvas trading UI) requires it after threat review.
