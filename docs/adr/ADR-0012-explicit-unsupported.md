# ADR-0012: Explicit unsupported-state reporting instead of unsafe fallback

## Context
Silent fallback to focus-stealing transports violates the product invariant.

## Decision
When no safe provider can execute an action, return Unsupported / RequiresForeground with capabilities; never silently downgrade in strict mode.

## Alternatives
Best-effort SendInput — rejected for default.

## Consequences
Honest compatibility tiers (A–F).

## Validation
G4 router tests; CompatibilityProbe.
