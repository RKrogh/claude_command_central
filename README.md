# Claude Command Central

A Windows control plane for managing multiple Claude Code terminal instances with voice input, cross-desktop navigation, and real-time monitoring.

Think `dockerd` + `docker ps` — the daemon handles hotkeys, speech-to-text, and keystroke injection while your Claude Code sessions stay in their own terminals.

## How It Works

```
                    Command Central Daemon (Windows)
                    ┌──────────────────────────────┐
                    │  HTTP Hook Server (:9000)     │
  Claude Code #1 ──│  Global Hotkeys (SharpHook)   │── Keystroke Injection
  Claude Code #2 ──│  Whisper STT (VoiceToText)    │── TTS Notifications
  Claude Code #3 ──│  Virtual Desktop Management   │── Cross-Desktop PTT
                    └──────────────────────────────┘
                              ▲
                    TUI (Terminal.Gui) — optional
```

1. Claude Code sessions run in WSL terminals as usual
2. Hooks fire on lifecycle events (session start, response complete, etc.) and notify the daemon via HTTP
3. Hold a PTT key, speak — your words appear in the target Claude Code prompt
4. Navigate between sessions on different virtual desktops with a single keystroke

## Prerequisites

- **Windows 10/11** with virtual desktop support
- **.NET 10 SDK** ([install](https://dot.net/download))
- **WSL2** with mirrored networking (for Claude Code hooks to reach localhost)
- **Whisper model** — download `ggml-tiny.bin` (or `ggml-base.en.bin` for better accuracy)
- **Piper TTS voice model** (optional) — for local TTS notifications, see below

### WSL2 Mirrored Networking

Create/edit `C:\Users\<you>\.wslconfig`:
```ini
[wsl2]
networkingMode=mirrored
```
Then `wsl --shutdown` and reopen WSL. This lets `localhost:9000` in WSL reach the Windows daemon.

### Whisper Model

Download a model to `models/`:
```powershell
mkdir models
Invoke-WebRequest -Uri "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin" -OutFile "models/ggml-tiny.bin"
```

For better accuracy (especially with accents), use `ggml-base.en.bin` instead.

### TTS Voice Model (optional)

TTS notifications ("instance 2 ready", "done") run on a local [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) engine using Piper VITS voice models. No model, no problem: the daemon starts fine, logs one warning telling you what to download, and skips speech until a model appears.

Download the default voice (`en_US-lessac-medium`, ~64 MB) into `models/tts/`:

From WSL:
```bash
bash scripts/download-tts-model.sh
```

Or from PowerShell:
```powershell
pwsh scripts/download-tts-model.ps1
```

Each slot (1-9) is auto-assigned its own voice. Download more voices for distinct per-instance voices — assignments persist across restarts:

```bash
bash scripts/download-tts-model.sh --voice en_US-amy-medium
bash scripts/download-tts-model.sh --list   # show the auto-assignment order
```

Manual download: grab `vits-piper-<voice>.tar.bz2` from the [sherpa-onnx tts-models release](https://github.com/k2-fsa/sherpa-onnx/releases/tag/tts-models) and extract into `models/tts/` so that `models/tts/vits-piper-<voice>/<voice>.onnx` exists (alongside `tokens.txt` and `espeak-ng-data/`).

**Cloud alternative (Voxtral):** set `Tts:NotificationEngine` to `Voxtral` and store your Mistral API key outside source control:
```powershell
dotnet user-secrets set "CommandCentral:Voxtral:ApiKey" "<your-key>" --project src/CommandCentral.Daemon/
```
Voxtral adds zero-shot voice cloning via personality `voiceRef` audio clips, at the cost of cloud latency and an API bill.

## Quick Start

### 1. Install hooks into Claude Code

From WSL:
```bash
bash scripts/install-hooks.sh
```

Or from PowerShell (auto-detects WSL symlinks):
```powershell
pwsh scripts/install-hooks.ps1
```

### 2. Start the daemon

From PowerShell:
```powershell
dotnet run --project src/CommandCentral.Daemon/
```

### 3. Start Claude Code sessions

Open Claude Code in WSL terminals as usual. Each session auto-registers with the daemon via the SessionStart hook.

### 4. Use PTT

Press `Ctrl+Shift+Q` (leader key), then hold `1`, speak, release — your words appear in instance 1's prompt.

## Hotkey Reference

All hotkeys use a **leader key** pattern: press `Ctrl+Shift+Q` first to activate a 2-second command window, then press the action key. Only the leader combo is intercepted globally.

### Leader Key

| Key | Action |
|-----|--------|
| `Ctrl+Shift+Q` | Activate leader mode (2s window) |
| `Escape` | Cancel leader mode |

### Push-to-Talk (leader, then hold to record)

| Key | Action |
|-----|--------|
| `1` through `9` | PTT for instance N (buffers if cross-desktop) |
| `Space` | PTT for selected instance |

### Navigation (leader, then press)

| Key | Action |
|-----|--------|
| `Shift+1` through `Shift+9` | Focus instance N (switch desktop) |
| `BackQuote` | Quick-back to previous desktop |
| `Tab` | Cycle selected instance |
| `M` | Mute/unmute all audio |
| `R` | Rebind selected instance to the current foreground window |

### Response Reading (leader, then press)

| Key | Action |
|-----|--------|
| `Ctrl+1` through `Ctrl+9` | Read instance N's last response aloud (press again to stop) |
| `P` | Read selected instance's last response aloud (press again to stop) |

Responses are captured by the Stop hook: it extracts the last assistant message from the
session transcript (requires `jq` in WSL) and sends it to the daemon. Markdown is stripped
before speaking (code blocks become "code block omitted") and reads are capped at
`Tts:MaxResponseChars` characters (default 1500, 0 = unlimited) to bound cloud TTS cost.

Reading uses the `Tts:ResponseEngine` (default Voxtral, Mistral's cloud TTS with per-slot
voice cloning). If it is unavailable (no API key), reads fall back to the local
notification engine. Re-run `scripts/install-hooks.sh` after upgrading to get the
transcript-extraction Stop hook.

### Cross-Desktop Behavior

- **Same desktop**: PTT injects text immediately
- **Different desktop**: Text is buffered and auto-injected when you switch to that desktop

All bindings are configurable in `appsettings.json`.

## Window Identification

The daemon needs to know which terminal window belongs to each instance. Claude Code
overwrites the terminal title, so title markers alone are unreliable. Instead, the daemon
binds windows from user activity (foreground-claim binding):

1. **Session start**: title marker match (best effort), otherwise the foreground window
   is claimed — you just launched Claude Code in that terminal.
2. **Every prompt submit**: the binding is refreshed from the foreground window. You just
   typed in that terminal, so this is the strongest signal and self-heals wrong bindings.
3. **PTT on an unbound instance**: claims the foreground window as a last resort.
4. **Manual rebind**: focus the correct terminal, then leader + `R` to bind it to the
   selected instance. Manual bindings are sticky: automatic claims never overwrite
   them, only another manual rebind does.

Check current bindings via `GET /api/state` (`window`, `windowBindingSource`, `wtSession`).

**Limitation**: two instances running in tabs of the *same* Windows Terminal window share
one OS window handle and cannot be targeted individually — keystrokes go to whichever tab
is active. Run instances in separate windows for reliable targeting. The Windows Terminal
tab GUID (`WT_SESSION`) is recorded per instance for diagnostics.

## Optional: Attach TUI

```powershell
dotnet run --project src/CommandCentral.Tui/
```

Pass a different daemon URL as the first argument if needed:

```powershell
dotnet run --project src/CommandCentral.Tui/ -- http://localhost:9000
```

The TUI is a live view of the daemon. It fetches an initial snapshot over
`GET /api/state`, then subscribes to the `/api/events` WebSocket stream for
real-time updates: no polling. If the daemon restarts, the TUI shows a
disconnected status and reconnects automatically with exponential backoff
(1s doubling up to 30s).

**Left pane** — agent list: number, project name, state, window binding
(`W✓`/`W✗`), and virtual desktop (`D:xxxx`, first hex chars of the desktop id).

**Right pane** — detail for the selected agent: status, project path, voice,
session id, window/desktop binding, and a newest-first activity log
(hooks, prompts, STT/TTS events).

| Key | Action |
|-----|--------|
| `↑` / `↓` | Select agent |
| `S` | Toggle settings pane (placeholder for now) |
| `Q` | Quit (daemon keeps running) |

## API

| Endpoint | Purpose |
|----------|---------|
| `GET /health` | Daemon health check |
| `GET /api/state` | Full state snapshot (instances, selected ID, recent activity) |
| `WS /api/events` | Real-time event stream (snapshot on connect, then instance/daemon events as JSON) |
| `POST /hooks/session-start` | Claude Code SessionStart hook |
| `POST /hooks/stop` | Claude Code Stop hook |
| `POST /hooks/notification` | Claude Code Notification hook |
| `POST /hooks/prompt-submit` | Claude Code UserPromptSubmit hook |
| `POST /hooks/session-end` | Claude Code SessionEnd hook |

## Configuration

Edit `src/CommandCentral.Daemon/appsettings.json`:

```jsonc
{
  "CommandCentral": {
    "Server": { "Port": 9000 },
    "Hotkeys": {
      "PttBindings": { "Ctrl+1": "1", ... },
      "FocusBindings": { "Shift+1": "1", ... },
      "FocusPttBindings": { "Ctrl+Shift+1": "1", ... },
      "QuickBack": "Ctrl+Shift+Section"
    },
    "Stt": {
      "Language": "en",
      "ModelPath": "../../models/ggml-tiny.bin"
    },
    "Tts": {
      "NotificationEngine": "SherpaOnnx",  // SherpaOnnx (local) | Voxtral (cloud) | Disabled
      "ResponseEngine": "Voxtral",          // engine for on-demand response reading
      "MaxResponseChars": 1500,             // cap per read (cost guard), 0 = unlimited
      "Voices": {}                          // optional explicit slot → voice overrides
    },
    "LocalTts": {
      "ModelsDir": "../../models/tts",
      "DefaultVoice": "en_US-lessac-medium"
    },
    "Persistence": {
      "StateFilePath": null  // default: %LOCALAPPDATA%\CommandCentral\state.json
    }
  }
}
```

### State Persistence

User-tuned runtime state survives daemon restarts via a small JSON file (default `%LOCALAPPDATA%\CommandCentral\state.json`, configurable via `Persistence:StateFilePath`):

- **Voice assignments** — each slot keeps its assigned voice
- **Selected instance** — your last explicit selection (leader + `Tab`) is restored when that slot re-registers

Delete the file to reset. Corrupt or missing files are handled gracefully (fresh state, one log line).

## Project Structure

```
src/
  CommandCentral.Core/        Core models, interfaces, events
  CommandCentral.Daemon/      Background service + HTTP endpoints
  CommandCentral.Input/       Hotkeys, PTT, STT, keystroke injection, virtual desktop
  CommandCentral.Output/      TTS notifications
  CommandCentral.Tui/         Terminal UI (Terminal.Gui)
hooks/
  claude-hooks.json           Hook template for Claude Code
scripts/
  install-hooks.sh            Bash installer (WSL)
  install-hooks.ps1           PowerShell installer (Windows, WSL fallback)
  download-tts-model.sh       Piper TTS voice model downloader (WSL)
  download-tts-model.ps1      Piper TTS voice model downloader (Windows)
models/
  ggml-tiny.bin               Whisper model (not committed)
  tts/                        Piper TTS voice models (not committed)
```

## Development

```bash
# Build
dotnet build src/CommandCentral.slnx

# Run tests
dotnet test src/CommandCentral.slnx

# Check hooks
bash scripts/install-hooks.sh --check
```

## Known Limitations

- **Terminal tabs share a window handle**: Instances in tabs of the same Windows Terminal window cannot be targeted individually (see Window Identification). Use separate windows.
- **Key conflicts**: Global hotkeys intercept keys system-wide. `Shift+1` blocks `!` on Nordic keyboards. A prefix-key design is planned.
- **Windows-only**: Daemon requires Windows for Win32 APIs (hotkeys, window management, virtual desktops). The architecture supports future platform adapters.

## License

MIT
