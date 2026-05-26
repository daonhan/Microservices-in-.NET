# QA Dump Dataset

The QA dataset is loaded automatically in Development. In other environments it only runs when `Qa:Seed=true` or `Qa__Seed=true` is configured. Reset the local stack with:

```powershell
docker compose down -v
docker compose up --build
```

Plaintext passwords below are for local QA runbooks only. Do not reuse them outside this dataset.

| Persona | Id | Username | Password | Role |
| --- | --- | --- | --- | --- |
| Admin | `d854813c-4a72-4afd-b431-878cba3ecf2a` | `microservices@daonhan.com` | `oKNrqkO7iC#G` | `Administrator` |
| Happy customer | `5ff2d67e-c6b5-4870-911f-79393ed416fd` | `customer-happy@qa.test` | `oKNrqkO7iC#G` | `Customer` |
| Decline customer | `be0d0a1d-c8fe-4b17-bf6a-051e8c809aa6` | `customer-decline@qa.test` | `oKNrqkO7iC#G` | `Customer` |
| Cancel customer | `00faac97-9ae4-4b7f-b8aa-00e7c569dd66` | `customer-cancel@qa.test` | `oKNrqkO7iC#G` | `Customer` |

Seeded catalog data:

| Persona | Product | Product id | Price | Basket quantity | Stock on hand |
| --- | --- | --- | --- | --- | --- |
| Happy customer | `product-happy` | `9001` | `10.00` | `2` | `25` |
| Decline customer | `product-decline` | `9002` | `9.99` | `1` | `25` |
| Cancel customer | `product-zero-stock` | `9003` | `10.00` | `1` | `0` |
| Admin (low-stock) | `product-low-stock` | `9004` | `10.00` | — | `1` (threshold `2`) |
| Admin (restock) | `product-restock-target` | `9005` | `10.00` | — | `0` |

Default warehouse: `DEFAULT` (`1`).

Pricing convention: scenarios that should succeed use `*.00` prices. Payment-decline scenarios use `*.99`, matching the `InMemoryPaymentGateway` decline rule (cents == 99).

Run the Bruno collection from `qa/bruno` with the `qa-local` environment after the stack is healthy.
For Bruno CLI, run from a collection copy/root and pass `--env-file qa-local.bru`;
the desktop app can use the `qa-local` environment directly.

## Local Bruno CLI smoke run

Use the pinned CLI version that CI uses:

```powershell
npx --yes @usebruno/cli@3.3.0 --version
```

Start from a clean stack before each full smoke run. The happy-path order consumes
the seeded happy customer basket; rerunning without `down -v` can make
`03-get-seeded-basket` fail because the basket is already empty.

```powershell
cd <repo-root>
docker compose down -v --remove-orphans
docker compose up -d --build
```

Wait for readiness in the same order as CI: Auth first, resource services next,
gateway last.

```powershell
$ports = 8003,8002,8000,8001,8005,8006,8007,8004

foreach ($port in $ports) {
  $url = "http://localhost:$port/health/ready"
  Write-Host "Waiting for $url"

  do {
    try {
      $res = Invoke-WebRequest $url -UseBasicParsing -TimeoutSec 3
      if ($res.StatusCode -eq 200) {
        Write-Host "Ready: $url"
        break
      }
    }
    catch {
      Start-Sleep -Seconds 3
    }
  } while ($true)
}
```

For CLI runs, copy the collection to a temporary root like CI does. Do not run
directly from `qa/bruno` with `qa-local.bru` in the same folder; Bruno CLI may
try to parse `qa-local.bru` as a request and print `parseBruRequest error`.

```powershell
$repo = Resolve-Path .
$collectionRoot = Join-Path $env:TEMP "bruno-smoke-local"
$envFile = Join-Path $repo "qa\bruno\qa-local.bru"

Remove-Item $collectionRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $collectionRoot | Out-Null

Copy-Item "$repo\qa\bruno\bruno.json" $collectionRoot
Copy-Item "$repo\qa\bruno\01-happy-path" $collectionRoot -Recurse
Copy-Item "$repo\qa\bruno\02-stock-shortage" $collectionRoot -Recurse
Copy-Item "$repo\qa\bruno\03-payment-decline" $collectionRoot -Recurse
Copy-Item "$repo\qa\bruno\04-admin-ops" $collectionRoot -Recurse

cd $collectionRoot
```

Do not run the full `01-happy-path` folder straight through for local smoke
validation. Requests after order placement depend on async saga work, so use the
same setup-then-poll shape as CI:

```powershell
npx --yes @usebruno/cli@3.3.0 run `
  01-happy-path/01-login-customer.bru `
  01-happy-path/02-login-admin.bru `
  01-happy-path/03-get-seeded-basket.bru `
  01-happy-path/04-get-product-happy.bru `
  01-happy-path/05-get-inventory-happy.bru `
  01-happy-path/06-place-order.bru `
  --env-file $envFile `
  --reporter-json happy-setup.json `
  --bail
