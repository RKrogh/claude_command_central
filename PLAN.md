# Claude Command Central — Implementation Plan

## 1. High-Level Architecture

The system follows a **daemon + attachable TUI** pattern. The daemon runs as a background
service handling all hook processing, hotkeys, STT/TTS, and keystroke injection. The TUI
is an optional view layer that connects to the daemon to display state and provide control.

Think `dockerd` + `docker ps` — the service does the work, the UI just shows you what's happening.

```
┌─────────────────────────────────────────────────────────────────┐
│              Command Central Daemon (background service)         │
│                                                                 │
│  ┌──────────────┐  ┌───────────┐  ┌──────────────────┐         │
│  │ Hook Server   │  │ Input     │  │ Instance         │         │
│  │ (HTTP)        │  │ Manager   │  │ Registry         │         │
│  │               │  │           │  │                  │         │
│  │ POST /stop    │  │ Global    │  │ Id, Voice,       │         │
│  │ POST /notify  │  │ Hotkeys   │  │ State, SessionId │         │
│  │ POST /start   │  │ PTT keys  │  │ Window handle    │         │
│  │ POST /prompt  │  │ Mic capture│ │ CWD, Project     │         │
│  │               │  │           │  │                  │         │
│  │ GET /api/state│  │           │  │                  │         │
│  │ WS  /api/ws   │  │           │  │                  │         │
│  └──────┬────────┘  └─────┬─────┘  └────────┬─────────┘         │
│         │                 │                  │                   │
│  ┌──────┴─────────────────┴──────────────────┴─────────┐        │
│  │                  Orchestrator                        │        │
│  │  - Maps hooks to instance state changes             │        │
│  │  - Routes STT results → keystroke injection         │        │
│  │  - Routes responses → TTS with correct voice        │        │
│  │  - Manages notification queue                       │        │
│  │  - Publishes state changes to connected TUI(s)      │        │
│  └──────┬─────────────────────────────────┬────────────┘        │
│         │                                 │                     │
│  ┌──────┴──────────┐          ┌───────────┴────────────┐        │
│  │ VoiceToText     │          │ TextToVoice            │        │
│  │ (NuGet)         │          │ (NuGet)                │        │
│  │                 │          │                        │        │
│  │ Whisper/Vosk    │          │ Piper/SherpaOnnx       │        │
│  │ NAudio mic      │          │ ElevenLabs             │        │
│  └─────────────────┘          └────────────────────────┘        │
└─────────────────────────────────────────────────────────────────┘
       ▲ HTTP hooks          │ Keystrokes        ▲ Attach/detach
       │                     ▼                   │
 ┌─────┴─────┐ ┌───────────┐ ┌───────────┐   ┌──┴──────────────┐
 │ Claude #1 │ │ Claude #2 │ │ Claude #3 │   │ TUI (optional)  │
 │ (WSL term)│ │ (WSL term)│ │ (WSL term)│   │ Terminal.Gui    │
 └───────────┘ └───────────┘ └───────────┘   └─────────────────┘
```

### Daemon ↔ TUI Communication

The daemon exposes a lightweight internal API for the TUI:
- `GET /api/state` — full snapshot of all instances + daemon status
- `WebSocket /api/ws` — real-time state change stream (instance added/removed/state changed, PTT status, activity log entries)

The TUI connects on startup and subscribes to events. Multiple TUI instances can connect
simultaneously (though one is the normal case). If no TUI is connected, the daemon operates
identically — it's purely a view layer.

## 2. TUI Layout (Terminal.Gui)

