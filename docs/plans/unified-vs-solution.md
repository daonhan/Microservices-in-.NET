# Plan: Unified Visual Studio Solution

> Source PRD: docs/prd/unified-vs-solution.md

## Architectural decisions

Durable decisions that apply across all phases:

- **Root Solution**: A single `MicroservicesInDotNet.sln` at the repository root.
- **Solution Folders**: 
  - `Services` (for all microservices)
  - `Libraries` (for shared `.NET` libraries)
  - `Tests` (for all test projects)
  - `ApiGateway` (for the API gateway project)
- **Directory Structure**: No files or directories on disk will be moved; the hierarchy will be managed entirely through virtual solution folders.

---

## Phase 1: Create and Populate the Root Solution

**User stories**: 1, 2, 3, 4, 5, 6

### What to build

Initialize the single, repository-wide `.sln` file and populate it with all existing `.csproj` files. Group the added projects into logical Solution Folders corresponding to their domains to ensure visual tidiness within Visual Studio 2026. Verify that building and testing from the root solution works seamlessly.

### Acceptance criteria

- [ ] The `MicroservicesInDotNet.sln` file is created at the repository root.
- [ ] `ECommerce.Shared` projects are added under a `Libraries` solution folder.
- [ ] All microservice projects are added under a `Services` solution folder.
- [ ] The API Gateway project is added under an `ApiGateway` solution folder.
- [ ] All test projects (`*.Tests.csproj`) are added under a `Tests` solution folder.
- [ ] Running `dotnet build MicroservicesInDotNet.sln` compiles all projects successfully.
- [ ] Running `dotnet test MicroservicesInDotNet.sln` executes all test suites successfully.
