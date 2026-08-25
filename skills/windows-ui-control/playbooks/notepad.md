# Notepad playbook

SideBySide first; expect stock UIA to reject; escalate to FocusSteal only after an honest failure.

## Setup

```powershell
$ui = "$env:USERPROFILE\.cursor\skills\windows-ui-control\scripts\ui.ps1"
# Optional: ensure Notepad is running
Start-Process notepad
powershell -NoProfile -File $ui ps-ensure
```

## Loop

1. **Find** — `powershell -NoProfile -File $ui find -Process notepad`  
   Confirm `data.windows` is a JSON **array**.

2. **Tree** — `powershell -NoProfile -File $ui tree -Process notepad -Depth 4`  
   Locate the document edit control (often name like `Text editor` / `RichEdit` depending on OS build).

3. **SideBySide setvalue** (default mode):

   ```powershell
   powershell -NoProfile -File $ui invoke -Process notepad -Name "Text editor" -Pattern setvalue -Text "hello-sbs"
   ```

   **Expect:** `ok: false` with `data.code` / action status **`RejectedBySafety`** on many builds (stock UIA SetValue steals foreground; strict ParallelSeat refuses).  
   If `Succeeded`, verify text and skip to stop — do not assume success on all machines.

4. **Escalate (only after SBS failure / user opt-in)** — FocusSteal setvalue or type:

   ```powershell
   powershell -NoProfile -File $ui invoke -Mode FocusSteal -Process notepad -Name "Text editor" -Pattern setvalue -Text "hello-fs"
   # or
   powershell -NoProfile -File $ui type -Mode FocusSteal -Process notepad -Text "hello-fs"
   ```

5. **Verify** — re-`tree` / `shot -Target window -Process notepad` / read control value. Do not claim FG was preserved under FocusSteal.

6. **Stop** — `powershell -NoProfile -File $ui ps-stop`

## Notes

- Never silently fall back from SideBySide to FocusSteal.
- Do not invent extra SideBySide patterns beyond `invoke` / `setvalue`.
- Control names differ (Win10 vs Win11 Notepad); use `tree` to pick the live name/AutomationId.
