# Progress Tracker

## Current Phase: Phase 1 — Foundation (MVP)

### Phase 0: Planning & Setup
- [x] Architecture design
- [x] Implementation plan drafted
- [x] Daemon + TUI architecture decided
- [x] NuGet packages replacing git submodules decided
- [x] Git repo initialized
- [x] Solution and project scaffolding

### Phase 1: Foundation (MVP)
- [x] Daemon with HTTP hook server (Minimal API)
- [x] SessionStart + Stop + Notification + PromptSubmit hook endpoints
- [x] Instance registry with auto-numbering
- [x] Orchestrator with full hook lifecycle
- [x] Internal state API (GET /api/state)
- [x] HotkeyManager (SharpHook — configurable PTT bindings, default Ctrl+0-9)
- [x] PushToTalkHandler (PTT flow with event bus)
- [x] AudioInputManager (mic → Whisper STT via VoiceToText NuGet)
- [x] KeystrokeInjector (SharpHook EventSimulator)
- [x] WindowsWindowManager (Win32 P/Invoke)
- [x] TtsNotifier + VoiceAssigner (TextToVoice NuGet)
- [x] DaemonService (IHostedService)
- [x] Full DI wiring with headless mode for tests
- [x] 69 tests passing (Core + Integration + HTTP endpoints + KeyCombo + SessionEnd)
- [x] Verify WSL2 → Windows localhost connectivity (mirrored networking via .wslconfig)
- [ ] End-to-end manual test: speak → text in Claude Code prompt
- [x] Hook installation script (install/uninstall/check, preserves existing settings)

### Phase 2: Multi-Instance + TTS
- [x] Multi-instance registry with auto-numbering (done in Phase 1)
- [x] Configurable PTT hotkeys — default Ctrl+0-9, user-definable combos (done in Phase 1)
- [x] Selected-instance mode Ctrl+Space (done in Phase 1)
- [x] Instance state tracking (done in Phase 1)
- [ ] TTS notification per instance (local engine — wiring exists, needs Whisper model)
- [ ] Window targeting (match terminal ↔ instance)

### Phase 3: TUI + Full Voice
- [ ] Daemon internal API (WebSocket for real-time updates)
- [x] TUI shell (Terminal.Gui — main window, panes, status bar) — scaffolded
- [ ] Agent list view with real-time state (connect to daemon)
- [ ] Agent detail view with activity log
- [ ] Settings view
- [ ] On-demand response reading (Ctrl+Shift+N)
- [ ] ElevenLabs TTS for response reading
- [x] Hook installer script (done in Phase 1)
- [x] WSL2 networking handling (mirrored networking, done in Phase 1)

### Phase 4: Polish & Extras
- [ ] Vosk streaming STT
- [ ] Visual PTT feedback in TUI
- [ ] Audio ducking
- [ ] Activity log / transcript viewer
- [ ] Auto-reconnect
- [ ] Persistent config across daemon restarts
