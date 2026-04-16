# Essential AI Coding Feedback Loops For .NET C# Projects

When working with AI coding agents — especially those operating independently — you need **feedback loops** so the AI can verify its own work. Without them, the AI is coding blind: generating plausible-looking code that may not compile, may break existing tests, or may violate your team's style conventions.

In TypeScript projects, the standard feedback loops are `tsc`, Vitest, Husky, and Prettier. Here's how to set up the equivalent feedback loops for .NET C# projects.

---

## 1. Set Up Build-Time Error Detection

The C# compiler is your first feedback loop — analogous to `tsc` in TypeScript. But out of the box, warnings don't fail the build. Fix that.

Create a `Directory.Build.props` at your repository root to enforce strict compilation across **all** projects:

```xml
<Project>
  <PropertyGroup>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
  </PropertyGroup>

  <!-- Suppress warnings that aren't code quality issues -->
  <!-- NU1603/NU1902/NU1903: NuGet dependency resolution warnings -->
  <!-- CA1711: *EventHandler suffix is intentional (implements IEventHandler<T>) -->
  <!-- CA1707: Underscores in test method names (Given_When_Then convention) -->
  <!-- CA1716: "Shared" in namespace is by design (ECommerce.Shared library) -->
  <PropertyGroup>
    <NoWarn>$(NoWarn);NU1603;NU1902;NU1903;CA1711;CA1707;CA1716</NoWarn>
  </PropertyGroup>
</Project>
```

Now run the build:

```bash
dotnet build
```

- `TreatWarningsAsErrors` — every compiler warning becomes an error. The AI **must** fix warnings to proceed.
- `EnforceCodeStyleInBuild` — `.editorconfig` style rules are enforced during `dotnet build`, not just in the IDE.
- `AnalysisLevel` — enables the latest Roslyn analyzer recommendations (null safety, platform compatibility, etc.).

This is the single highest-impact feedback loop. If the AI writes code that doesn't compile or has warnings, it gets an error message and retries.

---

## 2. Add Automated Tests

