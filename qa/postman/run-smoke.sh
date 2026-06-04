#!/usr/bin/env bash
# Mechanizes the human runbook in qa/postman/README.md:
#   clean stack -> wait for /health/ready on all nine services -> run the
#   smoke suite with Newman -> decide pass/fail from the JSON report.
#
# Run from anywhere; paths resolve against the repo root.
#   ./qa/postman/run-smoke.sh
#
# Env knobs:
#   RESET=0       skip "docker compose down -v && up" and run against the
#                 current stack (faster). Bounces only the gateway to reseed the
#                 DLQ operator fixtures (folder 06); the happy basket may still
#                 be consumed -> false happy-path failures; see README.
#   SKIP_BUILD=1  bring the stack up without "--build" (images already built).
#   DELAY=750     --delay-request value (must match the polling cadence).
#   REPORT=path   where to write the Newman JSON report.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

COLLECTION="qa/postman/ECommerce-Smoke.postman_collection.json"
ENVIRONMENT="qa/postman/qa-local.postman_environment.json"
REPORT="${REPORT:-$REPO_ROOT/qa/postman/out.json}"
DELAY="${DELAY:-750}"
RESET="${RESET:-1}"
SKIP_BUILD="${SKIP_BUILD:-0}"
PORTS=(8000 8001 8002 8003 8004 8005 8006 8007 8008)

newman_run() {
  if command -v newman >/dev/null 2>&1; then newman "$@"; else npx --yes newman "$@"; fi
}

if [[ "$RESET" == "1" ]]; then
  echo "=== Resetting stack (docker compose down -v) ==="
  docker compose down -v
  if [[ "$SKIP_BUILD" == "1" ]]; then
    echo "=== Starting stack (docker compose up -d) ==="
    docker compose up -d
  else
    echo "=== Starting stack (docker compose up -d --build) ==="
    docker compose up -d --build
  fi
else
  # RESET=0 keeps the running stack, but the DLQ operator fixtures (folder 06)
  # only reset to Pending when the gateway boots. Bounce just the gateway so a
  # rerun reseeds those rows instead of 409-ing on already-Replayed/Discarded
  # state. No -v, no data loss; the readiness loop below waits for it back.
  echo "=== RESET=0: restarting gateway to reseed DLQ operator fixtures ==="
  docker compose restart gateway
fi

echo "=== Waiting for /health/ready on ${#PORTS[@]} services (timeout 300s) ==="
deadline=$(( $(date +%s) + 300 ))
while :; do
  ready=0
  for p in "${PORTS[@]}"; do
    code=$(curl -s -o /dev/null -w "%{http_code}" --max-time 4 "http://localhost:$p/health/ready" || true)
    [[ "$code" == "200" ]] && ready=$((ready + 1))
  done
  echo "    $ready/${#PORTS[@]} ready"
  [[ "$ready" == "${#PORTS[@]}" ]] && break
  if [[ $(date +%s) -ge $deadline ]]; then
    echo "ERROR: services did not become healthy within 300s" >&2
    exit 1
  fi
  sleep 5
done

echo "=== Running Newman (--delay-request ${DELAY}) ==="
set +e
newman_run run "$COLLECTION" -e "$ENVIRONMENT" \
  --delay-request "$DELAY" -r cli,json --reporter-json-export "$REPORT"
newman_exit=$?
set -e

echo ""
echo "=== Verdict (from $REPORT) ==="
node -e '
const reportPath = require("path").resolve(process.argv[1]);
const r = require(reportPath).run, s = r.stats.assertions;
console.log(`requests: ${r.stats.requests.total} | assertions: ${s.total} total, ${s.failed} failed`);
if (s.failed) {
  const g = {};
  r.failures.forEach(f => {
    const k = `${f.parent && f.parent.name} / ${f.source && f.source.name} :: ${f.error.test}`;
    (g[k] = g[k] || { n: 0, m: f.error.message }).n++;
  });
  Object.entries(g).forEach(([k, v]) => console.log(`[x${v.n}] ${k} -> ${v.m}`));
  console.log("FAIL");
  process.exit(1);
}
console.log("PASS - assertions.failed == 0");
' "$REPORT"

exit $newman_exit
