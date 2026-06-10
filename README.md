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
3. **PTT/focus on an unbound instance**: claims the foreground window as a last resort.
4. **Manual rebind**: focus the correct terminal, then leader + `R` to bind it to the
   selected instance.

Check current bindings via `GET /api/state` (`window`, `windowBindingSource`, `wtSession`).

**Limitation**: two instances running in tabs of the *same* Windows Terminal window share
one OS window handle and cannot be targeted individually — keystrokes go to whichever tab
is active. Run instances in separate windows for reliable targeting. The Windows Terminal
tab GUID (`WT_SESSION`) is recorded per instance for diagnostics.

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

- **Terminal tabs share a window handle**: Instances in tabs of the same Windows Terminal window cannot be targeted individually (see Window Identification). Use separate windows.
- **Key conflicts**: Global hotkeys intercept keys system-wide. `Shift+1` blocks `!` on Nordic keyboards. A prefix-key design is planned.
- **Windows-only**: Daemon requires Windows for Win32 APIs (hotkeys, window management, virtual desktops). The architecture supports future platform adapters.

## License

MIT
