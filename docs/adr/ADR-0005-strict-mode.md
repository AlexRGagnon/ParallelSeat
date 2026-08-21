# ADR-0005: Strict mode forbids foreground SendInput

## Context
Foreground SendInput and related APIs steal the human seat.

## Decision
Strict mode (default) bans SetCursorPos, foreground SendInput, SetForegroundWindow, BringWindowToTop, SwitchToThisWindow, AttachThreadInput, AI SetFocus, BlockInput, and silent focus-steal fallbacks.

## Alternatives
Compatibility mode with explicit opt-in later (Tier E).

## Consequences
Some apps unsupported until adapter/bridge.

## Validation
SafetyTests + runtime FG/cursor assertions.
