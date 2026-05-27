---
description: Drive grill -> PRD -> plan -> [issues] in one chain. Disk-backed, resumable, context-isolated.
argument-hint: "<slug> [--from grill|prd|plan|issues]"
allowed-tools: Bash, Read, Glob, Grep, Task, Skill, AskUserQuestion, SendMessage, Write
---

# Goal

Drive the four-skill feature-spec pipeline (`grill-me` -> `to-prd` -> `prd-to-plan` -> `to-issues`) end-to-end in one Claude Code session. Main agent runs `grill-me` interactively; spawns one isolated subagent per remaining step via `Task`; subagents return one-line `FINAL:` sentinels; main never inherits subagent context.

After issues land, print one suggestion line for `/afk-task` handoff. Do **not** auto-invoke AFK.

## Argument parsing

`$ARGUMENTS` shape: `<slug> [--from grill|prd|plan|issues]`

- **slug** (positional, REQUIRED): lowercase-dashes (e.g. `idempotent-outbox-replay`). Reject if begins with `--`. Reject if contains uppercase, spaces, or non-`[a-z0-9-]` chars. If invalid, halt and tell user the rule.
- **`--from <phase>`** (optional): one of `grill` (default), `prd`, `plan`, `issues`. Anything else -> halt.

Compute derived values once, reuse throughout:

- `<Title-Case-Slug>`: split slug on `-`, capitalize first letter of each token, rejoin with `-`. **Mechanical only. No acronym carve-outs.** Examples: `dlq-replay` -> `Dlq-Replay`, `jwt-rotation` -> `Jwt-Rotation`.
- `PRD_PATH = docs/prd/PRD-<Title-Case-Slug>.md`
- `PLAN_PATH = docs/plans/<slug>.md`
- `BRIEF_PATH = docs/prd/.brief-<slug>.md` (temp file, cleaned up after Phase 2)
- `PRD_SKILL = to-prd`
- `PLAN_SKILL = prd-to-plan`
- `ISSUES_SKILL = to-issues`

Use forward slashes in all paths passed to subagents and `gh`. Use skill names with the `Skill` tool; do not pass machine-local filesystem paths.

## Preconditions (always)

Run in parallel:

- `gh auth status` -> must succeed
- `Glob docs/prd/*.md` -> directory must exist
- `Glob docs/plans/*.md` -> directory must exist

If any fail, halt with the specific error.

Phase-specific preconditions:

| `--from` | Check before running |
|---|---|
| `grill` | none additional |
| `prd` | if `PRD_PATH` already exists, `AskUserQuestion`: overwrite? (Yes / No / Pick different slug) |
| `plan` | `Read PRD_PATH`; if missing, halt: `Missing PRD at <PRD_PATH>. Run /spec-pipeline <slug> --from prd first.` |
| `issues` | `Read PLAN_PATH`; if missing, halt: `Missing plan at <PLAN_PATH>. Run /spec-pipeline <slug> --from plan first.` Soft-warn (do not halt) if `PRD_PATH` absent. |

## Phase 1 — Grill (skipped unless `--from grill`)

1. Invoke `Skill grill-me` with the seed topic from the user's original prompt (the text accompanying the slash command, NOT the slug). Grill runs in main session. User answers live.

2. After grill completes (user signals "done" / "ship it" / similar, or grill skill exits naturally), synthesize a self-contained brief (~250-350 words) covering exactly these sections, no more:

   ```
   # Brief: <Title-Case-Slug>

   ## Problem
   <what user is trying to solve, in their words>

   ## Scope
   <what is in>

   ## Out of scope
   <what is explicitly excluded>

   ## Key decisions
   <decisions reached during grill, bulleted>

   ## Constraints
   <repo / org / tech constraints to respect>
   ```

3. `Write BRIEF_PATH` with that content. This is the ONLY artifact the `to-prd` subagent will read from grill — main's transcript is NOT visible to it. Brief quality = PRD quality.

4. Show the brief path to user. Continue to Phase 2.

## Phase 2 — PRD (skipped if `--from` in `{plan, issues}`)

Spawn `Task subagent_type=spec-step-runner` with prompt body:

