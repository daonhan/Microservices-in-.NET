<!-- Template is advisory; the docs CI pipeline (azure-pipelines.docs.yml) is enforcing. -->

## Summary

<!-- One or two sentences: what changed and why. -->

## Linked issues

<!-- e.g. Closes #123, Refs #456 -->

## Test plan

- [ ] Unit / integration tests added or updated
- [ ] Manual verification described below (commands, expected output)

## Docs updated?

Tick each surface you touched, or `N/A` if not applicable.

- [ ] `README.md`
- [ ] `CONTEXT.md`
- [ ] `AGENTS.md`
- [ ] `.github/copilot-instructions.md`
- [ ] `docs/wiki/Home.md`
- [ ] `docs/wiki/Architecture.md`
- [ ] `docs/wiki/Diagram-Saga.md`
- [ ] `docs/wiki/Integration-Events.md`
- [ ] `docs/wiki/Service-*.md` (the affected service page(s))
- [ ] `docs/wiki/Roadmap.md`

If you skipped a surface above, justify it in one line.

## Drift self-check

- [ ] `scripts/check-doc-drift.sh` (or `.ps1`) ran locally and exited 0
- [ ] No banned phrases reintroduced outside `scripts/doc-drift-allowlist.txt`
