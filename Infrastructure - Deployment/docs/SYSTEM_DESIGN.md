# System Design — End-to-End CI/CD

> Companion to [OVERVIEW.md](OVERVIEW.md) and
> [ARCHITECTURE.md](ARCHITECTURE.md). Describes how a code change moves
> from a developer's laptop to a Production AKS cluster.

## Pipeline anatomy

Every microservice has its own `azure-pipelines.yml` colocated with the
service code:

```
auth-microservice/azure-pipelines.yml
basket-microservice/azure-pipelines.yml
inventory-microservice/azure-pipelines.yml
order-microservice/azure-pipelines.yml
payment-microservice/azure-pipelines.yml
product-microservice/azure-pipelines.yml
shipping-microservice/azure-pipelines.yml
api-gateway/azure-pipelines.yml
```

Each pipeline `extends` the shared
[`build-stage.yml`](../pipelines/templates/build-stage.yml) and one
[`deploy-stage.yml`](../pipelines/templates/deploy-stage.yml) per
environment, passing service-specific parameters (project path,
Dockerfile path, namespace, secrets).

## Triggers

```yaml
trigger:
  branches:
    include:
      - dev          # Dev deploy
      - staging      # Staging deploy
      - prod         # Prod deploy
      - deploy/*     # Dev deploy (feature/integration branches)
  paths:
    include:
      - <service>-microservice/
      - shared-libs/
      - Directory.Build.props
      - Infrastructure - Deployment/pipelines/templates/

pr:
  branches:
    include:
      - '*'          # Build + test only on PR validation
```

Path filters keep service builds independent — a change to a single
service does not rebuild the other seven. Changes to `shared-libs/` or
the build template fan out to all services.

## Build stage

Defined once in
[`pipelines/templates/build-stage.yml`](../pipelines/templates/build-stage.yml).
Runs on Microsoft-hosted `ubuntu-latest`.

| Step                                | Command / Task                                                                |
|-------------------------------------|--------------------------------------------------------------------------------|
| Use .NET SDK                        | `UseDotNet@2` (`packageType: sdk`, version `10.0.x`)                          |
| NuGet authenticate                  | `NuGetAuthenticate@1` (private feeds + `local-nuget-packages/`)               |
| Restore                             | `dotnet restore <Solution.slnx>`                                              |
| Lint                                | `dotnet format --verify-no-changes --verbosity minimal --no-restore`          |
| Build                               | `dotnet build -c Release --no-restore`                                        |
| Test (with coverage)                | `dotnet test -c Release --no-build --collect:"XPlat Code Coverage" -- ... Format=cobertura` |
| Publish test results                | `PublishTestResults@2` (VSTest .trx, fail on test failure)                    |
| Publish coverage                    | `PublishCodeCoverageResults@2` (Cobertura)                                    |
| Publish coverage artifact           | `PublishPipelineArtifact@1` (`<service>-coverage`)                            |
| App publish                         | `dotnet publish <Service>.csproj -c Release --no-build -o <staging>/publish`  |
| App publish artifact                | `publish:` (`<service>-publish`)                                              |
| Compute image tag                   | `bash`: `<branch>-<buildnumber>` (or tag name on tag builds). Stage output.   |
| ACR login                           | `Docker@2 command: login` (skipped on PR builds)                              |
| Docker build                        | `Docker@2 command: build` (`buildContext: .`, root context for shared libs)   |
| Docker push                         | `Docker@2 command: push` → `<acr>.azurecr.io/<service>:<imageTag>`            |

Image tag and image name are exposed as **stage outputs** from the
`ComputeTag` step so the deploy stages can resolve the exact image to
roll out without recomputing it.

## Deploy stage (per environment)

Defined once in
[`pipelines/templates/deploy-stage.yml`](../pipelines/templates/deploy-stage.yml).
Each per-service pipeline references it three times — Dev, Staging, Prod
— with environment-specific parameters.

