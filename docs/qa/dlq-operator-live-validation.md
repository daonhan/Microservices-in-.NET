# DLQ Operator — live black-box validation

Runbook to validate the **CI-pending** acceptance criteria left open on #326 / #327 / #328 / #330.

The hermetic tests (#329, closed) already prove the equivalent behavior in-process. This runbook proves the **live** path against a real Docker stack — the `POST /login → Operator token` flow and the `newman` folder-06 run that the four issues stay open for.

> Requires Docker Desktop running. No Docker daemon → can't run this → the issues stay open (by design).

Persona + fixtures are seeded automatically at boot (`AuthQaOperatorSeeder`, `DeadLetterQaFixtureSeeder`) — see [scenarios/06-dlq-operator.md](scenarios/06-dlq-operator.md) and [ADR-0014](../adr/0014-env-gated-qa-runtime-seeders-for-operator-and-dlq.md). Seeding fires only under `ASPNETCORE_ENVIRONMENT=Development` or `Qa__Seed=true`; both are already set in `docker-compose.yaml`.

| Value | |
| --- | --- |
| Auth | `http://localhost:8003` |
| Gateway | `http://localhost:8004` |
| Operator persona | `operator@qa.test` / `oKNrqkO7iC#G` |
| List fixture GUID | `f0000000-0000-0000-0000-000000000001` |

---

## 1. Boot a clean stack

```powershell
docker compose down -v
docker compose up -d --build
```

Wait until all nine services report ready (ports 8000–8008):

```powershell
$ports = 8000..8008
do {
  $ready = ($ports | Where-Object {
    try { (Invoke-WebRequest "http://localhost:$_/health/ready" -TimeoutSec 4).StatusCode -eq 200 } catch { $false }
  }).Count
  "$ready/9 ready"; if ($ready -lt 9) { Start-Sleep 5 }
} until ($ready -eq 9)
```

## 2. `POST /login` → Operator token

```powershell
$tok = (Invoke-RestMethod -Method Post -Uri http://localhost:8003/login `
  -ContentType application/json `
  -Body '{"username":"operator@qa.test","password":"oKNrqkO7iC#G"}').token
```

Decode `user_role` and confirm it is `Operator`:

```powershell
$p = $tok.Split('.')[1].Replace('-','+').Replace('_','/')
switch ($p.Length % 4) { 2 { $p += '==' } 3 { $p += '=' } }
[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($p)) | ConvertFrom-Json | Select-Object user_role
```

Expected: `user_role : Operator`.

Sanity-check the token against the DLQ list:

```powershell
Invoke-RestMethod "http://localhost:8004/operator/api/failures?service=qa-operator" `
  -Headers @{ Authorization = "Bearer $tok" } | Select-Object -Expand items | Select-Object id,status
```

Expected: 5 rows, all `status 0` (Pending), including `f0000000-0000-0000-0000-000000000001`.

## 3. `newman` folder-06 run

```powershell
npx --yes newman run qa/postman/ECommerce-Smoke.postman_collection.json `
  -e qa/postman/qa-local.postman_environment.json `
  --folder "06 DLQ Operator" --delay-request 750 -r cli
```

Pass = `assertions failed: 0`. Folder 06 shows 5 positive + 3 negative requests green.

---

## One-shot alternative (whole suite)

Mechanizes down-v → up → wait → newman → PASS/FAIL verdict:

```bash
# Git-bash / WSL
RESET=1 ./qa/postman/run-smoke.sh
```

```powershell
# Pure PowerShell
scripts/local-smoke-test.ps1
```

## Rerun without rebuild

The gateway seeder resets the four mutating rows (`…0002`–`…0005`) to Pending on every boot, so a rerun that skips `docker compose down -v` is still green:

```bash
RESET=0 ./qa/postman/run-smoke.sh
```

## After a green run

Check the CI-pending boxes and close the issues:

| Issue | Phase | Criterion this run satisfies |
| --- | --- | --- |
| #326 | 1 seeder + login + list | clean-stack `POST /login` + newman folder-06 Phase-1 |
| #327 | 2 four endpoints | full `RESET=1` run, folder 06 all green |
| #328 | 3 Bruno + CI | `npx @usebruno/cli run qa/bruno/dlq-operator --env-file qa/bruno/qa-local.bru` green |
| #330 | 5 config + docs + ADR | full `RESET=1` run green end-to-end |

The same validation runs automatically on CI via `.github/workflows/smoke-test.yml` once the branch is merged / a PR is opened — that CI smoke pass is the canonical close-out.
