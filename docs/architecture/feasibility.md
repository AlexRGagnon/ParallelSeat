# Feasibility

## Verified Windows behavior

- One native system cursor; one foreground top-level window; one keyboard focus per relevant GUI input queue.
- UI Automation Invoke/SetValue behavior is provider-defined; focus change must be measured and treated as failure in strict mode.
- `WS_EX_NOACTIVATE` + tool/layered/transparent styles support nonactivating click-through overlays.
- Named-pipe default DACL grants Everyone read — custom same-user SID DACL is mandatory.
- UIPI blocks messages to higher-integrity processes.
- Virtual HID does not create a second focus model.

## Design decisions

1. Logical AI focus (ADR-0001).
2. Same-session, same-application-instance (ADR-0002).
3. Capability-based provider routing (ADR-0003).
4. Strict mode default; no silent downgrade (ADR-0005, ADR-0012).
5. User-mode MVP before bridge/driver (ADR-0006).

## Hypotheses (experiments)

Documented in `docs/testing/experiments.md`. G0 requires experiment specs; G3 requires Experiments 1–2 pass on the harness.
