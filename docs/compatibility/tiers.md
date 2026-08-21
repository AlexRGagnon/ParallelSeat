# Compatibility Tiers

| Tier | Meaning |
|------|---------|
| A | Official application adapter |
| B | Full semantic UI Automation |
| C | Partial UIA + targeted Win32 |
| D | Requires opt-in in-process bridge |
| E | Focus-stealing compatibility mode only |
| F | Unsupported |

Probe workflow: inspect exe/tree, HWND hierarchy, UIA framework IDs/patterns, identifiers, windowed vs windowless, foreground requirements, modality, elevation, protected status.
