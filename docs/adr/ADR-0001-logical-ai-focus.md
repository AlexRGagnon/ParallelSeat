# ADR-0001: Logical AI focus instead of a second native Windows focus

## Context
Windows provides one native foreground window and one keyboard focus per input queue.

## Decision
ParallelSeat implements native focus for the human and logical focus for the AI seat.

## Alternatives
Second desktop/session; virtual HID; global SendInput — all rejected as they steal or share the native focus model incorrectly.

## Consequences
AI targeting is capability-based; cannot promise two native carets in one control.

## Validation
G2/G3: FG HWND and cursor unchanged during AI actions in strict mode.
