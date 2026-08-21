# Notepad Experiments 1–2
TimestampUtc: 2026-08-21T06:46:04.2513644Z
PID: 18852
Instance: 18852:639228118298426364:c:\program files\windowsapps\microsoft.windowsnotepad_11.2606.15.0_x64__8wekyb3d8bbwe\notepad\notepad.exe
TopHwnd: 0x10470

## Experiment 1 — Background SetValue
- Pass: False
- Skipped: False
- FocusStealDetected: True
- ForegroundChanged: True
- CursorChanged: False
- Duration: 80 ms
- Detail: SetValue status=RejectedBySafety provider=UiAutomation context=PS-064604 fgChanged=True cursorChanged=False msg=Strict mode violation: native foreground or cursor changed during action.

## Experiment 2 — Background Invoke
- Pass: True
- Skipped: False
- FocusStealDetected: False
- ForegroundChanged: False
- CursorChanged: False
- Duration: 83 ms
- Detail: Invoke status=Succeeded provider=UiAutomation context=Add New Tab fgChanged=False cursorChanged=False msg=

## Verdict
Exp1 SetValue: FAIL — SetValue status=RejectedBySafety provider=UiAutomation context=PS-064604 fgChanged=True cursorChanged=False msg=Strict mode violation: native foreground or cursor changed during action.
Exp2 Invoke: PASS — Invoke status=Succeeded provider=UiAutomation context=Add New Tab fgChanged=False cursorChanged=False msg=
Architectural consequence: Strict mode correctly rejected focus/cursor theft; Notepad SetValue needs adapter or Tier E for that action.
