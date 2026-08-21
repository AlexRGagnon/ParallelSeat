# ADR-0009: Local named-pipe protocol and same-user security

## Context
AI orchestration needs local IPC without network exposure.

## Decision
Length-prefixed JSON-RPC over named pipes with custom DACL for the interactive user SID (not default Everyone-read), version negotiation, and handshake token.

## Alternatives
TCP localhost; Unix sockets — pipes preferred on Windows.

## Consequences
Cross-user access denied by design.

## Validation
IPC integration + SafetyTests for ACL/malformed/replay.