```
┌─ Command Central ──────────┬─ Agent: api-backend (#2) ─────────┐
│                            │                                    │
│  Agents                    │  Status: Busy                      │
│  ─────                     │  Project: ~/Projects/api           │
│  [1] api-backend    ● Busy │  Voice: Adam                       │
│  [2] frontend       ○ Idle │  Session: abc-123                  │
│  [3] infra          ◐ Wait │                                    │
│                            │  Recent Activity                   │
│  ● = working               │  ─────────────────                 │
│  ○ = idle                  │  14:32 Hook: Stop received         │
│  ◐ = waiting for input     │  14:31 TTS: "Instance ready"      │
│                            │  14:28 Hook: SessionStart          │
│  [S]ettings                │  14:25 STT: "fix the auth bug"    │
│  [H]otkeys                 │                                    │
│                            │                                    │
├────────────────────────────┴────────────────────────────────────┤
│ PTT: off │ Selected: #1 │ Agents: 3/9 │ Audio: ■■■□□ │ 14:33  │
└─────────────────────────────────────────────────────────────────┘
```

**Left pane** — Control panel:
- Agent list with numbered shortcuts and state indicators
- Legend for state icons
- Navigation shortcuts (Settings, Hotkeys)
- Selecting an agent updates the right pane

**Right pane** — Context-sensitive detail:
- Agent detail view (status, project, voice, session, activity log)
- Settings view (when [S] is selected on left)
- Hotkey reference (when [H] is selected)

**Bottom bar** — Summary status:
- PTT state (off / recording → target)
- Currently selected agent
- Agent count
- Audio level indicator (during PTT)
- Clock

## 3. Component Breakdown

### 3.1 Hook Server (HTTP endpoint)

Minimal API on `localhost:9000`. Receives POST requests from Claude Code hooks.

**Hook endpoints:**

| Endpoint | Hook Event | Purpose |
|----------|-----------|---------|
| `POST /hooks/session-start` | `SessionStart` | Register new instance |
| `POST /hooks/stop` | `Stop` | Response complete — trigger TTS notification |
| `POST /hooks/notification` | `Notification` | Instance idle/needs input — update state |
| `POST /hooks/prompt-submit` | `UserPromptSubmit` | Track what was sent (for context) |

**Internal API endpoints (for TUI):**

| Endpoint | Purpose |
|----------|---------|
| `GET /api/state` | Full state snapshot |
| `WebSocket /api/ws` | Real-time event stream |

Each hook request includes `session_id` in the JSON body (from Claude Code hook payload).
The manager maps `session_id` to an instance.

**Instance registration flow:**
1. Claude Code starts → `SessionStart` hook fires → POSTs to daemon
2. Daemon assigns next available instance number (1-9)
3. Daemon resolves which terminal window owns this session (by matching PID/window title)
4. Daemon assigns a voice profile to the instance
5. Connected TUI(s) receive instance-added event

### 3.2 Instance Registry

In-memory (with optional JSON persistence for restart recovery).

```csharp
record InstanceInfo
{
    required string Id;           // "1", "2", etc.
    required string SessionId;    // from Claude Code
    string? Cwd;                  // working directory
    string? ProjectName;          // derived from cwd
    IntPtr WindowHandle;          // terminal window for keystroke injection
    string VoiceProfile;          // TTS voice name/config
    InstanceState State;          // Idle, Busy, WaitingForInput
    DateTime LastActivity;
}

enum InstanceState { Idle, Busy, WaitingForInput, Disconnected }
```

### 3.3 Input Manager (Hotkeys + PTT + STT)

**Global hotkey registration** via SharpHook (cross-platform, wraps libuio):

