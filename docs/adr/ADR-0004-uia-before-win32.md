# ADR-0004: UI Automation before message injection

## Context
UIA provides semantic patterns; Win32 messages are brittle and deadlock-prone.

## Decision
Prefer UIA semantic patterns over targeted Win32 messaging for MVP.

## Alternatives
Win32-first; injection-first — deferred/rejected for MVP.

## Consequences
Depends on provider quality; must measure focus side effects.

## Validation
Experiments 1–2; Phase 3 tests.
