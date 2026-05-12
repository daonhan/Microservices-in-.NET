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

Scenario pages:

- [01 Happy Path](scenarios/01-happy-path.md)
- [02 Stock Shortage](scenarios/02-stock-shortage.md)
- [03 Payment Decline](scenarios/03-payment-decline.md)
- [04 Admin Ops](scenarios/04-admin-ops.md)
