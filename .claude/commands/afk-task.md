---
description: Pick the next AFK GitHub issue(s), implement end-to-end, run feedback loops, then commit and update the issue.
argument-hint: "[count] [focus]  e.g. '3 tests' or '1' or 'gateway'"
allowed-tools: Bash, Read, Edit, Write, Glob, Grep, Task
---

# Goal

Complete AFK tasks end-to-end: select → implement → verify → commit → update issue.

## Argument parsing

`$ARGUMENTS` may contain:
- A leading integer `N` → run the full loop **N times** (default `N=1`).
- A remaining string → focus area (issue number or keyword like `tests`, `gateway`).

Examples:
- `/afk-task` → 1 task, no focus
- `/afk-task 3` → 3 tasks back-to-back
- `/afk-task 2 gateway` → 2 tasks preferring gateway-related issues
- `/afk-task 42` → ambiguous: if `42` matches an open issue number, treat as focus; otherwise treat as count. Prefer count when it is small (≤20) **and** no issue with that number exists.

After each completed task, loop back to step 1 until `N` is reached or `NO MORE TASKS`. Between tasks, ensure `git status` is clean before starting the next.

## 1. Context gathering (read-only)

Run these in parallel and read the output before deciding:

- `gh issue list --state open --json number,title,labels,body` — open issues
- `git log -n 10 --oneline` — what has been done recently
- `gh issue view <N>` for any candidate issue, to read the full body and comments

Skip issues labelled `hitl` / `human-in-the-loop` / `blocked`. Work only on AFK-eligible issues.

If no AFK issues remain, stop and report `NO MORE TASKS`.

## 2. Task selection

Pick **one** issue, prioritized in this order:

1. Critical bugfixes (label `bug` + `priority/critical`, or production-impacting)
2. Development infrastructure (tests, CI, dev scripts, type-checking, local k8s)
3. Tracer bullets for new features — smallest end-to-end vertical slice through all layers
4. Polish & quick wins
5. Refactors

If a focus argument was provided, prefer issues matching it (title, label, or body) before falling back to the priority list.

State the chosen issue number, title, and *why* you picked it before writing any code.

## 3. Exploration

Repo conventions live in [CLAUDE.md](../../CLAUDE.md) — read it before touching unfamiliar areas. Key facts to respect without re-deriving:

- Per-service `.slnx` solutions; no root `.sln`. Operate from the service directory.
- All projects target `net10.0`. `TreatWarningsAsErrors` is on — analyzer warnings break the build.
- Cross-cutting concerns belong in `shared-libs/ECommerce.Shared`, consumed as a NuGet package from `local-nuget-packages/`. Bump `<Version>` if consumers should pick up changes.
- Order ↔ Inventory saga: changes to either side must consider both. Event types in `IntegrationEvents/Events/`, handlers in `IntegrationEvents/EventHandlers/`.
- DTOs live in `ApiModels/`; domain types in `Models/`. Keep the split.
- Test names use `Given_When_Then` (CA1707 suppressed).

Use the `Explore` subagent for anything broader than a few files.

## 4. Implementation

For new features, start with a **tracer bullet**: a tiny, end-to-end slice through all layers that validates the approach early. Expand it out only after the slice works.

- Smallest change that resolves the issue. No speculative refactors. No "improvements" to adjacent code.
- Match existing style.
- Follow TDD when fixing a bug or implementing a feature: write a failing test that reproduces it, then make it pass.
- Every changed line should trace directly to the issue.

## 5. Feedback loops (must pass before commit)

Run from the affected service directory:

```bash
dotnet build               # type-check; warnings are errors
dotnet test                # full suite for the touched service
dotnet format --verify-no-changes --verbosity minimal
```

If shared-libs changed, also rebuild + repack and bump version per CLAUDE.md before testing consumers:

```bash
cd shared-libs/ECommerce.Shared
dotnet pack -c Release
dotnet nuget push bin/Release/*.nupkg -s ../../local-nuget-packages
```

Pre-commit hook (`.husky/task-runner.json`) re-runs `dotnet format`, `dotnet build`, and the basket test suite. Step 6 invokes `.claude/scripts/afk-commit-gate.sh` which runs the same hook group and routes to handoff on `MSB3248`/virtiofs sandbox failure. **Never bypass with `--no-verify`, `-c core.hooksPath=`, or `Hooks-Deferred:` footer.**

## 6. Commit (gated)

**Always** run the commit gate first. Never call `git commit` directly in this loop.

```bash
.claude/scripts/afk-commit-gate.sh <issue-number>
```

Branch on exit code:

- **Exit 0** — hooks pass. Stage and commit:
  ```bash
  git add -A
  git commit -m "<≤100-char subject describing the change>"
  ```
  - Conventional Commits style preferred (`fix:`, `feat:`, `chore:`, `refactor:`, `test:`).
  - Optional body: ≤3 bullets covering the key decision or any blocker for the next iteration.
  - No file lists. No `Co-Authored-By` footer.

- **Exit 75 (handoff)** — gate posted a handoff comment to the issue and preserved the working tree. **Do NOT commit.** Per repo `CLAUDE.md`:
  - No `--no-verify`, no `-c core.hooksPath=`, no skipping hooks.
  - No `Hooks-Deferred:` / `Validation-Deferred:` footer.
  - No partial commit.
  - Leave the issue **open**. Skip step 7 close. Go to step 8.

Do **not** `git push` — pushing is a human-confirmed step.

## 7. Update the issue

- If gate exit 0 **and** task fully complete: `gh issue close <N> --comment "<one-line summary + commit sha>"`.
- If gate exit 0 but task partially complete: `gh issue comment <N> --body "<what was done, what remains, why>"`, leave open.
- If gate exit 75: gate already posted the handoff comment. Add nothing further unless you have context the gate did not capture.

## 8. Loop or stop

- If more iterations remain in `N`, return to step 1 and pick the next issue. Print a short banner like `--- Task k/N complete, starting next ---`.
- After the final iteration, print a summary: issue numbers handled, commit SHAs, anything left open.

## Stop conditions

Stop and surface to the user — do **not** improvise — when:

- A destructive action would be needed (`git push`, `git reset --hard`, dropping data, deleting branches).
- The chosen issue requires architectural decisions not already in the issue or `docs/`.
- Feedback loops fail in a way you cannot diagnose after one focused attempt.
- `git status` is dirty at the start of a new iteration.
- `NO MORE TASKS` before reaching `N`.

## FINAL RULES

Work on ONE task at a time. Complete the full loop (steps 1-8) for the selected task before starting the next iteration. If `N > 1`, repeat sequentially; never work tasks in parallel.
