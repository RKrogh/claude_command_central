# Ideas & Future Enhancements

## Voice & Audio
- **Wake word**: Instead of PTT, say "Hey Claude" + instance number to activate
- **Voice commands**: "read that again", "louder", "stop" as meta-commands to the manager
- **Speaker identification**: Recognize who's speaking if multiple users share a machine
- **Ambient mode**: Always-listening with VAD (voice activity detection), auto-routes to selected instance
- **Audio ducking**: Lower music/other audio when TTS speaks

## Instance Management
- **Auto-naming**: Derive instance names from project/cwd ("the API project", "the frontend")
- **Instance groups**: Group related instances, address them as a group
- **Priority instances**: Some instances get audio priority over others
- **Instance persistence**: Remember instance ↔ voice mappings across restarts
- **Health monitoring**: Detect crashed/hung instances, alert user

## UI & UX
- **Terminal theme inheritance**: TUI should use the user's terminal font/colors rather than Terminal.Gui defaults. Consider upgrading to Terminal.Gui v2 for better theme support, or implement a theme system that reads from terminal capabilities.
- **Overlay widget**: Small floating window showing instance states, active PTT, etc.
- **LED strip integration**: Physical LEDs per instance (USB LED strip) — green=idle, red=busy, blue=speaking
- **Stream Deck integration**: Physical buttons for PTT per instance
- **Taskbar badges**: Show instance state on taskbar icons

## Integration
- **Claude Code MCP server**: Expose manager as an MCP tool so Claude can call back into the manager
- **Clipboard mode**: Option to put STT text on clipboard instead of keystroke injection
- **Macro support**: "Deploy instance 2" → sends a predefined sequence of commands
- **Inter-instance routing**: "Tell instance 2 to run the tests" — manager relays commands
- **Webhook forwarding**: Forward specific hook events to external services (Slack, Discord)

## Security
- **API key / shared secret**: Hook requests include an `Authorization` header with a shared secret. Daemon rejects requests without it. Secret auto-generated on first run, stored in appsettings, and injected into hook curl commands by the install script. Prevents rogue processes from sending fake hooks even on localhost.
- **Rate limiting**: Throttle hook endpoints to prevent abuse (e.g. max 10 requests/second per session)

## Technical
- **WASM STT**: Run Whisper in browser-based UI instead of native
- **GPU acceleration**: Use CUDA/DirectML for faster Whisper inference
- **Model switching**: Auto-select STT model size based on utterance length
- **Noise cancellation**: Pre-process mic input with RNNoise before STT
- **Multi-language**: Auto-detect language or switch per-instance