```
step: prd
slug: <slug>
title_case_slug: <Title-Case-Slug>
brief_path: <BRIEF_PATH>
prd_path: <PRD_PATH>
skill_name: <PRD_SKILL>
```

**Sentinel protocol** — parse the FIRST LINE of each subagent message:

- `DRAFT_READY:` -> subagent has proposed deep-module list, paused for approval.
  - Extract module list from message body.
  - `AskUserQuestion` to user: approve as-is, or describe changes (free text).
  - `SendMessage` user's reply back to subagent. Loop until next sentinel.
- `FINAL: <abs-path> #<issue-number>` -> capture path and issue number into variables. Verify file exists. Verify `gh issue view <#>` returns a body matching the file (sanity). Delete `BRIEF_PATH`. Advance.
- `ERROR: <reason>` -> halt pipeline. Surface reason to user. Do NOT advance. Do NOT delete brief (user may want to retry).
- Anything else -> halt with `Subagent returned non-sentinel first line: <line>`.

Store: `PRD_ISSUE=<number>`. Show user: PRD file path + GH issue URL.

## Phase 3 — Plan (skipped if `--from issues`)

Preflight: `Read PRD_PATH`. If absent (only possible when `--from plan` and user lied about it existing), halt.

Spawn `Task subagent_type=spec-step-runner` with prompt body:

```
step: plan
slug: <slug>
prd_path: <PRD_PATH>
plan_path: <PLAN_PATH>
skill_name: <PLAN_SKILL>
```

Single-shot (no `DRAFT_READY:`). Expect:

- `FINAL: <abs-path>` -> verify file exists. Read it, surface 5-line summary to user (phase count, tracer-bullet titles).
- `ERROR: <reason>` -> halt.

Ask user: "Plan written to `<PLAN_PATH>`. Continue to issues, stop here, or edit plan first?" (`AskUserQuestion`). If `edit first`, halt — user re-runs `/spec-pipeline <slug> --from issues` after editing.

## Phase 4 — Issues (skipped if user chose stop or edit-first; require explicit opt-in even if `--from issues`)

Preflight: `Read PLAN_PATH`. If absent, halt.

Dedupe check: `gh issue list --label prd --search "<Title-Case-Slug>"` and `gh issue list --search "<Title-Case-Slug>" --state all`. If matches found, surface them and `AskUserQuestion`: skip / proceed anyway / abort. Do not silently double-create.

Spawn `Task subagent_type=spec-step-runner` with prompt body:

```
step: issues
slug: <slug>
plan_path: <PLAN_PATH>
prd_issue: <PRD_ISSUE if captured, else "none">
skill_name: <ISSUES_SKILL>
```

Expect:

- `FINAL: <count> issues: #<n1>,#<n2>,...` -> capture, show user.
- `ERROR: <reason>` -> halt.

Print AFK handoff line, verbatim:

```
Run /afk-task to start unattended execution.
```

Do NOT invoke `/afk-task` yourself.

## Stop conditions

Stop and surface to user without improvising when:

- Subagent returns `ERROR:` at any phase.
- Precondition file missing for the entered phase.
- Dedupe match unresolved.
- `AskUserQuestion` returns abort/cancel.
- Slug invalid (contains uppercase, spaces, special chars, or begins with `--`).
- `gh auth status` fails.
- A destructive action would be needed (e.g., overwriting an existing PRD without explicit user approval).

## Known limitations

- **Concurrent same-slug runs race.** Two simultaneous `/spec-pipeline foo` invocations will clobber each other's files. No file locking. Avoid.
- **Pre-existing PRD overwrite requires user confirm** (handled in preconditions).
- **`to-prd` skill default GH issue title** is whatever the subagent picks; we standardize on `PRD: <Title-Case-Slug>` via subagent prompt.
- **No auto-commit.** Generated `PRD-*.md` and plan file are left in working tree for the user to commit manually.
- **Brief temp file** (`docs/prd/.brief-<slug>.md`) is ignored by `.gitignore` via `docs/prd/.brief-*.md` and is deleted after Phase 2 succeeds.

## FINAL RULES

Sentinel parsing is strict — first line only, no preamble. If a subagent returns extra prose before the sentinel, halt with parse error rather than guess.

Never auto-invoke `/afk-task`. Print the suggestion line and stop.
