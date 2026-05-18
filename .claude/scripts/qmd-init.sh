#!/usr/bin/env bash
# QMD bootstrap for Nhamnhi long-term memory (Phase 1: sessions only).
# Idempotent: re-running does not duplicate collections or corrupt the index.
#
# Usage: .claude/scripts/qmd-init.sh

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SESSIONS_DIR="$REPO_ROOT/.claude/agent-memory/sessions"
SESSIONS_COLLECTION="nhamnhi-sessions"
SESSIONS_CONTEXT="Saved Claude Code session transcripts for the Nhamnhi .NET microservices monorepo. Prior decisions, bug investigations, saga refactors, DLQ runbook context, gateway provider tradeoffs."

step() { printf '[qmd-init] %s\n' "$*"; }
die()  { printf '[qmd-init] ERROR: %s\n' "$*" >&2; exit 1; }

command -v qmd >/dev/null 2>&1 || die "'qmd' not on PATH. Install from https://github.com/tobi/qmd then re-run."

mkdir -p "$SESSIONS_DIR"

# Collection: create only if absent.
if qmd collection list 2>/dev/null | grep -Eq "(^|[[:space:]/])${SESSIONS_COLLECTION}([[:space:]]|$)"; then
    step "collection '$SESSIONS_COLLECTION' exists, skipping create"
else
    step "creating collection '$SESSIONS_COLLECTION' at $SESSIONS_DIR"
    qmd collection add "$SESSIONS_DIR" --name "$SESSIONS_COLLECTION" --mask "**/*.md"
fi

# Context: qmd context add is upsert-safe; re-running just overwrites the string.
step "setting context on qmd://$SESSIONS_COLLECTION"
qmd context add "qmd://$SESSIONS_COLLECTION" "$SESSIONS_CONTEXT"

step "qmd update"
qmd update

step "qmd embed"
qmd embed

step "done"
