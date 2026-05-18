#!/usr/bin/env bash
# Claude Code SessionEnd hook for QMD memory capture.
# Reads hook JSON from stdin, writes one redacted markdown session file, then
# refreshes the local QMD session index asynchronously.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="${CLAUDE_PROJECT_DIR:-$(cd "$SCRIPT_DIR/../.." && pwd)}"
if [[ "$REPO_ROOT" =~ ^[A-Za-z]:[\\/] ]]; then
    if command -v cygpath >/dev/null 2>&1; then
        REPO_ROOT="$(cygpath -u "$REPO_ROOT" 2>/dev/null || printf '%s\n' "$REPO_ROOT")"
    elif command -v wslpath >/dev/null 2>&1; then
        REPO_ROOT="$(wslpath -u "$REPO_ROOT" 2>/dev/null || printf '%s\n' "$REPO_ROOT")"
    fi
fi
MEMORY_DIR="$REPO_ROOT/.claude/agent-memory"
LOG_PATH="$MEMORY_DIR/qmd-index.log"
SESSIONS_COLLECTION="nhamnhi-sessions"
SESSIONS_INDEX_SCRIPT="$SCRIPT_DIR/qmd-index-sessions.mjs"

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

timestamp() {
    date -u '+%Y-%m-%dT%H:%M:%SZ'
}

make_temp_log() {
    if command -v mktemp >/dev/null 2>&1; then
        mktemp "$MEMORY_DIR/qmd-index.XXXXXX.log"
        return 0
    fi

    printf '%s\n' "$MEMORY_DIR/qmd-index.$$.$RANDOM.log"
}

append_index_log() {
    local temp_log="$1"
    local lock_dir="$LOG_PATH.lock"
    local locked=0

    for _ in {1..600}; do
        if mkdir "$lock_dir" 2>/dev/null; then
            locked=1
            break
        fi

        sleep 0.1
    done

    if [[ "$locked" -eq 1 ]]; then
        cat "$temp_log" >>"$LOG_PATH"
        rm -f "$temp_log"
        rmdir "$lock_dir"
        return 0
    fi

    {
        printf '[%s] WARNING: timed out waiting for log lock %s; appending without lock\n' "$(timestamp)" "$lock_dir"
        cat "$temp_log"
    } >>"$LOG_PATH"
    rm -f "$temp_log"
}

run_index_job() {
    local session_path="$1"
    local temp_log
    mkdir -p "$MEMORY_DIR"
    temp_log="$(make_temp_log)"

    {
        local index_status=0

        printf '== qmd index run started %s ==\n' "$(timestamp)"
        printf '[%s] repo: %s\n' "$(timestamp)" "$REPO_ROOT"
        printf '[%s] collection: %s\n' "$(timestamp)" "$SESSIONS_COLLECTION"
        printf '[%s] session: %s\n' "$(timestamp)" "$session_path"

        printf '[%s] command: node %s %s\n' "$(timestamp)" "$SESSIONS_INDEX_SCRIPT" "$SESSIONS_COLLECTION"
        (cd "$REPO_ROOT" && node "$SESSIONS_INDEX_SCRIPT" "$SESSIONS_COLLECTION") || index_status=$?
        if [[ "$index_status" -eq 0 ]]; then
            printf '[%s] command succeeded: sessions-only qmd index\n' "$(timestamp)"
        else
            printf '[%s] command failed (%s): sessions-only qmd index\n' "$(timestamp)" "$index_status"
        fi

        printf '== qmd index run finished %s status=%s ==\n\n' "$(timestamp)" "$index_status"
    } >"$temp_log" 2>&1

    append_index_log "$temp_log"
}

if [[ "${1:-}" == "--index-session" ]]; then
    run_index_job "${2:?missing session path}"
    exit 0
fi

PYTHON_BIN="$(find_python)" || die "python3 or python is required on PATH"

output_path="$("$PYTHON_BIN" "$SCRIPT_DIR/qmd_memory.py" write-session --repo-root "$REPO_ROOT")"
printf '[session-end] wrote %s\n' "$output_path" >&2

mkdir -p "$MEMORY_DIR"

BASH_BIN="${BASH:-bash}"
if command -v nohup >/dev/null 2>&1; then
    nohup "$BASH_BIN" "$SCRIPT_DIR/session-end.sh" --index-session "$output_path" >/dev/null 2>&1 &
else
    "$BASH_BIN" "$SCRIPT_DIR/session-end.sh" --index-session "$output_path" >/dev/null 2>&1 &
fi

printf '[session-end] qmd indexing started in background; logging to %s\n' "$LOG_PATH" >&2
