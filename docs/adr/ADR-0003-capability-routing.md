# ADR-0003: Capability-based provider routing

## Context
Blind mouse/keyboard emulation steals focus and is unreliable.

## Decision
Route actions by available capabilities: Adapter → UIA → Win32 → Bridge → FocusSteal → Unsupported.

## Alternatives
Always SendInput; always UIA — rejected.

## Consequences
AI must inspect capabilities before acting; some actions unsupported.

## Validation
G4 router unit/integration tests.