```

Extract the values needed by the poll requests:

```powershell
$run = Get-Content .\happy-setup.json -Raw | ConvertFrom-Json

$customerToken = ($run[0].results | Where-Object path -like "*01-login-customer*").response.data.token
$adminToken = ($run[0].results | Where-Object path -like "*02-login-admin*").response.data.token
$orderLocation = ($run[0].results | Where-Object path -like "*06-place-order*").response.headers.location
$orderId = ([string]$orderLocation).Trim('/').Split('/')[-1]
```

Then poll the order and shipment requests with `--env-var` values, matching the
workflow's behavior:

```powershell
do {
  npx --yes @usebruno/cli@3.3.0 run 01-happy-path/07-poll-order.bru `
    --env-file $envFile `
    --env-var customerToken=$customerToken `
    --env-var adminToken=$adminToken `
    --env-var orderId=$orderId `
    --reporter-json poll-order.json

  $poll = Get-Content .\poll-order.json -Raw | ConvertFrom-Json
  $status = $poll[0].results[0].response.data.status
  Write-Host "Order status: $status"

  if ($status -eq "Confirmed") { break }
  Start-Sleep -Milliseconds 750
} while ($true)

do {
  npx --yes @usebruno/cli@3.3.0 run 01-happy-path/08-list-shipping-by-order.bru `
    --env-file $envFile `
    --env-var customerToken=$customerToken `
    --env-var adminToken=$adminToken `
    --env-var orderId=$orderId `
    --reporter-json poll-shipment.json

  $poll = Get-Content .\poll-shipment.json -Raw | ConvertFrom-Json
  $body = $poll[0].results[0].response.data
  $shipment = @($body)[0]

  if ($shipment.shipmentId) { break }
  Write-Host "Shipment not ready yet"
  Start-Sleep -Milliseconds 750
} while ($true)

$shipmentId = $shipment.shipmentId
```

During the Bruno smoke soak, keep `qa/bruno/qa-local.bru` and the `$Qa` hash in
`scripts/local-smoke-test.ps1` in lockstep. Any PR that changes persona emails,
passwords, product IDs, customer IDs, or seeded shipment IDs must update both
surfaces so the legacy PowerShell smoke gate and the non-blocking `bruno-smoke`
job exercise the same dataset.

Bruno request files that run in CI should include a `tests` block with three
layers: expected HTTP status, fields consumed by downstream requests, and a
lightweight response-shape check using Chai assertions. Keep request-level
assertions close to the `.bru` file so a contract drift fails at the request
that observes it.

## Bruno CLI 3.3.0 form-urlencoded regression

`@usebruno/cli@3.3.0` (the CI-pinned version) drops the body of requests
declared with `body: form-urlencoded` on the wire (Content-Length: 0).
The desktop client transmits the same request correctly, so the
regression is CLI-only. Auth's `POST /token` is the only seeded surface
that needs a form body (per RFC 6749 `client_credentials`).

The workaround used by `qa/bruno/saga-operator/01-issue-service-token.bru`
is to switch the body type to `multipartForm`:

```
post {
  url: {{authBaseUrl}}/token
  body: multipartForm
  auth: none
}

body:multipart-form {
  grant_type: client_credentials
  client_id: {{serviceClientId}}
  client_secret: {{serviceClientSecret}}
}
```

Why multipart works: ASP.NET Core's `[FromForm]` model binder accepts
both `application/x-www-form-urlencoded` and `multipart/form-data` for
the same parameters, and the Auth `/token` endpoint already calls
`DisableAntiforgery()`, so the multipart envelope is parsed identically
to the form-urlencoded one on the server side.

Why not the other obvious shapes:

- `body: text` with a hand-rolled `key=value&...` payload — the CLI
  ships the body bytes verbatim and does **not** interpolate `{{var}}`
  inside `body:text` (verified empirically). The seeded credentials
  would arrive as literal `{{serviceClientId}}` strings.
- `body: form-urlencoded` — the regression we are working around.
- `bru.runRequest` in a `script:pre-request` — adds a helper request
  and ties the saga-operator chain to script-side state instead of the
  declarative body.

CI pins `@usebruno/cli@3.3.0`. Any CLI version bump must re-verify
`saga-operator/01-issue-service-token.bru` still returns 200 with a
populated `res.body.token`. If the upstream regression is fixed, the
body type can be reverted to `form-urlencoded` for clarity.

Scenario pages:

- [ASB Emulator Local Profile](asb-emulator-local.md)
- [01 Happy Path](scenarios/01-happy-path.md)
- [02 Stock Shortage](scenarios/02-stock-shortage.md)
- [03 Payment Decline](scenarios/03-payment-decline.md)
- [04 Admin Ops](scenarios/04-admin-ops.md)
- [05 Saga Operator Abort](scenarios/05-saga-operator-abort.md)
