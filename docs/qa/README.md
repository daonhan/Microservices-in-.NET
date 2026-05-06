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

Seeded happy-path catalog data:

| Item | Value |
| --- | --- |
| Product | `product-happy` |
| Product id | `9001` |
| Price | `10.00` |
| Basket quantity | `2` |
| Warehouse | `DEFAULT` (`1`) |
| Stock on hand | `25` |

Pricing convention: scenarios that should succeed use `*.00` prices. Payment-decline scenarios in later phases use `*.99`, matching the current `InMemoryPaymentGateway` decline rule.

Run the Bruno collection from `qa/bruno` with the `qa-local` environment after the stack is healthy.

Scenario pages:

- [01 Happy Path](scenarios/01-happy-path.md)
