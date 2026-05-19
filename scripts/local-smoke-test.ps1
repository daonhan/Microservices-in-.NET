<#
.SYNOPSIS
    Scenario-driven smoke test against the QA-seeded local stack via the API gateway.

.DESCRIPTION
    Drives one of the QA Bruno scenarios end-to-end against the running stack:
      - happy:     login as customer-happy, place an order against product-happy, watch
                   the saga to Confirmed, then admin-pick/pack/dispatch/deliver the shipment.
      - decline:   login as customer-decline, place an order against product-decline (priced
                   to trigger InMemoryPaymentGateway decline), assert the order ends Cancelled.
      - stock-out: login as customer-cancel, place an order against product-zero-stock,
                   assert the order ends Cancelled.
      - saga-first-transition:
                   place an order and assert Saga DB reaches StockReserved.
      - saga-happy-orchestrated:
                   place a happy-path order and assert Saga DB reaches CurrentStep=Completed,
                   Status=Completed.
      - saga-decline-orchestrated:
                   place a payment-decline order and assert Saga DB reaches Status=Compensated.
      - admin:     login as admin, hit the inventory low-stock and restock-target fixtures.
      - all:       run every scenario in order. Default.

    Each scenario only checks HTTP responses (per Phase 4 of the QA dataset PRD); SQL,
    event-bus and Jaeger checks remain manual and live in docs/qa/scenarios/.

    Constants below mirror qa/bruno/qa-local.bru. If you change one, change both — and the
    scenario will fail loudly the next time anything touches the corresponding seed.

    Prerequisite: the gateway is reachable at the given -Base URL. For local Docker:
        docker compose up -d
    For the local Kubernetes guide:
        kubectl port-forward svc/apigateway-loadbalancer 8004:8004

.PARAMETER Base
    Gateway base URL. Defaults to http://localhost:8004.

.PARAMETER Scenario
    Scenario name. One of: happy, decline, stock-out, saga-first-transition, admin, all.

.PARAMETER PollSeconds
    How long to wait for asynchronous saga state changes. Defaults to 30.
#>
[CmdletBinding()]
param(
    [string]$Base = 'http://localhost:8004',
    [ValidateSet('happy', 'decline', 'stock-out', 'saga-first-transition', 'saga-happy-orchestrated', 'saga-decline-orchestrated', 'admin', 'all')]
    [string]$Scenario = 'all',
    [int]$PollSeconds = 30,
    [string]$SqlContainer = 'sql'
)

$ErrorActionPreference = 'Stop'

# Constants — must match qa/bruno/qa-local.bru and the QaPersonas/seed migrations.
$Qa = @{
    CustomerHappyId         = '5ff2d67e-c6b5-4870-911f-79393ed416fd'
    CustomerHappyEmail      = 'customer-happy@qa.test'
    CustomerDeclineId       = 'be0d0a1d-c8fe-4b17-bf6a-051e8c809aa6'
    CustomerDeclineEmail    = 'customer-decline@qa.test'
    CustomerCancelId        = '00faac97-9ae4-4b7f-b8aa-00e7c569dd66'
    CustomerCancelEmail     = 'customer-cancel@qa.test'
    CustomerPassword        = 'oKNrqkO7iC#G'
    AdminEmail              = 'microservices@daonhan.com'
    AdminPassword           = 'oKNrqkO7iC#G'
    ProductHappyId          = 9001
    ProductDeclineId        = 9002
    ProductZeroStockId      = 9003
    ProductLowStockId       = 9004
    ProductRestockTargetId  = 9005
}

function Write-Step([string]$Tag, [string]$Message) {
    Write-Host "[$Tag] $Message" -ForegroundColor Cyan
}

function Get-AuthHeaders([string]$Token) {
    return @{ Authorization = "Bearer $Token" }
}

function Invoke-Login([string]$Email, [string]$Password) {
    $body = @{ username = $Email; password = $Password } | ConvertTo-Json
    $resp = Invoke-RestMethod "$Base/login" -Method Post -ContentType 'application/json' -Body $body
    if (-not $resp.token) {
        throw "login for $Email returned no token: $($resp | ConvertTo-Json -Depth 4)"
    }
    return $resp.token
}

