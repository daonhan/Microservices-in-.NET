#!/usr/bin/env bash
# Claude Code SessionEnd hook for QMD memory capture.
# Reads hook JSON from stdin, writes one redacted markdown session file, then
# refreshes the local QMD index synchronously for Phase 2.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="${CLAUDE_PROJECT_DIR:-$(cd "$SCRIPT_DIR/../.." && pwd)}"
SESSIONS_COLLECTION="nhamnhi-sessions"

die() {
    printf '[session-end] ERROR: %s\n' "$*" >&2
    exit 1
}

find_python() {
    if [[ -n "${PYTHON:-}" ]] && command -v "$PYTHON" >/dev/null 2>&1; then
        printf '%s\n' "$PYTHON"
        return 0
    fi

    if command -v python3 >/dev/null 2>&1; then
        printf '%s\n' "python3"
        return 0
    fi

    if command -v python >/dev/null 2>&1; then
        printf '%s\n' "python"
        return 0
    fi

    return 1
}

PYTHON_BIN="$(find_python)" || die "python3 or python is required on PATH"

output_path="$("$PYTHON_BIN" "$SCRIPT_DIR/qmd_memory.py" write-session --repo-root "$REPO_ROOT")"
printf '[session-end] wrote %s\n' "$output_path" >&2

command -v qmd >/dev/null 2>&1 || die "'qmd' not on PATH; session was written but the QMD index was not refreshed"

printf '[session-end] qmd update for %s\n' "$SESSIONS_COLLECTION" >&2
qmd update

printf '[session-end] qmd embed for %s\n' "$SESSIONS_COLLECTION" >&2
qmd embed
