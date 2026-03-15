#!/usr/bin/env bash
# Install/uninstall Command Central hooks into Claude Code settings.
# Usage:
#   ./install-hooks.sh           # Install hooks
#   ./install-hooks.sh --check   # Check if hooks are installed
#   ./install-hooks.sh --uninstall  # Remove hooks
#   ./install-hooks.sh --port 8080  # Use custom daemon port

set -euo pipefail

# Resolve project root
if [[ -n "${CC_ROOT:-}" ]]; then
    PROJECT_ROOT="$CC_ROOT"
elif SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]:-$0}")" 2>/dev/null && pwd)"; then
    PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
else
    PROJECT_ROOT="$(git rev-parse --show-toplevel 2>/dev/null)" || {
        echo "ERROR: Cannot determine project root. Set CC_ROOT env var."
        exit 1
    }
fi

HOOKS_FILE="$PROJECT_ROOT/hooks/claude-hooks.json"
SETTINGS_FILE="$HOME/.claude/settings.json"
DAEMON_PORT=9000

# Parse arguments
ACTION="install"
while [[ $# -gt 0 ]]; do
    case "$1" in
        --check)    ACTION="check"; shift ;;
        --uninstall) ACTION="uninstall"; shift ;;
        --port)     DAEMON_PORT="$2"; shift 2 ;;
        -h|--help)
            echo "Usage: $0 [--check|--uninstall] [--port PORT]"
            echo ""
            echo "  --check      Check if hooks are installed"
            echo "  --uninstall  Remove Command Central hooks"
            echo "  --port PORT  Daemon port (default: 9000)"
            exit 0
            ;;
        *) echo "Unknown option: $1"; exit 1 ;;
    esac
done

# Verify hooks template exists
if [[ ! -f "$HOOKS_FILE" ]]; then
    echo "ERROR: Hooks template not found at $HOOKS_FILE"
    exit 1
fi

# Ensure settings directory exists
mkdir -p "$(dirname "$SETTINGS_FILE")"

# Create settings file if it doesn't exist
if [[ ! -f "$SETTINGS_FILE" ]]; then
    echo '{}' > "$SETTINGS_FILE"
fi

check_hooks() {
    python3 - "$SETTINGS_FILE" "$DAEMON_PORT" <<'PYEOF'
import json, sys

settings_file = sys.argv[1]
port = sys.argv[2]

with open(settings_file) as f:
    settings = json.load(f)

hooks = settings.get("hooks", {})
expected = ["SessionStart", "Stop", "Notification", "UserPromptSubmit", "SessionEnd"]
installed = []
missing = []

for event in expected:
    entries = hooks.get(event, [])
    found = any(
        any(f"localhost:{port}/hooks/" in h.get("command", "")
            for h in entry.get("hooks", []))
        for entry in entries
    )
    if found:
        installed.append(event)
    else:
        missing.append(event)

if missing:
    print("Missing hooks: " + ", ".join(missing))
    if installed:
        print("Installed hooks: " + ", ".join(installed))
    sys.exit(1)
else:
    print("All hooks installed: " + ", ".join(installed))
    sys.exit(0)
PYEOF
}

install_hooks() {
    # Back up existing settings
    if [[ -f "$SETTINGS_FILE" ]]; then
        cp "$SETTINGS_FILE" "$SETTINGS_FILE.bak"
        echo "Backed up settings to $SETTINGS_FILE.bak"
    fi

    python3 - "$HOOKS_FILE" "$SETTINGS_FILE" "$DAEMON_PORT" <<'PYEOF'
import json, sys

hooks_file = sys.argv[1]
settings_file = sys.argv[2]
port = sys.argv[3]

with open(hooks_file) as f:
    new_hooks = json.load(f)["hooks"]

with open(settings_file) as f:
    settings = json.load(f)

# Update port in hook commands if non-default
if port != "9000":
    for event, entries in new_hooks.items():
        for entry in entries:
            for hook in entry.get("hooks", []):
                if "command" in hook:
                    hook["command"] = hook["command"].replace("localhost:9000", f"localhost:{port}")

existing_hooks = settings.get("hooks", {})

# For each hook event, remove any existing Command Central entries, then add ours
for event, new_entries in new_hooks.items():
    existing_entries = existing_hooks.get(event, [])

    # Filter out existing Command Central hooks (identified by daemon URL)
    filtered = [
        entry for entry in existing_entries
        if not any(
            "localhost:" in h.get("command", "") and "/hooks/" in h.get("command", "")
            for h in entry.get("hooks", [])
        )
    ]

    # Add our hooks
    filtered.extend(new_entries)
    existing_hooks[event] = filtered

settings["hooks"] = existing_hooks

with open(settings_file, "w") as f:
    json.dump(settings, f, indent=2)
    f.write("\n")

print(f"Installed {len(new_hooks)} hook events")
PYEOF

    echo "Hooks installed to $SETTINGS_FILE"

    # Check daemon connectivity
    if curl -sf --connect-timeout 2 "http://localhost:$DAEMON_PORT/health" >/dev/null 2>&1; then
        echo "Daemon is reachable at localhost:$DAEMON_PORT"
    else
        echo "WARNING: Daemon not reachable at localhost:$DAEMON_PORT"
        echo "  Start the daemon before launching Claude Code sessions."
    fi
}

uninstall_hooks() {
    python3 - "$SETTINGS_FILE" <<'PYEOF'
import json, sys

settings_file = sys.argv[1]

with open(settings_file) as f:
    settings = json.load(f)

hooks = settings.get("hooks", {})
removed = 0

for event in list(hooks.keys()):
    original_count = len(hooks[event])
    hooks[event] = [
        entry for entry in hooks[event]
        if not any(
            "localhost:" in h.get("command", "") and "/hooks/" in h.get("command", "")
            for h in entry.get("hooks", [])
        )
    ]
    removed += original_count - len(hooks[event])

    # Clean up empty event lists
    if not hooks[event]:
        del hooks[event]

if not hooks and "hooks" in settings:
    del settings["hooks"]

with open(settings_file, "w") as f:
    json.dump(settings, f, indent=2)
    f.write("\n")

print(f"Removed {removed} Command Central hook entries")
PYEOF

    echo "Hooks removed from $SETTINGS_FILE"
}

case "$ACTION" in
    check)     check_hooks ;;
    install)   install_hooks ;;
    uninstall) uninstall_hooks ;;
esac