```yaml
- template: ../../Infrastructure - Deployment/pipelines/templates/deploy-stage.yml
  parameters:
    environment: dev
    azureSubscription: $(AKS_SERVICE_CONNECTION_DEV)
    namespace: ecommerce-dev
    manifestPath: Infrastructure - Deployment/kube/aks-dev-<service>.yml
    imageRepository: $(ACR_NAME).azurecr.io/<service>
    imageTag: $[ stageDependencies.Build.Build.outputs['ComputeTag.imageTag'] ]
    secrets:
      - name: <service>-db-secret
        items:
          connection-string: $(DEV_<SERVICE>_DB_CONN)
      - name: appinsights-secret
        items:
          connection-string: $(DEV_APPLICATIONINSIGHTS_CONNECTION_STRING)
      - name: acr-pull-secret
        type: dockerRegistry
        dockerRegistryEndpoint: $(ACR_SERVICE_CONNECTION)
```

Inside the deploy stage:

1. **Login to AKS** via the configured Azure DevOps service connection
   (one per environment).
2. **Create / update K8s secrets** using `KubernetesManifest@0` with
   `action: createSecret`, sourced from pipeline variables prefixed
   `DEV_`, `STAGING_`, or `PROD_`.
3. **Apply manifests** via `KubernetesManifest@0` with
   `action: deploy` and `containers: <imageRepository>:<imageTag>` —
   substitutes the image tag computed in the build stage. The manifest
   uses `image: $(ACR_NAME).azurecr.io/<service>:$(IMAGE_TAG)`
   placeholders that `KubernetesManifest@0` rewrites with the resolved
   image reference, so the cluster always rolls the exact image just
   built.

## Approval gates

Manual approvals are not coded into the pipeline. They are configured on
the Azure DevOps **Environment** (`ecommerce-staging`, `ecommerce-prod`)
under *Approvals and checks*. Adding a reviewer there blocks the deploy
stage until approved, with no pipeline change required.

## Branching model

```
feature/* ──PR──► dev ──► (Dev deploy)
deploy/*  ─────► dev ──► (Dev deploy)        # ad-hoc integration branches
dev       ──PR──► staging ──► (Staging deploy)
staging   ──PR──► prod    ──► (Prod deploy + approval)
```

PRs to any branch run **build + test only** (Docker push and deploy are
gated on `ne(variables['Build.Reason'], 'PullRequest')`).

## Image tagging

| Source ref          | Tag                          |
|---------------------|------------------------------|
| `refs/heads/<br>`   | `<br>-<BUILD_BUILDNUMBER>`   |
| `refs/heads/feat/x` | `feat-x-<BUILD_BUILDNUMBER>` (slashes → dashes) |
| `refs/tags/<tag>`   | `<tag>` (verbatim)           |

Tags are immutable in ACR — every successful build produces a unique tag,
so rollbacks are a re-deploy of an older tag. `latest` is intentionally
not used.

## Coverage and quality

Cobertura coverage from each `dotnet test` run is published as a
pipeline artifact and as Azure DevOps coverage tab content. There is no
hard quality gate yet — adding one (e.g. `--threshold` or a custom task)
is left as a future enhancement.

## Deployment commands cheat sheet

```bash
# What the pipeline does, by hand
SERVICE=order
ENV=dev
TAG=$(git rev-parse --short HEAD)

docker build -f $SERVICE-microservice/Order.Service/Dockerfile -t myacr.azurecr.io/$SERVICE:$TAG .
az acr login --name myacr
docker push myacr.azurecr.io/$SERVICE:$TAG

az aks get-credentials -g rg-ecommerce-$ENV -n aks-ecommerce-$ENV
sed -i "s|\$(ACR_NAME)|myacr|g; s|\$(IMAGE_TAG)|$TAG|g" "Infrastructure - Deployment/kube/aks-$ENV-$SERVICE.yml"
kubectl apply -f "Infrastructure - Deployment/kube/aks-$ENV-$SERVICE.yml"
kubectl -n ecommerce-$ENV rollout status deploy/$SERVICE
```

## Failure handling

| Failure                            | Behavior                                         |
|------------------------------------|--------------------------------------------------|
| `dotnet format` violation          | Build fails before tests run.                    |
| Test failure                       | Pipeline fails (tests publish marks as failed).  |
| Docker build / push failure        | Deploy stages do not run (stage dependency).     |
| Rollout stuck                      | `KubernetesManifest@0` waits for rollout; fails the stage on timeout. Use `kubectl rollout undo` or redeploy a known-good tag. |
| Image pull failure                 | Pod stays in `ImagePullBackOff`; check `acr-pull-secret` and ACR attach. |
| Health probe failure               | Readiness keeps pod out of Service; liveness restarts. Check Application Insights traces and `kubectl logs`. |
