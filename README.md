# ParallelSeat

Local Windows 11 system that lets a human and an AI agent operate the **same running application instance** at the same time—without the AI stealing the native mouse cursor, keyboard focus, or foreground window.

## Core invariant

> The human keeps native Windows control while the AI operates a logically selected part of the same running application instance through targeted, capability-aware actions that do not steal foreground focus or move the native cursor.

## Stack

- Windows 11 x64
- .NET 8 / C# 12 / WPF
- Windows UI Automation + targeted Win32 messaging
- Local named-pipe JSON-RPC (same-user ACL)
- Python async Agent SDK

## Build

```powershell
# Requires .NET 8 SDK
dotnet build ParallelSeat.sln -c Release
dotnet test ParallelSeat.sln -c Release
```

## Agent install (Host + Cli)

Agents should not require opening the WPF Host UI. Publish both exes into LocalAppData:

```powershell
pwsh -File .\scripts\Install-AgentHost.ps1
# → %LOCALAPPDATA%\ParallelSeat\bin\ParallelSeat.Host.exe
# → %LOCALAPPDATA%\ParallelSeat\bin\ParallelSeat.Cli.exe
```

The Cursor skill **windows-ui-control** (`~/.cursor/skills/windows-ui-control/`) prefers that bin, starts Host **headless** via `ui.ps1 ps-ensure`, and stops it with `ps-stop` when the last skill-managed lease ends.

```powershell
$ui = "$env:USERPROFILE\.cursor\skills\windows-ui-control\scripts\ui.ps1"
powershell -NoProfile -File $ui ps-ensure
powershell -NoProfile -File $ui ps-status
powershell -NoProfile -File $ui ps-stop
```

SideBySide surface for agents remains `invoke` / `setvalue` only; FocusSteal is the escape hatch. Stock UIA may still steal FG on some apps — strict mode reports `RejectedBySafety`.

## Docs

See [docs/](docs/) for requirements, architecture, ADRs, protocols, and phases.

## Clean-room

This is a clean-room implementation. Do not reverse engineer or copy proprietary multi-seat products.
