# Security Model

- Same-user named-pipe DACL (no Everyone read)
- Process/window/control allowlists
- Explicit target claiming
- Strict mode default
- No password fields, secure desktop, UAC, protected processes
- Emergency stop (hotkey, tray, RPC)
- Fail-closed on conflict, stale targets, integrity mismatch
- Audit log with FG/cursor before/after; sensitive text redaction
