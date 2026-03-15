# Claude Command Central

## Project Overview
A Windows .NET application that acts as a central control plane for multiple Claude Code terminal instances.
Runs as a **background daemon** with an **attachable Terminal.Gui TUI** for monitoring and control.
Enables voice input (STT) targeted at specific instances and voice output (TTS) with per-instance voices.

## Architecture
- **Daemon**: .NET 10 background service running on Windows — handles all hook processing, hotkeys, STT/TTS
- **TUI**: Attachable Terminal.Gui interface — view layer that connects to daemon via WebSocket
- **HTTP server**: localhost endpoint receiving Claude Code hooks from WSL instances + internal API for TUI
- **Dependencies**: VoiceToText and TextToVoice via NuGet packages (nuget.org)
- **Claude Code integration**: Via hooks system (Stop, Notification, SessionStart, UserPromptSubmit)
- **Claude Code terminals**: Remain as-is in separate windows — Command Central is a control plane, not a replacement

## Key Design Decisions
- **Daemon + TUI**: Headless-first, TUI is optional view layer (like `dockerd` + `docker`)
- **Terminal.Gui**: Multi-pane TUI with agent list, detail view, and status bar
- Simulated keystrokes for STT injection (text appears in prompt, user can edit before sending)
- Instance selection: Both numbered PTT (hold 1-9) and selected-instance PTT coexist
- STT: Local only (Whisper/Vosk) for privacy and speed
- TTS: Hybrid — local (Piper/SherpaOnnx) for notifications, ElevenLabs on-demand for full responses
- Tool-agnostic control plane — works with Claude Code today, adaptable to other CLI tools

## Tech Stack
- .NET 10, C#, Windows-first (manages WSL Claude Code instances)
- ASP.NET Minimal API for hook endpoint + internal API
- WebSocket for daemon ↔ TUI real-time communication
- Terminal.Gui for TUI rendering
- SharpHook for global hotkeys and keystroke simulation
- VoiceToText (NuGet) for STT
- TextToVoice (NuGet) for TTS

## Coding Conventions
- File-scoped namespaces
- Primary constructors
- Nullable enabled
- Composition over inheritance
- Explicit over implicit

## Project Structure
See PLAN.md for full architecture and implementation plan.
See PROGRESS.md for current status and task tracking.
See IDEAS.md for future enhancements.

# currentDate
Today's date is 2026-03-15.
