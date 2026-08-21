# MVP Acceptance Criteria

- Only one test-harness process is running for a given harness run.
- Both top-level windows belong to the same process and share in-memory state.
- Human can type in Window A while AI updates a field in Window B.
- AI Invoke/SetValue does not change native foreground HWND or move native cursor (strict mode).
- AI cursor overlay is visible, independent, nonactivating, click-through.
- Human mouse/keyboard remain normal.
- Strict mode never selects a focus-stealing transport.
- Human entering AI target pauses the seat per policy.
- Destroying the AI seat leaves the app running and removes overlays/subscriptions.
- No logical key/button remains pressed after cancel/failure.
- Stale HWND/UIA yields controlled error + re-resolution attempt.
- Emergency stop cancels queue and removes overlay.
- Application restart cannot be mistaken for the prior instance.
- Every action logs provider and FG/cursor before/after.
