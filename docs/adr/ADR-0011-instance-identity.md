# ADR-0011: Application-instance identity across process groups

## Context
PID alone is insufficient (child processes, PID reuse).

## Decision
Identify instances by root PID + process start time + executable identity + process tree + owned top-level HWNDs (+ app marker when available).

## Alternatives
PID-only — rejected.

## Consequences
Attach fails closed if identity does not match.

## Validation
Restart detection tests.