function Invoke-PlaceOrder([hashtable]$Headers, [string]$CustomerId, [int]$ProductId, [int]$Quantity) {
    $body = @{ orderProducts = @(@{ productId = "$ProductId"; quantity = $Quantity }) } | ConvertTo-Json -Depth 4
    $resp = Invoke-WebRequest "$Base/order/$CustomerId" -Method Post -Headers $Headers `
        -ContentType 'application/json' -Body $body
    $location = $resp.Headers.Location
    if ($location -is [array]) { $location = $location[0] }
    $location = ([string]$location).Trim('/')
    if (-not $location) {
        throw "POST /order/$CustomerId returned no Location header"
    }
    return $location.Split('/')[-1]
}

function Wait-OrderStatus([hashtable]$Headers, [string]$CustomerId, [string]$OrderId, [string]$Expected, [int]$TimeoutSec) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $last = $null
    do {
        try {
            $order = Invoke-RestMethod "$Base/order/$CustomerId/$OrderId" -Headers $Headers
            $last = $order.status
            if ($last -eq $Expected) { return $order }
        } catch {
            $last = "lookup-error: $($_.Exception.Message)"
        }
        Start-Sleep -Milliseconds 750
    } while ((Get-Date) -lt $deadline)
    throw "order $OrderId did not reach status '$Expected' within ${TimeoutSec}s (last: '$last')"
}

function Wait-ShipmentForOrder([hashtable]$Headers, [string]$OrderId, [int]$TimeoutSec) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        try {
            $shipments = Invoke-RestMethod "$Base/shipping/by-order/$OrderId" -Headers $Headers
            $first = @($shipments)[0]
            $shipmentId = $first.shipmentId
            if (-not $shipmentId) { $shipmentId = $first.id }
            if ($shipmentId) {
                return [pscustomobject]@{ id = $shipmentId; status = $first.status }
            }
        } catch {
            # keep polling — shipment may not be created yet
        }
        Start-Sleep -Milliseconds 750
    } while ((Get-Date) -lt $deadline)
    throw "no shipment created for order $OrderId within ${TimeoutSec}s"
}

function Wait-ShipmentStatus([hashtable]$Headers, [string]$ShipmentId, [string]$Expected, [int]$TimeoutSec) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $last = $null
    do {
        try {
            $resp = Invoke-RestMethod "$Base/shipping/$ShipmentId" -Headers $Headers
            $last = $resp.status
            if ($last -eq $Expected) { return $resp }
        } catch {
            $last = "lookup-error: $($_.Exception.Message)"
        }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "shipment $ShipmentId did not reach status '$Expected' within ${TimeoutSec}s (last: '$last')"
}

function Invoke-ShipmentTransition([hashtable]$Headers, [string]$ShipmentId, [string]$Action, $Body = $null) {
    $params = @{
        Uri     = "$Base/shipping/$ShipmentId/$Action"
        Method  = 'Post'
        Headers = $Headers
    }
    if ($null -ne $Body) {
        $params.ContentType = 'application/json'
        $params.Body = ($Body | ConvertTo-Json -Depth 6)
    }
    Invoke-WebRequest @params | Out-Null
}

function Invoke-SagaSqlScalar([string]$Query) {
    $password = 'micR0S3rvice$'

    if (Get-Command 'sqlcmd' -ErrorAction SilentlyContinue) {
        $output = & sqlcmd -S 'localhost,1433' -U 'sa' -P $password -C -h -1 -W -Q $Query
        if ($LASTEXITCODE -ne 0) {
            throw "sqlcmd failed with exit code $LASTEXITCODE"
        }

        $rows = @($output) | ForEach-Object { "$_".Trim() } | Where-Object { $_ }
        return ($rows | Select-Object -First 1)
    }

    if (-not (Get-Command 'docker' -ErrorAction SilentlyContinue)) {
        throw 'Neither sqlcmd nor docker is available to query the Saga database.'
    }

    $paths = @('/opt/mssql-tools18/bin/sqlcmd', '/opt/mssql-tools/bin/sqlcmd')
    foreach ($path in $paths) {
        $output = & docker exec $SqlContainer $path -S 'localhost' -U 'sa' -P $password -C -h -1 -W -Q $Query 2>$null
        if ($LASTEXITCODE -eq 0) {
            $rows = @($output) | ForEach-Object { "$_".Trim() } | Where-Object { $_ }
            return ($rows | Select-Object -First 1)
        }
    }

    throw "Could not run sqlcmd in container '$SqlContainer'."
}

function Wait-SagaStep([string]$OrderId, [string]$Expected, [int]$TimeoutSec) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $last = $null
    $query = @"
SET NOCOUNT ON;
SELECT TOP 1 si.CurrentStep
FROM Saga.dbo.SagaInstances si
JOIN Saga.dbo.OrderSagaStates os ON os.SagaId = si.SagaId
WHERE os.OrderId = '$OrderId'
ORDER BY si.CreatedAt DESC;
"@

    do {
        try {
            $last = Invoke-SagaSqlScalar $query
            if ($last -eq $Expected) { return }
        } catch {
            $last = "lookup-error: $($_.Exception.Message)"
        }
        Start-Sleep -Milliseconds 750
    } while ((Get-Date) -lt $deadline)

    throw "saga for order $OrderId did not reach step '$Expected' within ${TimeoutSec}s (last: '$last')"
}

function Wait-SagaStatus([string]$OrderId, [string]$Expected, [int]$TimeoutSec) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $last = $null
    $query = @"
SET NOCOUNT ON;
SELECT TOP 1 si.Status
FROM Saga.dbo.SagaInstances si
JOIN Saga.dbo.OrderSagaStates os ON os.SagaId = si.SagaId
WHERE os.OrderId = '$OrderId'
ORDER BY si.CreatedAt DESC;
"@

    do {
        try {
            $last = Invoke-SagaSqlScalar $query
            if ($last -eq $Expected) { return }
        } catch {
            $last = "lookup-error: $($_.Exception.Message)"
        }
        Start-Sleep -Milliseconds 750
    } while ((Get-Date) -lt $deadline)

    throw "saga for order $OrderId did not reach status '$Expected' within ${TimeoutSec}s (last: '$last')"
}

function Invoke-Happy {
    Write-Step 'happy' 'login customer-happy'
    $cToken = Invoke-Login $Qa.CustomerHappyEmail $Qa.CustomerPassword
    $cH = Get-AuthHeaders $cToken

    Write-Step 'happy' "GET /basket/$($Qa.CustomerHappyId)"
    $basket = Invoke-RestMethod "$Base/basket/$($Qa.CustomerHappyId)" -Headers $cH
    if (-not $basket) { throw 'no seeded basket for customer-happy' }

    Write-Step 'happy' "GET /product/$($Qa.ProductHappyId)"
    $product = Invoke-RestMethod "$Base/product/$($Qa.ProductHappyId)" -Headers $cH
    if (-not $product) { throw "product-happy ($($Qa.ProductHappyId)) missing" }

    Write-Step 'happy' "POST /order/$($Qa.CustomerHappyId)"
    $orderId = Invoke-PlaceOrder $cH $Qa.CustomerHappyId $Qa.ProductHappyId 2
    Write-Step 'happy' "orderId=$orderId; polling until Confirmed"
    Wait-OrderStatus $cH $Qa.CustomerHappyId $orderId 'Confirmed' $PollSeconds | Out-Null

    Write-Step 'happy' 'login admin for shipment transitions'
    $aH = Get-AuthHeaders (Invoke-Login $Qa.AdminEmail $Qa.AdminPassword)

    Write-Step 'happy' "wait for shipment created for orderId=$orderId"
    $shipment = Wait-ShipmentForOrder $aH $orderId $PollSeconds
    $shipmentId = $shipment.id

    Write-Step 'happy' "pick shipment $shipmentId"
    Invoke-ShipmentTransition $aH $shipmentId 'pick'
    Wait-ShipmentStatus $aH $shipmentId 'Picked' 15 | Out-Null

    Write-Step 'happy' "pack shipment $shipmentId"
    Invoke-ShipmentTransition $aH $shipmentId 'pack'
    Wait-ShipmentStatus $aH $shipmentId 'Packed' 15 | Out-Null

    Write-Step 'happy' "dispatch shipment $shipmentId"
    $dispatchBody = @{
        carrierKey      = 'fake-ground'
        shippingAddress = @{
            recipient  = 'QA Happy Customer'
            line1      = '1 QA Way'
            line2      = $null
            city       = 'Testville'
            state      = 'WA'
            postalCode = '98052'
            country    = 'US'
        }
        overrideQuote   = $null
    }
    Invoke-ShipmentTransition $aH $shipmentId 'dispatch' $dispatchBody
    Wait-ShipmentStatus $aH $shipmentId 'Shipped' 15 | Out-Null

    Write-Step 'happy' "deliver shipment $shipmentId"
    Invoke-ShipmentTransition $aH $shipmentId 'deliver'
    Wait-ShipmentStatus $aH $shipmentId 'Delivered' 15 | Out-Null

    Write-Host "[happy] OK" -ForegroundColor Green
}

function Invoke-StockOut {
    Write-Step 'stock-out' 'login customer-cancel'
    $h = Get-AuthHeaders (Invoke-Login $Qa.CustomerCancelEmail $Qa.CustomerPassword)

    Write-Step 'stock-out' "GET /basket/$($Qa.CustomerCancelId)"
    $basket = Invoke-RestMethod "$Base/basket/$($Qa.CustomerCancelId)" -Headers $h
    if (-not $basket) { throw 'no seeded basket for customer-cancel' }

    Write-Step 'stock-out' "GET /inventory/$($Qa.ProductZeroStockId) (sanity)"
    $inv = Invoke-RestMethod "$Base/inventory/$($Qa.ProductZeroStockId)" -Headers $h
    if (-not $inv) { throw "product-zero-stock ($($Qa.ProductZeroStockId)) missing in inventory" }
    if ($inv.totalOnHand -gt 0) {
        throw "product-zero-stock totalOnHand=$($inv.totalOnHand); expected 0 — seed drift"
    }

    Write-Step 'stock-out' "POST /order/$($Qa.CustomerCancelId) for product-zero-stock"
    $orderId = Invoke-PlaceOrder $h $Qa.CustomerCancelId $Qa.ProductZeroStockId 1
    Write-Step 'stock-out' "orderId=$orderId; polling until Cancelled"
    Wait-OrderStatus $h $Qa.CustomerCancelId $orderId 'Cancelled' $PollSeconds | Out-Null

    Write-Host "[stock-out] OK" -ForegroundColor Green
}

function Invoke-SagaFirstTransition {
    Write-Step 'saga' 'login customer-happy'
    $h = Get-AuthHeaders (Invoke-Login $Qa.CustomerHappyEmail $Qa.CustomerPassword)

    Write-Step 'saga' "POST /order/$($Qa.CustomerHappyId) for orchestrated first transition"
    $orderId = Invoke-PlaceOrder $h $Qa.CustomerHappyId $Qa.ProductHappyId 1

    Write-Step 'saga' "orderId=$orderId; polling Saga DB until StockReserved"
    Wait-SagaStep $orderId 'StockReserved' $PollSeconds

    Write-Host "[saga-first-transition] OK" -ForegroundColor Green
}

function Invoke-SagaHappyOrchestrated {
    Write-Step 'saga-happy' 'login customer-happy'
    $cH = Get-AuthHeaders (Invoke-Login $Qa.CustomerHappyEmail $Qa.CustomerPassword)

    Write-Step 'saga-happy' "POST /order/$($Qa.CustomerHappyId)"
    $orderId = Invoke-PlaceOrder $cH $Qa.CustomerHappyId $Qa.ProductHappyId 1

    Write-Step 'saga-happy' 'login admin for shipment transitions'
    $aH = Get-AuthHeaders (Invoke-Login $Qa.AdminEmail $Qa.AdminPassword)

    Write-Step 'saga-happy' "drive shipment for orderId=$orderId so saga reaches ShipmentCreated"
    $shipment = Wait-ShipmentForOrder $aH $orderId $PollSeconds
    $shipmentId = $shipment.id
    Wait-OrderStatus $cH $Qa.CustomerHappyId $orderId 'Confirmed' $PollSeconds | Out-Null

    Write-Step 'saga-happy' "polling Saga DB until CurrentStep=Completed and Status=Completed"
    Wait-SagaStep $orderId 'Completed' $PollSeconds
    Wait-SagaStatus $orderId 'Completed' 5

    Write-Step 'saga-happy' "shipment $shipmentId reached creation; cleanup transitions"
    Invoke-ShipmentTransition $aH $shipmentId 'pick'
    Wait-ShipmentStatus $aH $shipmentId 'Picked' 15 | Out-Null

    Write-Host "[saga-happy-orchestrated] OK" -ForegroundColor Green
}

function Invoke-SagaDeclineOrchestrated {
    Write-Step 'saga-decline' 'login customer-decline'
    $h = Get-AuthHeaders (Invoke-Login $Qa.CustomerDeclineEmail $Qa.CustomerPassword)

    Write-Step 'saga-decline' "POST /order/$($Qa.CustomerDeclineId) for product-decline"
    $orderId = Invoke-PlaceOrder $h $Qa.CustomerDeclineId $Qa.ProductDeclineId 1

    Write-Step 'saga-decline' "polling Saga DB until Status=Compensated"
    Wait-SagaStatus $orderId 'Compensated' $PollSeconds

    Write-Step 'saga-decline' "polling Order DB until status=Cancelled (participant end-state)"
    Wait-OrderStatus $h $Qa.CustomerDeclineId $orderId 'Cancelled' $PollSeconds | Out-Null

    Write-Host "[saga-decline-orchestrated] OK" -ForegroundColor Green
}

function Invoke-Decline {
    Write-Step 'decline' 'login customer-decline'
    $h = Get-AuthHeaders (Invoke-Login $Qa.CustomerDeclineEmail $Qa.CustomerPassword)

    Write-Step 'decline' "GET /basket/$($Qa.CustomerDeclineId)"
    $basket = Invoke-RestMethod "$Base/basket/$($Qa.CustomerDeclineId)" -Headers $h
    if (-not $basket) { throw 'no seeded basket for customer-decline' }

    Write-Step 'decline' "GET /product/$($Qa.ProductDeclineId) (sanity)"
    $product = Invoke-RestMethod "$Base/product/$($Qa.ProductDeclineId)" -Headers $h
    if (-not $product) { throw "product-decline ($($Qa.ProductDeclineId)) missing" }

    Write-Step 'decline' "POST /order/$($Qa.CustomerDeclineId) for product-decline"
    $orderId = Invoke-PlaceOrder $h $Qa.CustomerDeclineId $Qa.ProductDeclineId 1
    Write-Step 'decline' "orderId=$orderId; polling until Cancelled (payment decline → cascade cancel)"
    Wait-OrderStatus $h $Qa.CustomerDeclineId $orderId 'Cancelled' $PollSeconds | Out-Null

    Write-Host "[decline] OK" -ForegroundColor Green
}

function Invoke-Admin {
    Write-Step 'admin' 'login admin'
    $aH = Get-AuthHeaders (Invoke-Login $Qa.AdminEmail $Qa.AdminPassword)

    Write-Step 'admin' "GET /inventory/$($Qa.ProductLowStockId) (low-stock fixture)"
    $low = Invoke-RestMethod "$Base/inventory/$($Qa.ProductLowStockId)" -Headers $aH
    if (-not $low -or $low.productId -ne $Qa.ProductLowStockId) {
        throw "product-low-stock ($($Qa.ProductLowStockId)) missing or malformed in inventory"
    }

    Write-Step 'admin' "POST /inventory/$($Qa.ProductRestockTargetId)/restock quantity=10"
    $body = @{ quantity = 10 } | ConvertTo-Json
    $resp = Invoke-RestMethod "$Base/inventory/$($Qa.ProductRestockTargetId)/restock" -Method Post `
        -Headers $aH -ContentType 'application/json' -Body $body
    if (-not $resp -or $resp.productId -ne $Qa.ProductRestockTargetId) {
        throw "restock response missing productId=$($Qa.ProductRestockTargetId)"
    }
    if ($resp.newOnHand -lt 10) {
        throw "restock newOnHand=$($resp.newOnHand); expected >= 10 — seed drift"
    }

    Write-Host "[admin] OK" -ForegroundColor Green
}

switch ($Scenario) {
    'happy'     { Invoke-Happy }
    'decline'   { Invoke-Decline }
    'stock-out' { Invoke-StockOut }
    'saga-first-transition' { Invoke-SagaFirstTransition }
    'saga-happy-orchestrated' { Invoke-SagaHappyOrchestrated }
    'saga-decline-orchestrated' { Invoke-SagaDeclineOrchestrated }
    'admin'     { Invoke-Admin }
    'all' {
        Invoke-Happy
        Invoke-StockOut
        Invoke-Decline
        Invoke-Admin
    }
}

Write-Host "smoke '$Scenario' OK" -ForegroundColor Green