| Key | Action |
|-----|--------|
| Hold `Ctrl+1` through `Ctrl+9` | PTT for instance N directly |
| `Ctrl+`` (backtick) | Cycle selected instance |
| Hold `Ctrl+Space` | PTT for selected instance |
| `Ctrl+Shift+M` | Mute/unmute (stop all audio) |

*Key bindings are configurable via settings file.*

**PTT flow:**
1. User presses and holds PTT key → mic capture starts (NAudio)
2. Audio buffers accumulate
3. User releases key → mic stops → audio sent to STT engine
4. Transcribed text → keystroke injection into target instance's terminal window
5. Text appears in Claude Code prompt — user reviews and hits Enter (or edits first)

**Keystroke injection:**
- Primary: SharpHook `EventSimulator` for cross-platform keystroke simulation
- Window focusing: platform adapter behind `IWindowManager` interface
  - Windows: `SetForegroundWindow` (Win32)
  - Linux: `xdotool` (alternative: `wmctrl`)
  - macOS: `osascript` / AppleScript (alternative: `cliclick`)
- Flow: focus target window → simulate keystrokes via SharpHook → text appears in prompt
- Optionally: skip foreground switch on Windows via `SendMessage`/`PostMessage` for background injection

### 3.4 TTS Output Manager

**Notification mode** (automatic on `Stop` hook):
- Short audio cue: "Instance 2 ready" or just a distinct chime per instance
- Uses local engine (Piper/SherpaOnnx) for speed and zero cost
- Non-blocking — queued playback

**Read-response mode** (on demand via hotkey):
- `Ctrl+Shift+1` through `Ctrl+Shift+9` = read last response from instance N
- Uses ElevenLabs for quality, or local engine based on user preference
- `last_assistant_message` from the Stop hook payload provides the text

**Voice assignment:**
- Each instance gets a distinct voice from a voice pool
- Configurable in settings: `{ "1": "en-adam", "2": "en-bella", ... }`
- Voices recycled when instances disconnect

### 3.5 Configuration

`appsettings.json` in the app directory:

```json
{
  "Server": {
    "Port": 9000,
    "Host": "localhost"
  },
  "Hotkeys": {
    "PttSelectedInstance": "Ctrl+Space",
    "CycleInstance": "Ctrl+OemTilde",
    "MuteAll": "Ctrl+Shift+M",
    "ReadResponse": "Ctrl+Shift+{N}"
  },
  "Stt": {
    "Engine": "Whisper",
    "ModelPath": "./models/ggml-base.en.bin",
    "Language": "en"
  },
  "Tts": {
    "NotificationEngine": "SherpaOnnx",
    "ResponseEngine": "ElevenLabs",
    "Voices": {
      "1": { "Name": "Adam", "Engine": "ElevenLabs" },
      "2": { "Name": "Bella", "Engine": "ElevenLabs" },
      "3": { "Name": "en_US-lessac-medium", "Engine": "SherpaOnnx" }
    }
  },
  "Instances": {
    "MaxInstances": 9,
    "AutoAssignVoices": true
  }
}
```

## 4. Claude Code Hook Configuration

Installed to `~/.claude/settings.json` (or project-level):

```json
{
  "hooks": {
    "SessionStart": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "curl -s -X POST http://localhost:9000/hooks/session-start -H 'Content-Type: application/json' -d \"$(cat /dev/stdin)\"",
            "timeout": 5
          }
        ]
      }
    ],
    "Stop": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "curl -s -X POST http://localhost:9000/hooks/stop -H 'Content-Type: application/json' -d \"$(cat /dev/stdin)\"",
            "timeout": 10
          }
        ]
      }
    ],
    "Notification": [
      {
        "matcher": "idle_prompt",
        "hooks": [
          {
            "type": "command",
            "command": "curl -s -X POST http://localhost:9000/hooks/notification -H 'Content-Type: application/json' -d \"$(cat /dev/stdin)\"",
            "timeout": 5
          }
        ]
      }
    ],
    "UserPromptSubmit": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "curl -s -X POST http://localhost:9000/hooks/prompt-submit -H 'Content-Type: application/json' -d \"$(cat /dev/stdin)\"",
            "timeout": 5
          }
        ]
      }
    ]
  }
}
```

Note: Hooks run in WSL, `curl` to `localhost:9000` reaches the Windows host
(WSL2 networking may need `$(hostname).local` or the Windows host IP instead of `localhost`).

## 5. Project Structure

```
claude_command_central/
├── CLAUDE.md                          # Claude Code project instructions
├── PLAN.md                            # This file
├── PROGRESS.md                        # Implementation progress tracking
├── IDEAS.md                           # Future ideas and enhancements
├── src/
│   ├── CommandCentral.sln
│   ├── CommandCentral.Core/           # Core abstractions, models, interfaces
│   │   ├── Models/
│   │   │   ├── InstanceInfo.cs
│   │   │   ├── InstanceState.cs
│   │   │   └── VoiceProfile.cs
│   │   ├── Services/
│   │   │   ├── IInstanceRegistry.cs
│   │   │   ├── IOrchestrator.cs
│   │   │   ├── IAudioInputManager.cs
│   │   │   ├── ITtsNotifier.cs
│   │   │   └── IKeystrokeInjector.cs
│   │   └── Configuration/
│   │       └── CommandCentralOptions.cs
│   ├── CommandCentral.Daemon/         # Background service — the "engine"
│   │   ├── Program.cs
│   │   ├── DaemonService.cs           # IHostedService — lifecycle management
│   │   ├── Endpoints/
│   │   │   ├── HookEndpoints.cs       # Hook routes (/hooks/*)
│   │   │   └── ApiEndpoints.cs        # TUI API routes (/api/*)
│   │   ├── Hubs/
│   │   │   └── StateHub.cs            # WebSocket hub for TUI real-time updates
│   │   └── appsettings.json
│   ├── CommandCentral.Input/          # Hotkeys, PTT, mic capture, STT
│   │   ├── HotkeyManager.cs          # SharpHook-based (cross-platform)
│   │   ├── PushToTalkHandler.cs
│   │   ├── KeystrokeInjector.cs       # SharpHook EventSimulator (cross-platform)
│   │   └── Platform/
│   │       ├── IWindowManager.cs      # Interface: focus window, enumerate windows
│   │       ├── WindowsWindowManager.cs
│   │       ├── LinuxWindowManager.cs  # xdotool-based (future)
│   │       └── MacWindowManager.cs    # osascript-based (future)
│   ├── CommandCentral.Output/         # TTS notifications and response reading
│   │   ├── TtsNotifier.cs
│   │   └── VoiceAssigner.cs
│   └── CommandCentral.Tui/           # Terminal UI — attachable view layer
│       ├── Program.cs                 # Entry point — connects to daemon
│       ├── MainWindow.cs              # Top-level Terminal.Gui window
│       ├── Views/
│       │   ├── AgentListView.cs       # Left pane — agent list + nav
│       │   ├── AgentDetailView.cs     # Right pane — agent detail
│       │   ├── SettingsView.cs        # Right pane — settings editor
│       │   └── StatusBar.cs           # Bottom bar — summary state
│       └── Services/
│           └── DaemonClient.cs        # WebSocket client to daemon
├── hooks/
│   └── claude-hooks.json              # Hook config template for installation
├── scripts/
│   └── install-hooks.ps1              # PowerShell script to install hooks
└── tests/
    ├── CommandCentral.Core.Tests/
    └── CommandCentral.Integration.Tests/
