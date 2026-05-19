---
name: load-session-context
description: "Retrieve relevant prior Claude Code sessions and curated repo docs for the Nhamnhi monorepo from the local QMD index before continuing work. Use when the current task may build on earlier decisions, bug investigations, runbook discussions, or architectural conversations that are not visible in the current context window, e.g. saga refactors, DLQ runbook context, gateway provider tradeoffs, auth/JWT setup, outbox/event bus changes, observability instrumentation. Returns silently when nothing relevant is found."
allowed-tools: Bash(qmd *), Read
---

# load-session-context

Pull prior session context and curated repository documentation from the local QMD index for the Nhamnhi monorepo.

Collections:

- `nhamnhi-sessions` - saved Claude Code session transcripts under `.claude/agent-memory/sessions/`.
- `nhamnhi-docs` - curated repo docs from root guidance files, `docs/**/*.md`, and `**/runbooks/**/*.md`.

## Procedure

1. **Hybrid query first.** Run `qmd query "<concise query in the user's own words>" -c nhamnhi-sessions -c nhamnhi-docs -n 8 --min-score 0.3`. This checks both prior sessions and docs using hybrid retrieval.
2. **Fall back to exact search** only for unique identifiers (class names, file paths, event names, env var keys): `qmd search "<exact identifier>" -c nhamnhi-sessions -c nhamnhi-docs -n 8`.
3. **Fetch full documents** only after reviewing the hit list. Use `qmd get <docid-or-path>` to pull the body of any document that looks load-bearing for the current task. Do not fetch every hit; pick the one or two whose snippets actually match.
4. **Prefer newer sessions on conflict.** Session filenames begin with `YYYY-MM-DD`. When two sessions disagree, use the newer session's conclusion and treat the older one as superseded. When docs and sessions disagree, prefer the current source that most directly governs the task and mention the conflict briefly if it affects the recommendation.
5. **Silent on miss.** If the hit list is empty, scores are below `--min-score`, or no result looks relevant, do not invoke this skill's output into the conversation. Continue silently. Do not fabricate context.
6. **Cite recovered context.** When you do use retrieved content, name the source inline (for example `from 2026-05-18-yarp-vs-ocelot-gateway-decision-...md` or `from docs/.../runbook.md`) so the user can trace the claim back.

## When to invoke

Auto-invoke when the user's task plausibly continues prior work in the repo:

- "Why did we pick X over Y?" - architectural decisions.
- "Continue the saga refactor / DLQ poller / gateway work."
- "What was the conclusion on ..." - decision recall.
- Bug investigations that mention prior incidents or runbooks.

Skip for: trivial single-file edits, formatting fixes, brand-new features unrelated to existing code.

## Operational Notes

- This skill retrieves from QMD only. It does not refresh the index.
- Docs are refreshed by `.claude/scripts/qmd-init.sh` or by an explicit manual QMD update, not by the SessionEnd hook.
- SessionEnd refreshes only `nhamnhi-sessions`.
