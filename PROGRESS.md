# Progress Tracker

## Current Phase: Phase 0 — Planning & Setup

### Phase 0: Planning & Setup
- [x] Architecture design
- [x] Implementation plan drafted
- [x] Daemon + TUI architecture decided
- [x] NuGet packages replacing git submodules decided
- [ ] Git repo initialized
- [ ] Solution and project scaffolding
- [ ] Verify WSL2 → Windows localhost connectivity

### Phase 1: Foundation (MVP)
- [ ] Daemon with HTTP hook server (Minimal API)
- [ ] SessionStart + Stop hook endpoints
- [ ] Instance registry (single instance)
- [ ] PTT mic capture → Whisper STT (proof of concept)
- [ ] Keystroke injection to terminal window
- [ ] End-to-end: speak → text in Claude Code prompt
- [ ] Stop hook → console notification

### Phase 2: Multi-Instance + TTS
- [ ] Multi-instance registry with auto-numbering
- [ ] Numbered PTT hotkeys (Ctrl+1..9)
- [ ] Selected-instance mode (Ctrl+Space)
- [ ] TTS notification per instance (local engine)
- [ ] Window targeting (match terminal ↔ instance)
- [ ] Instance state tracking

### Phase 3: TUI + Full Voice
- [ ] Daemon internal API (REST + WebSocket)
- [ ] TUI shell (Terminal.Gui — main window, panes, status bar)
- [ ] Agent list view with real-time state
- [ ] Agent detail view with activity log
- [ ] Settings view
- [ ] On-demand response reading (Ctrl+Shift+N)
- [ ] ElevenLabs TTS for response reading
- [ ] Hook installer script
- [ ] WSL2 networking handling

### Phase 4: Polish & Extras
- [ ] Vosk streaming STT
- [ ] Visual PTT feedback in TUI
- [ ] Audio ducking
- [ ] Activity log / transcript viewer
- [ ] Auto-reconnect
- [ ] Persistent config across daemon restarts
