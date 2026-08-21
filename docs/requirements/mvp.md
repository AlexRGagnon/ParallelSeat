# ParallelSeat Requirements (MVP)

## Objective

One Windows 11 interactive logon session, one desktop, one application instance/process group, shared application state. Human retains native cursor/focus/foreground. AI uses a logical seat with nonactivating overlay and capability-based action routing.

## Non-negotiable exclusions

No second application instance, browser profile, VM, Hyper-V, Sandbox, RDP, child session, second account, second desktop, separate user session, remote-control session, second native foreground/focus, virtual HID as sole solution, or global SendInput as primary input.

## Strict mode (default)

Forbidden: SetCursorPos, foreground SendInput, SetForegroundWindow, BringWindowToTop, SwitchToThisWindow, AttachThreadInput, AI SetFocus, BlockInput, human input suppression, undisclosed injection, automatic elevation, secure-desktop interaction, silent focus-stealing fallback.

## MVP acceptance

See `docs/testing/acceptance.md` and automated tests under `tests/`.
