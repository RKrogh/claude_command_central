# Progress Tracker

## Current Phase: Phase 2 — Multi-Instance + TTS

### Priority: Hotkey Redesign (DONE)
- [x] Leader key redesign — only `Ctrl+Shift+BackQuote` (key left of 1) intercepted globally. All other bindings (PTT, focus, cycle, quick-back, mute) activate only during a 2s leader window. State machine: Idle → LeaderActive → PttActive. No more keyboard hijacking. FocusPtt bindings dropped (use focus then PTT separately). KeyCombo.Matches now rejects extra Ctrl modifier for plain-key bindings.

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
- [x] HotkeyManager (SharpHook SimpleGlobalHook — configurable PTT bindings, default Ctrl+0-9)
- [x] PushToTalkHandler (PTT flow with event bus)
- [x] AudioInputManager (mic → Whisper STT via VoiceToText NuGet)
- [x] KeystrokeInjector (SharpHook EventSimulator)
- [x] WindowsWindowManager (Win32 P/Invoke)
- [x] TtsNotifier + VoiceAssigner (TextToVoice NuGet)
- [x] DaemonService (IHostedService)
- [x] Full DI wiring with headless mode for tests
- [x] 70 tests passing (Core + Integration + HTTP endpoints + KeyCombo + SessionEnd)
- [x] Verify WSL2 → Windows localhost connectivity (mirrored networking via .wslconfig)
- [x] End-to-end manual test: speak → text in Claude Code prompt
- [x] Hook installation script (install/uninstall/check, preserves existing settings)
- [x] PTT key suppression (keys don't leak to focused app)
- [x] Whisper language pinning (configurable, defaults to "en")
- [x] Whisper.net.Runtime dependency (native library)
- [x] Cross-platform hook installer (PowerShell with WSL symlink detection + bash)

### Phase 2: Multi-Instance + TTS
- [x] Multi-instance registry with auto-numbering (done in Phase 1)
- [x] Configurable PTT hotkeys — default Ctrl+0-9, user-definable combos (done in Phase 1)
- [x] Selected-instance mode Ctrl+Space (done in Phase 1)
- [x] Instance state tracking (done in Phase 1)
- [x] Window targeting via terminal marker (partially working — see Known Issues)
- [x] Window focus + keystroke injection (AttachThreadInput + SetForegroundWindow)
- [x] GetForegroundWindow capture as fallback
- [x] Virtual desktop awareness (IVirtualDesktopManager COM interop)
- [x] Buffered PTT — cross-desktop text buffered, auto-injected on desktop switch
- [x] Focus-only bindings (Shift+N — switch to instance N's desktop)
- [x] Focus PTT (Ctrl+Shift+N — switch desktop + record)
- [x] Quick-back (Ctrl+Shift+§ — return to previous desktop)
- [x] Key modifier exclusivity (Ctrl+1 won't fire when Ctrl+Shift+1 pressed)
- [ ] TTS notification per instance (local engine — wiring exists, needs model)

### Phase 3: TUI + Full Voice
- [ ] Daemon internal API (WebSocket for real-time updates)
- [x] TUI shell (Terminal.Gui — main window, panes, status bar) — scaffolded
- [ ] Agent list view with real-time state (connect to daemon)
- [ ] Agent detail view with activity log
- [ ] Settings view
- [ ] On-demand response reading (Ctrl+Shift+N)
- [ ] ElevenLabs TTS for response reading

### Phase 4: Polish & Extras
- [ ] Vosk streaming STT
- [ ] Visual PTT feedback in TUI
- [ ] Audio ducking
- [ ] Activity log / transcript viewer
- [ ] Auto-reconnect
- [ ] Persistent config across daemon restarts

### Known Issues
- **Window marker unreliable**: Terminal title marker (`cc:<hex>`) fails consistently — Claude Code resets the title before daemon can match. Instance 2+ on the same desktop may get `window: 0x0`. Foreground fallback only claims one window per desktop. Needs a different identification approach.
- ~~**Key binding conflicts**~~: Resolved — leader key redesign (only `Ctrl+Shift+BackQuote` intercepted globally).
