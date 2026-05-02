<#
.SYNOPSIS
    Build all microservice Docker images for local Kubernetes practice.

.DESCRIPTION
    Builds product, order, basket, auth, inventory, shipping, payment, and api-gateway
    images tagged as `local/<service>:latest`, with the repo root as build context so
    that shared-libs/, Directory.Build.props, and local-nuget-packages/ are visible.

    For Minikube, run `minikube docker-env | Invoke-Expression` (or
    `& minikube -p minikube docker-env --shell powershell | Invoke-Expression`)
    BEFORE this script so images land in the cluster's image store.

.PARAMETER Tag
    Image tag suffix. Default: `latest`.

.PARAMETER Parallel
    Build images in parallel (PowerShell 7+). Default: sequential.

.EXAMPLE
    ./scripts/build-local-images.ps1
    ./scripts/build-local-images.ps1 -Tag dev
    ./scripts/build-local-images.ps1 -Parallel
#>
[CmdletBinding()]
param(
    [string]$Tag = 'latest',
    [switch]$Parallel
)

$ErrorActionPreference = 'Stop'

# Run from repo root regardless of caller's CWD
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
Push-Location $repoRoot
try {
    $services = @(
        @{ Name = 'product';    Context = 'product-microservice/Product.Service';     Image = 'productservice'    },
        @{ Name = 'order';      Context = 'order-microservice/Order.Service';         Image = 'orderservice'      },
        @{ Name = 'basket';     Context = 'basket-microservice/Basket.Service';       Image = 'basketservice'     },
        @{ Name = 'auth';       Context = 'auth-microservice/Auth.Service';           Image = 'authservice'       },
        @{ Name = 'inventory';  Context = 'inventory-microservice/Inventory.Service'; Image = 'inventoryservice'  },
        @{ Name = 'shipping';   Context = 'shipping-microservice/Shipping.Service';   Image = 'shippingservice'   },
        @{ Name = 'payment';    Context = 'payment-microservice/Payment.Service';     Image = 'paymentservice'    },
        @{ Name = 'apigateway'; Context = 'api-gateway/ApiGateway';                   Image = 'apigateway'        }
    )

    function Build-One {
        param($svc, $tag)
        $imageRef = "local/$($svc.Image):$tag"
        $dockerfile = "$($svc.Context)/Dockerfile"
        Write-Host "==> Building $imageRef from $dockerfile" -ForegroundColor Cyan
        docker build -f $dockerfile -t $imageRef .
        if ($LASTEXITCODE -ne 0) { throw "build failed for $($svc.Name)" }
    }

    if ($Parallel -and $PSVersionTable.PSVersion.Major -ge 7) {
        $services | ForEach-Object -Parallel {
            $svc = $_
            $imageRef = "local/$($svc.Image):$using:Tag"
            $dockerfile = "$($svc.Context)/Dockerfile"
            Write-Host "==> Building $imageRef from $dockerfile"
            docker build -f $dockerfile -t $imageRef .
            if ($LASTEXITCODE -ne 0) { throw "build failed for $($svc.Name)" }
        } -ThrottleLimit 4
    }
    else {
        foreach ($svc in $services) { Build-One -svc $svc -tag $Tag }
    }

    Write-Host "`nAll images built. Verify with: docker images | Select-String 'local/'" -ForegroundColor Green
}
finally {
    Pop-Location
}