```

## 6. Implementation Phases

### Phase 1: Foundation (MVP)
**Goal:** Daemon running, single instance, basic STT → keystroke injection works end to end.

1. Set up solution structure, git repo, NuGet references
2. Build daemon with HTTP hook server (SessionStart + Stop endpoints)
3. Build instance registry (single instance)
4. Build PTT with mic capture → Whisper STT → console output (proof of concept)
5. Build keystroke injection to a terminal window
6. Wire it all together: speak → text appears in Claude Code prompt
7. Add Stop hook → console notification ("Instance ready")

### Phase 2: Multi-Instance + TTS
**Goal:** Multiple instances with voice notifications.

1. Multi-instance registry with auto-numbering
2. Numbered PTT hotkeys (Ctrl+1 through Ctrl+9)
3. Selected-instance mode with Ctrl+Space
4. TTS notification on Stop hook (local engine, per-instance voice)
5. Window targeting — resolve which terminal belongs to which instance
6. Instance state tracking (busy/idle/waiting)

### Phase 3: TUI + Full Voice
**Goal:** Attachable TUI, read responses aloud.

1. Daemon internal API (GET /api/state, WebSocket /api/ws)
2. TUI shell with Terminal.Gui — main window, panes, status bar
3. Agent list view with real-time state updates
4. Agent detail view with activity log
5. Settings view
6. On-demand response reading (Ctrl+Shift+N)
7. ElevenLabs integration for high-quality response reading
8. Hook installer script
9. WSL2 networking handling (localhost vs host IP)

### Phase 4: Polish & Extras
**Goal:** Production quality, nice-to-haves.

1. Vosk streaming STT (lower latency than Whisper batch)
2. Visual PTT feedback in TUI (status bar animation, pane highlights)
3. Audio ducking (lower other audio during TTS)
4. Activity log viewer / transcript in TUI
5. Auto-reconnect on instance crash/restart
6. Persist instance config across daemon restarts

## 7. Key Technical Challenges

### WSL2 ↔ Windows Networking
WSL2 uses a virtual network. `localhost` from WSL may not reach Windows host directly.
Solutions:
- Use `$(cat /etc/resolv.conf | grep nameserver | awk '{print $2}')` to get Windows host IP
- Or configure WSL2 mirrored networking mode (Windows 11 22H2+)
- Hook commands need to use the correct IP

### Terminal Window Identification
Need to match a Claude Code session to a specific terminal window.
Approaches:
- On SessionStart, capture the PID chain: hook → curl → shell → terminal
- Match terminal window by title (often includes CWD)
- Or: have the hook set the terminal title to include the instance ID

### Audio Device Management
- Mic input: default device or configurable
- Speaker output: default device or per-instance routing
- Handle device disconnects gracefully

### Latency Budget
Target: PTT release → text in prompt < 3 seconds
- Mic stop + buffer flush: ~100ms
- Whisper inference (base.en model): ~1-2s for typical utterance
- Keystroke injection: ~200ms
- Total: ~1.5-2.5s — acceptable

### Daemon Lifecycle
- Daemon should start automatically (Windows service or startup task)
- Graceful shutdown: drain TTS queue, persist state
- TUI disconnect should not affect daemon operation
- Single daemon instance enforced (named mutex or PID file)

## 8. Dependencies (NuGet packages)

| Package | Purpose | Notes |
|---------|---------|-------|
| `SharpHook` | Global hotkeys + keystroke simulation | Cross-platform (wraps libuio) |
| `Terminal.Gui` | TUI rendering | Multi-pane terminal UI |
| `VoiceToText` | STT (Whisper/Vosk + mic capture) | NuGet package from nuget.org |
| `TextToVoice` | TTS (Piper/SherpaOnnx/ElevenLabs) | NuGet package from nuget.org |
| `Microsoft.AspNetCore.App` | HTTP server + WebSocket | Built into .NET |
| `Microsoft.Extensions.Hosting` | Background service hosting | Built into .NET |

## 9. Portability Notes

The architecture is **Windows-first but portable by design**. Platform-specific code is isolated
behind interfaces with thin adapters.

| Layer | Cross-platform? | Platform-specific part |
|-------|:---:|---|
| Core (orchestrator, registry, models) | Yes | — |
| Daemon (HTTP server, WebSocket) | Yes | — |
| TUI (Terminal.Gui) | Yes | — |
| STT engines (Whisper, Vosk) | Yes | — |
| TTS engines (Piper, SherpaOnnx, ElevenLabs) | Yes | Only Windows SAPI is locked |
| Global hotkeys (SharpHook) | Yes | — |
| Keystroke simulation (SharpHook) | Yes | — |
| Window focusing/targeting | **No** | `IWindowManager` adapter per platform |
| Mic capture (NAudio) | **No** | `IAudioSource` adapter — swap to PortAudioSharp |

**To port**: implement `IWindowManager` and `IAudioSource` for the target platform. Everything else works.
