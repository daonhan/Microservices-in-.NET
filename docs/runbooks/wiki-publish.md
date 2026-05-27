# Wiki publish — manual sync from docs/wiki

This runbook publishes the checked-in wiki source under [`docs/wiki/`](../wiki/) to the GitHub Wiki remote at [`https://github.com/daonhan/Microservices-in-.NET.wiki.git`](https://github.com/daonhan/Microservices-in-.NET.wiki.git). The main repo is the source of truth; the wiki remote is a distribution target.

## Prerequisites

- Git CLI available on `PATH`.
- Write access to the GitHub Wiki remote.
- PowerShell 7+ for [`scripts/publish-wiki.ps1`](../../scripts/publish-wiki.ps1) or a POSIX shell for [`scripts/publish-wiki.sh`](../../scripts/publish-wiki.sh).
- A clean, reviewed main-branch checkout after the docs/wiki PR has merged.

## When to run

Run this after PR #1 of the docs/wiki refresh merges to `main`. The publish step is intentionally not wired to CI; an operator runs one script manually after reviewing the final `docs/wiki/` contents.

Do not publish from an unreviewed feature branch unless the goal is to test a private fork of the wiki remote.

## Clone strategy

The publish scripts use a temporary clone so the source tree stays untouched:

1. Shallow clone the wiki remote into a temp directory.
2. Remove all non-`.git` files from that temp clone.
3. Copy the current `docs/wiki/` contents into the clone.
4. Commit with a timestamped message that includes the main repo HEAD SHA.
5. Push the wiki branch.

If the temp clone has no changes after the copy, the scripts print `no wiki changes to publish` and exit successfully without pushing.

## Dry run

From repo root:

```powershell
./scripts/publish-wiki.ps1 -DryRun
```

```bash
./scripts/publish-wiki.sh --dry-run
```

Dry-run mode validates the repo path and prints the clone, copy, commit, and push plan. It does not clone the wiki remote, write a commit, push, or modify `docs/wiki/`.

## Publish

PowerShell:

```powershell
./scripts/publish-wiki.ps1
```

Bash:

```bash
./scripts/publish-wiki.sh
```

Both scripts exit non-zero on git failures. If a push fails, fix the remote/auth issue and rerun the same command from the reviewed checkout; do not hand-edit the wiki remote to resolve drift.

## Verification

After publish:

```bash
git ls-remote https://github.com/daonhan/Microservices-in-.NET.wiki.git HEAD
```

Open the wiki in a browser and spot-check Home, Architecture, Shared-Library, and one service page. The source files remain under `docs/wiki/`; any content fix should be made in the main repo and republished with the same script.
