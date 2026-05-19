#!/usr/bin/env bash
# QMD bootstrap for Nhamnhi long-term memory (Phase 4: sessions + curated docs).
# Idempotent: re-running does not duplicate collections or corrupt the index.
#
# Usage: .claude/scripts/qmd-init.sh

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SESSIONS_DIR="$REPO_ROOT/.claude/agent-memory/sessions"
SESSIONS_COLLECTION="nhamnhi-sessions"
SESSIONS_CONTEXT="Saved Claude Code session transcripts for the Nhamnhi .NET microservices monorepo. Prior decisions, bug investigations, saga refactors, DLQ runbook context, gateway provider tradeoffs."
DOCS_COLLECTION="nhamnhi-docs"
DOCS_MASK="{README.md,CLAUDE.md,CONTEXT.md,AGENTS.md,docs/**/*.md,**/runbooks/**/*.md}"
DOCS_CONTEXT="Curated Nhamnhi repository documentation. Includes root project guidance, context documents, docs markdown, and runbooks. Excludes generated, vendored, template, build, and unrelated markdown outside the allowlist."

step() { printf '[qmd-init] %s\n' "$*"; }
die()  { printf '[qmd-init] ERROR: %s\n' "$*" >&2; exit 1; }

collection_exists() {
    qmd collection list 2>/dev/null | grep -Eq "(^|[[:space:]/])${1}([[:space:]]|$)"
}

ensure_collection() {
    local name="$1"
    local path="$2"
    local mask="$3"

    if collection_exists "$name"; then
        step "collection '$name' exists, skipping create"
        return 0
    fi

    step "creating collection '$name' at $path"
    qmd collection add "$path" --name "$name" --mask "$mask"
}

command -v qmd >/dev/null 2>&1 || die "'qmd' not on PATH. Install from https://github.com/tobi/qmd then re-run."

mkdir -p "$SESSIONS_DIR"

ensure_collection "$SESSIONS_COLLECTION" "$SESSIONS_DIR" "**/*.md"
ensure_collection "$DOCS_COLLECTION" "$REPO_ROOT" "$DOCS_MASK"

# Context: qmd context add is upsert-safe; re-running just overwrites the string.
step "setting context on qmd://$SESSIONS_COLLECTION"
qmd context add "qmd://$SESSIONS_COLLECTION" "$SESSIONS_CONTEXT"

step "setting context on qmd://$DOCS_COLLECTION"
qmd context add "qmd://$DOCS_COLLECTION" "$DOCS_CONTEXT"

step "qmd update"
qmd update

step "qmd embed"
qmd embed

step "done"
