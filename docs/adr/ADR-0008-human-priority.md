# ADR-0008: Human-priority target conflict policy

## Context
Human and AI may target overlapping UI.

## Decision
Human always wins: stop new AI actions, cancel unsafe queued actions, pause seat, never suppress human input, require revalidation to resume.

## Alternatives
AI priority; input blocking — rejected.

## Consequences
AI throughput drops during human interaction in claimed regions.

## Validation
Experiment 7; Phase 7 tests.
