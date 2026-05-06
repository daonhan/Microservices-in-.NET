---
description: "Review changes produced by the AFK Task prompt, refine clarity without changing behavior, verify, and commit."
name: "AFK Review"
argument-hint: "[issue-number or focus] [base-branch]  e.g. '42' or 'gateway main'"
agent: "agent"
model: "GPT-5.5 (copilot)"
---

# Goal

Review code changes created by [AFK Task](./afk-task.prompt.md). Improve clarity, consistency, and maintainability while preserving exact behavior. If the changes are already clean, leave them untouched.

`${input:focus}` may contain:
- An issue number, title keyword, or focus area from the AFK task.
- Optionally, a base branch name. Default base branch is `main`.

Examples:
- `42` -> review AFK work for issue #42 against `main`
- `gateway` -> review current branch changes related to gateway work against `main`
- `84 develop` -> review issue #84 against `develop`

## 1. Context Gathering

Before changing code, gather and read:

- `git status --short --branch` — current branch and dirty state
- `git log -n 10 --format="%H%n%ad%n%B---" --date=short` — recent AFK/task commits
- `git diff <base>..HEAD` — code changes to review
- `gh issue view <issue-number>` — issue body and comments when an issue number is provided or can be inferred from the branch/commit messages
- [CLAUDE.md](../../.claude/CLAUDE.md), [repo guidance](../../CLAUDE.md), and [.github/copilot-instructions.md](../copilot-instructions.md) for project standards

If the branch has no diff against the base branch, report that there is nothing to review and stop.

If there are unrelated uncommitted user changes, do not overwrite or revert them. Stop and ask for guidance unless the review can be limited safely to the AFK changes.

## 2. Review Focus

Prioritize issues that affect maintainability while preserving behavior:

- Unnecessary complexity, nesting, or duplicated logic
- Names that obscure intent
- Logic that can be made more explicit without becoming clever
- Redundant abstractions introduced only for one use
- Comments that merely restate obvious code
- Nested ternaries or compact expressions that reduce readability
- Code that does not match nearby service style or repo conventions
- Tests that are hard to understand or do not clearly cover the AFK issue
- Verification gaps caused by the AFK task's touched service boundaries

Also check AFK-specific requirements:

- The change maps directly to the GitHub issue scope.
- It follows the repo shape: per-service `.slnx`, Minimal APIs, DTOs in `ApiModels`, domain types in `Models`.
- Cross-cutting concerns are not duplicated outside `shared-libs/ECommerce.Shared`.
- Order and Inventory saga changes consider both sides when event contracts or handlers are touched.
- Shared library changes include the required package/version workflow if consumers need them.

## 3. Balance Rules

Do not make changes that:

- Alter behavior, public contracts, data shape, migrations, event names, routes, or status codes.
- Expand scope beyond review refinements.
- Refactor unrelated code.
- Introduce new dependencies or architectural patterns.
- Replace a helpful local pattern with a personal preference.
- Hide a bug fix inside a review-only cleanup. If a behavioral bug is found, stop and report it separately.

## 4. Execution

If improvements are worth making:

1. State the issue/branch being reviewed, base branch, and the highest-value cleanup you intend to make.
2. Edit only the files required for review refinements.
3. Run the affected feedback loops from the touched service directory:

```bash
dotnet build
dotnet test
dotnet format --verify-no-changes --verbosity minimal
```

If multiple services are touched, run those commands for each affected service. If `shared-libs/ECommerce.Shared` is touched, also follow the shared library workflow from the repo guidance before validating consumers.

4. Commit the review refinements with:

```bash
git commit -am "refactor: review AFK task changes"
```

Use a more specific conventional subject when one is obvious, keeping it at 72 characters or less. Do not push.

If the code is already clean and well-structured, make no changes and do not commit.

## 5. Output

End with one of these:

- `<promise>COMPLETE</promise>` when review refinements were committed successfully.
- `<promise>NO_CHANGES</promise>` when the branch was reviewed and no improvements were warranted.
- `<promise>BLOCKED</promise>` when review cannot continue safely, followed by the blocker and the next human decision needed.

Include a concise summary of reviewed issue/branch, verification commands run, commit SHA if a commit was created, and any residual risk.
