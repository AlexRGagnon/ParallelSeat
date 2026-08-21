# ADR-0010: Clean-room implementation boundary

## Context
Avoid IP contamination from proprietary multi-seat tools.

## Decision
Implement from public Microsoft docs and original design only. No reverse engineering of proprietary binaries/protocols/branding.

## Alternatives
None acceptable.

## Consequences
Slower compatibility learning; harness-first approach.

## Validation
Code review / contribution policy.
