<#
.SYNOPSIS
    End-to-end smoke test against the local Kubernetes stack via the API gateway.

.DESCRIPTION
    Logs in as the seeded administrator, creates a product, places an order
    against it, and reads the order back. Mirrors the saga in
    docs/LOCAL_K8S_GUIDE.md and Getting-Started.md.

    Prerequisite: `kubectl port-forward svc/apigateway-loadbalancer 8004:8004`
    in another terminal.

.PARAMETER Base
    Gateway base URL. Defaults to http://localhost:8004 (the port-forward).

.PARAMETER Username
    Login username. Defaults to the seeded administrator.

.PARAMETER Password
    Login password. Defaults to the seeded administrator's password.
#>
[CmdletBinding()]
param(
    [string]$Base     = 'http://localhost:8004',
    [string]$Username = 'microservices@daonhan.com',
    [string]$Password = 'oKNrqkO7iC#G'
)

$ErrorActionPreference = 'Stop'

Write-Host "==> POST $Base/login as $Username" -ForegroundColor Cyan
$loginBody = @{ Username = $Username; Password = $Password } | ConvertTo-Json
$login = Invoke-RestMethod "$Base/login" -Method Post -ContentType 'application/json' -Body $loginBody
$token = $login.token
if (-not $token) { throw "login returned no token: $($login | ConvertTo-Json -Depth 4)" }
$h = @{ Authorization = "Bearer $token" }

Write-Host "==> POST $Base/product/ (admin)" -ForegroundColor Cyan
$productBody = @{
    Name          = "smoke-$(Get-Random)"
    Price         = 9.99
    ProductTypeId = 1            # 'Shoes' (seeded)
    Description   = 'local k8s smoke test'
} | ConvertTo-Json
# /product/ POST returns 201 Created with the new id in the Location header.
$createResp = Invoke-WebRequest "$Base/product/" -Method Post -Headers $h `
    -ContentType 'application/json' -Body $productBody
$location = $createResp.Headers.Location
if ($location -is [array]) { $location = $location[0] }
$productId = [int](([string]$location).TrimStart('/'))
Write-Host "    productId = $productId"

Write-Host "==> GET $Base/product/$productId" -ForegroundColor Cyan
$product = Invoke-RestMethod "$Base/product/$productId" -Headers $h
$product | Format-List

$customerId = "smoke-$(Get-Random)"
Write-Host "==> POST $Base/order/$customerId" -ForegroundColor Cyan
$orderBody = @{
    OrderProducts = @(
        @{ ProductId = "$productId"; Quantity = 1 }
    )
} | ConvertTo-Json
$orderResp = Invoke-WebRequest "$Base/order/$customerId" -Method Post -Headers $h `
    -ContentType 'application/json' -Body $orderBody
Write-Host "    order Location: $($orderResp.Headers.Location)"

Write-Host "`n==> Saga should have run. Tail logs to confirm:" -ForegroundColor Green
Write-Host "    kubectl logs -l app=orderservice     --tail=30"
Write-Host "    kubectl logs -l app=inventoryservice --tail=30"
Write-Host "    look for: OrderCreatedEvent -> StockReserved -> OrderConfirmed"