# ADR-0007: Nonactivating overlay for AI cursor visualization

## Context
AI needs a visible cursor that must not activate or receive input.

## Decision
Use top-level layered tool window with WS_EX_NOACTIVATE, WS_EX_TOOLWINDOW, WS_EX_LAYERED, WS_EX_TOPMOST, and click-through behavior; PerMonitorV2 DPI.

## Alternatives
In-app drawing (requires target modification); activating HUD — rejected.

## Consequences
Overlay must track monitors/DPI carefully.

## Validation
Experiment 5; Overlay UiTests.
