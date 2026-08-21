# ADR-0002: Same-session, same-application-instance architecture

## Context
Product requires human and AI on one interactive logon, one desktop, one app instance.

## Decision
Attach AI seats only to an identified application instance (PID + start time + exe + process tree + owned HWNDs). Never use a second instance/profile/VM/session.

## Alternatives
Second instance, RDP, Sandbox — excluded by requirements.

## Consequences
Instance identity must prevent PID reuse confusion after restart.

## Validation
G1 harness; application restart detection tests.
