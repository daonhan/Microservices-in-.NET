#!/usr/bin/env bash
# AFK commit gate. Honors <repo>/CLAUDE.md "handoff not commit" policy.
#
# Exit 0  -> hooks pass, caller may commit.
# Exit 75 -> handoff required (EX_TEMPFAIL). Caller must NOT commit,
#            must NOT use --no-verify, must NOT add Hooks-Deferred footer.
#            Issue stays open. Working tree preserved for host.
#
# Usage: .claude/scripts/afk-commit-gate.sh <issue-number>

set -euo pipefail

ISSUE_NUM="${1:?usage: afk-commit-gate.sh <issue-number>}"
REPO_ROOT="$(git rev-parse --show-toplevel)"
cd "$REPO_ROOT"

LOG_DIR="${TMPDIR:-/tmp}"
RESTORE_LOG="$LOG_DIR/afk-restore.log"
TOOL_RESTORE_LOG="$LOG_DIR/afk-tool-restore.log"
HOOK_LOG="$LOG_DIR/afk-hook.log"

# 1. Clean bin/obj per CLAUDE.md sandbox mitigation.
find . -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} + 2>/dev/null || true

# 2. Restore local .NET tools so `dotnet husky` is available in clean sandboxes.
if ! dotnet tool restore >"$TOOL_RESTORE_LOG" 2>&1; then
    echo "BLOCKER: dotnet tool restore failed" >&2
    tail -n 40 "$TOOL_RESTORE_LOG" >&2
    exit 75
fi

# 3. Restore packages. Retry once with --force on first failure (CLAUDE.md step 2).
if ! dotnet restore >"$RESTORE_LOG" 2>&1; then
    if ! dotnet restore --force >"$RESTORE_LOG" 2>&1; then
        echo "BLOCKER: dotnet restore failed after --force retry" >&2
        tail -n 40 "$RESTORE_LOG" >&2
        exit 75
    fi
fi

# 4. Run husky pre-commit group.
if dotnet husky run --group pre-commit >"$HOOK_LOG" 2>&1; then
    echo "OK: hooks pass, commit allowed for issue #$ISSUE_NUM"
    exit 0
fi

# 5. Classify failure.
if grep -q "MSB3248" "$HOOK_LOG"; then
    REASON="MSB3248 virtiofs sandbox limitation (documented non-regression)"
else
    REASON="hook failure (see $HOOK_LOG)"
fi

# 6. Post handoff comment, preserve working tree, exit non-zero.
HANDOFF_BODY=$(cat <<EOF
**AFK iteration: handoff required.**

Sandbox hook validation failed: $REASON

Working-tree changes left in place. Host steps to validate and commit:

\`\`\`bash
find . -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
dotnet tool restore
dotnet restore
dotnet husky run --group pre-commit
git add -A && git commit
\`\`\`

Per repo \`CLAUDE.md\`: no \`--no-verify\`, no \`Hooks-Deferred\` footer,
no partial commits. Handoff is the correct sandbox outcome.

Last 40 lines of hook log:
\`\`\`
$(tail -n 40 "$HOOK_LOG")
\`\`\`
EOF
)

if command -v gh >/dev/null 2>&1; then
    gh issue comment "$ISSUE_NUM" --body "$HANDOFF_BODY" >/dev/null || \
        echo "WARN: gh issue comment failed; handoff body printed below" >&2
else
    echo "WARN: gh CLI not available; handoff body printed below" >&2
fi

echo "---- HANDOFF BODY ----" >&2
echo "$HANDOFF_BODY" >&2
echo "---- END HANDOFF ----" >&2
echo "HANDOFF: issue #$ISSUE_NUM. Working tree preserved. No commit." >&2
exit 75
