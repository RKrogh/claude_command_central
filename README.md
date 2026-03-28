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

Hold `Ctrl+1`, speak, release — your words appear in instance 1's prompt.

## Hotkey Reference

### Push-to-Talk (hold to record)

| Key | Action |
|-----|--------|
| `Ctrl+1` through `Ctrl+9` | PTT for instance N (buffers if cross-desktop) |
| `Ctrl+Shift+1` through `Ctrl+Shift+9` | Focus PTT — switch to instance N's desktop + record |
| `Ctrl+Space` | PTT for selected instance |

### Navigation

| Key | Action |
|-----|--------|
| `Shift+1` through `Shift+9` | Focus instance N (switch desktop, no recording) |
| `Ctrl+Shift+§` | Quick-back — return to previous desktop |
| `Ctrl+BackQuote` | Cycle selected instance |

### Cross-Desktop Behavior

- **Same desktop**: PTT injects text immediately
- **Different desktop**: Text is buffered and auto-injected when you switch to that desktop
- **Focus PTT** (`Ctrl+Shift+N`): Switches to the desktop first, then records — text injects directly

All bindings are configurable in `appsettings.json`.

## Optional: Attach TUI

```powershell
dotnet run --project src/CommandCentral.Tui/
```

Shows registered instances, their states, and activity log. Connects to the daemon via HTTP polling.

## API

| Endpoint | Purpose |
|----------|---------|
| `GET /health` | Daemon health check |
| `GET /api/state` | Full state snapshot (instances, selected ID) |
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
    }
  }
}
```

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
models/
  ggml-tiny.bin               Whisper model (not committed)
```

## Development

```bash
# Build
dotnet build src/CommandCentral.Daemon/

# Run tests
dotnet test src/CommandCentral.Core.Tests/
dotnet test src/CommandCentral.Integration.Tests/

# Check hooks
bash scripts/install-hooks.sh --check
```

## Known Limitations

- **Window identification**: Terminal marker approach is unreliable when multiple sessions share a desktop. First session works; subsequent sessions may not get a window handle. Active investigation.
- **Key conflicts**: Global hotkeys intercept keys system-wide. `Shift+1` blocks `!` on Nordic keyboards. A prefix-key design is planned.
- **Windows-only**: Daemon requires Windows for Win32 APIs (hotkeys, window management, virtual desktops). The architecture supports future platform adapters.

## License

MIT
