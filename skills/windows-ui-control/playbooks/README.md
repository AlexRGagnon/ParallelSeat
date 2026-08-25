# Playbooks

- [session-checklist.md](session-checklist.md) — first UI task this session (Host ensure → act → stop).
- [notepad.md](notepad.md) — SideBySide setvalue → expect `RejectedBySafety` → FocusSteal template.
- Add optional per-app recipes here (e.g. `ninjatrader.md`, `excel.md`).

Each app playbook should include:

1. Process / window title patterns
2. Common menu paths and control names
3. Verify signals (dialog text, file mtime, status bar)
4. Known Invoke-vs-click pitfalls

The core toolkit in `scripts/ui.ps1` remains general-purpose; playbooks are shortcuts, not requirements.