Use a test framework like [xUnit](https://xunit.net/) for catching logical errors:

```bash
dotnet test
```

If you have multiple microservices with separate solution files, run tests per-service:

```bash
dotnet test basket-microservice/Basket.Service.slnx
dotnet test order-microservice/Order.Service.slnx
dotnet test product-microservice/Product.Service.slnx
```

Or run all test projects at once from the repo root:

```bash
dotnet test **/*.Tests/*.csproj
```

A typical test project `.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
    <PackageReference Include="NSubstitute" Version="5.3.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\MyService\MyService.csproj" />
  </ItemGroup>
</Project>
```

Basic unit tests covering core functionality help keep the AI on track. When the AI changes business logic, failing tests tell it exactly what broke.

---

## 3. Install Husky.Net for Pre-commit Hooks

[Husky.Net](https://alirezanet.github.io/Husky.Net/) enforces feedback loops before every commit — the .NET equivalent of Husky for Node.js.

Install and initialize:

```bash
# Create a dotnet tool manifest (if you don't have one)
dotnet new tool-manifest

# Install Husky.Net
dotnet tool install Husky

# Initialize Husky (creates .husky/ directory and git hooks)
dotnet husky install
```

Add a `task-runner.json` inside `.husky/` to define what runs on pre-commit:

```json
{
  "tasks": [
    {
      "name": "dotnet-format",
      "group": "pre-commit",
      "command": "dotnet",
      "args": ["format", "--verify-no-changes", "--verbosity", "diagnostic"]
    },
    {
      "name": "dotnet-build",
      "group": "pre-commit",
      "command": "dotnet",
      "args": ["build", "--no-restore"]
    },
    {
      "name": "dotnet-test-basket",
      "group": "pre-commit",
      "command": "dotnet",
      "args": ["test", "basket-microservice/Basket.Service.slnx", "--no-build"]
    },
    {
      "name": "dotnet-test-order",
      "group": "pre-commit",
      "command": "dotnet",
      "args": ["test", "order-microservice/Order.Service.slnx", "--no-build"]
    },
    {
      "name": "dotnet-test-product",
      "group": "pre-commit",
      "command": "dotnet",
      "args": ["test", "product-microservice/Product.Service.slnx", "--no-build"]
    }
  ]
}
```

If any step fails, the commit is blocked and the AI gets an error message. It will fix the issue and retry.

---

## 4. Set Up Automatic Code Formatting

Use `.editorconfig` with `dotnet format` to auto-format code — the .NET equivalent of Prettier.

Create an `.editorconfig` at your repository root:

```ini
root = true

[*]
indent_style = space
indent_size = 4
end_of_line = lf
charset = utf-8
trim_trailing_whitespace = true
insert_final_newline = true

[*.{csproj,props,targets,xml,json,yaml,yml}]
indent_size = 2

[*.cs]
# Namespace declarations
csharp_style_namespace_declarations = file_scoped:warning

# 'using' placement
csharp_using_directive_placement = outside_namespace:warning

# 'var' preferences
csharp_style_var_for_built_in_types = true:suggestion
csharp_style_var_when_type_is_apparent = true:suggestion
csharp_style_var_elsewhere = true:suggestion

# Expression-bodied members
csharp_style_expression_bodied_methods = when_on_single_line:suggestion
csharp_style_expression_bodied_constructors = false:suggestion
csharp_style_expression_bodied_properties = true:suggestion
csharp_style_expression_bodied_accessors = true:suggestion

# Braces
csharp_prefer_braces = true:warning

# Null checking
csharp_style_throw_expression = true:suggestion
csharp_style_conditional_delegate_call = true:suggestion
dotnet_style_coalesce_expression = true:suggestion
dotnet_style_null_propagation = true:suggestion

# Sort usings
dotnet_sort_system_directives_first = true
dotnet_separate_import_directive_groups = false
```

Run the formatter:

```bash
# Check for violations (CI / pre-commit mode)
dotnet format --verify-no-changes

# Auto-fix violations
dotnet format
```

`dotnet format` reads your `.editorconfig` rules and either reports or auto-fixes formatting violations. All AI-generated code now conforms to your formatting standards.

> **Tip:** EF Core migrations are auto-generated and will trigger style violations.
> Add this to your `.editorconfig` to skip them:
>
> ```ini
> [**/Migrations/*.cs]
> generated_code = true
> ```

The pre-commit hook (Step 3) runs `dotnet format --verify-no-changes` to block commits with formatting drift. The AI agent can run `dotnet format` to auto-fix before retrying the commit.

---

## Why This Works for AI

AI agents don't get frustrated by repetition. When code fails the build, tests, or formatting checks, the agent simply tries again. This makes feedback loops — and pre-commit hooks especially — incredibly powerful for AI-driven development.

Here's the complete feedback chain:

```
AI writes code
  → dotnet build (catches compile errors + warnings)
    → dotnet test (catches logic errors)
      → dotnet format --verify-no-changes (catches style violations)
        → git commit (blocked if any step fails)
          → AI reads error output and retries
```

Each layer catches a different class of error. Together, they give the AI everything it needs to self-correct without human intervention.

---

## Quick Reference

| Feedback Loop | TypeScript | .NET C# |
|---|---|---|
| Type / compile checking | `tsc` | `dotnet build` + `TreatWarningsAsErrors` |
| Automated tests | `vitest` / `jest` | `dotnet test` (xUnit) |
| Pre-commit hooks | Husky (npm) | Husky.Net (dotnet tool) |
| Code formatting | Prettier + lint-staged | `dotnet format` + `.editorconfig` |
| Code linting | ESLint | Roslyn Analyzers (`AnalysisLevel`) |

---

## Setup Checklist

```bash
# 1. Build-time error detection
#    Create Directory.Build.props (see Section 1)

# 2. Tests (should already exist — if not, create test projects)
dotnet test

# 3. Pre-commit hooks
dotnet new tool-manifest
dotnet tool install Husky
dotnet husky install

# 4. Code formatting
#    Create .editorconfig (see Section 4)
dotnet format --verify-no-changes
```

Once all four feedback loops are in place, your AI coding agent has everything it needs to verify its own work — automatically, on every commit.
