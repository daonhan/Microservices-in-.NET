<#
.SYNOPSIS
    One-shot deploy of the local-stack microservices platform to a local
    Kubernetes cluster (Docker Desktop or Minikube).

.DESCRIPTION
    Applies infra -> observability -> microservices, with `kubectl wait`
    readiness gates between stages so dependent pods do not crash-loop.

    Assumes you have already built local images (./scripts/build-local-images.ps1)
    and replaced <USERNAME>/ with local/ in kubernetes/*.yaml.

.PARAMETER Timeout
    `kubectl wait` timeout for each pod readiness check. Default: `300s`.

.PARAMETER SkipObservability
    Skip the Jaeger/Prometheus/Grafana/Loki/OTel stack.

.PARAMETER LocalStorageFix
    Stream kubernetes/sql.yaml through an in-memory `ReadWriteMany` ->
    `ReadWriteOnce` rewrite before applying. Use this on Docker Desktop /
    Minikube where the only StorageClass (`rancher.io/local-path`) does not
    support RWX. The file on disk is NOT modified. Auto-enabled when `auto`
    is passed and no RWX-capable StorageClass is detected.

.EXAMPLE
    ./scripts/apply-local-k8s.ps1
    ./scripts/apply-local-k8s.ps1 -SkipObservability
    ./scripts/apply-local-k8s.ps1 -Timeout 600s
    ./scripts/apply-local-k8s.ps1 -LocalStorageFix
    ./scripts/apply-local-k8s.ps1 -LocalStorageFix auto
#>
[CmdletBinding()]
param(
    [string]$Timeout = '300s',
    [switch]$SkipObservability,
    [ValidateSet('off','on','auto')]
    [string]$LocalStorageFix = 'auto'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
Push-Location $repoRoot
try {
    function Wait-App {
        param([string]$App, [string]$T = $Timeout)
        kubectl wait --for=condition=ready pod -l "app=$App" --timeout=$T
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "$App did not become Ready within $T — recent events:"
            kubectl get pods -l "app=$App"
            kubectl describe pods -l "app=$App" | Select-Object -Last 30
            throw "$App readiness gate failed"
        }
    }

    function Test-RwxSupported {
        # local-path / hostpath provisioners do not support ReadWriteMany.
        $provisioners = & kubectl get storageclass -o jsonpath='{.items[*].provisioner}' 2>$null
        if ($LASTEXITCODE -ne 0 -or -not $provisioners) { return $true }  # be permissive on errors
        $rwxIncapable = @('rancher.io/local-path','docker.io/hostpath','k8s.io/minikube-hostpath')
        foreach ($p in $provisioners.Split(' ')) {
            if ($p -and -not ($rwxIncapable -contains $p)) { return $true }
        }
        return $false
    }

    function Apply-SqlManifest {
        param([bool]$RewriteRwx)
        if ($RewriteRwx) {
            Write-Host '    rewriting sql.yaml: ReadWriteMany -> ReadWriteOnce (in-memory; file unchanged)' -ForegroundColor Yellow
            $yaml = (Get-Content -Raw kubernetes/sql.yaml) -replace 'ReadWriteMany','ReadWriteOnce'
            $yaml | kubectl apply -f -
        } else {
            kubectl apply -f kubernetes/sql.yaml
        }
        if ($LASTEXITCODE -ne 0) { throw 'kubectl apply (sql) failed' }
    }

    $rewriteSql = switch ($LocalStorageFix) {
        'on'   { $true }
        'off'  { $false }
        'auto' { -not (Test-RwxSupported) }
    }

    Write-Host "==> 1/3 Infrastructure (sql, rabbitmq, redis)" -ForegroundColor Cyan
    Apply-SqlManifest -RewriteRwx:$rewriteSql
    kubectl apply -f kubernetes/rabbitmq.yaml -f kubernetes/redis.yaml
    if ($LASTEXITCODE -ne 0) { throw 'kubectl apply (infra) failed' }

    Write-Host '    waiting for infra pods to become Ready...'
    Wait-App mssql
    Wait-App rabbitmq '180s'
    Wait-App redis    '120s'

    if (-not $SkipObservability) {
        Write-Host '==> 2/3 Observability (otel, jaeger, prometheus, alertmanager, loki, grafana, exporters)' -ForegroundColor Cyan
        kubectl apply `
            -f kubernetes/otel-collector.yaml `
            -f kubernetes/jaeger.yaml `
            -f kubernetes/prometheus.yaml `
            -f kubernetes/alertmanager.yaml `
            -f kubernetes/loki.yaml `
            -f kubernetes/grafana.yaml `
            -f kubernetes/exporters.yaml
        if ($LASTEXITCODE -ne 0) { throw 'kubectl apply (observability) failed' }
    }
    else {
        Write-Host '==> 2/3 Observability skipped (-SkipObservability)' -ForegroundColor Yellow
    }

    Write-Host '==> 3/3 Microservices' -ForegroundColor Cyan
    # Filter excludes aks-dev-*.yml / aks-staging-*.yml / aks-prod-*.yml
    Get-ChildItem kubernetes -Filter '*-microservice.yaml' |
        ForEach-Object {
            kubectl apply -f $_.FullName
            if ($LASTEXITCODE -ne 0) { throw "kubectl apply failed for $($_.Name)" }
        }
    kubectl apply -f kubernetes/api-gateway.yaml
    if ($LASTEXITCODE -ne 0) { throw 'kubectl apply (api-gateway) failed' }

    # Auth dev-keys: in Development the auth service signs JWTs from PEM files
    # at /app/dev-keys. The base manifest does not commit a Secret/volume
    # for them (Development-only material), so we bootstrap it here.
    $devKeyDir = 'auth-microservice/Auth.Service/dev-keys'
    if ((Test-Path "$devKeyDir/dev-private.pem") -and (Test-Path "$devKeyDir/dev-public.pem")) {
        Write-Host '    bootstrapping auth-dev-keys Secret + volume mount' -ForegroundColor Yellow
        kubectl create secret generic auth-dev-keys `
            --from-file="dev-private.pem=$devKeyDir/dev-private.pem" `
            --from-file="dev-public.pem=$devKeyDir/dev-public.pem" `
            --dry-run=client -o yaml | kubectl apply -f -
        if ($LASTEXITCODE -ne 0) { throw 'failed to create auth-dev-keys secret' }

        $patch = @'
spec:
  template:
    spec:
      volumes:
        - name: dev-keys
          secret:
            secretName: auth-dev-keys
      containers:
        - name: authservice
          volumeMounts:
            - name: dev-keys
              mountPath: /app/dev-keys
              readOnly: true
'@
        kubectl patch deployment authservice --patch $patch
        if ($LASTEXITCODE -ne 0) { throw 'failed to patch authservice with dev-keys volume' }
    }
    else {
        Write-Warning "Auth dev-keys not found at $devKeyDir/. authservice will fail to start in Development."
    }

    Write-Host '    waiting for microservice pods to become Ready...'
    foreach ($app in @(
        'productservice','orderservice','basketservice','authservice',
        'inventoryservice','shippingservice','paymentservice','apigateway'
    )) {
        Wait-App $app
    }

    Write-Host "`nAll pods Ready. Snapshot:" -ForegroundColor Green
    kubectl get pods
    Write-Host "`nNext: kubectl port-forward svc/apigateway-loadbalancer 8004:8004"
}
finally {
    Pop-Location
}
