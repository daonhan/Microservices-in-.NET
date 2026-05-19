#requires -Version 7
#requires -Modules @{ ModuleName='Pester'; ModuleVersion='5.0' }

BeforeAll {
    $script:ScriptPath = (Resolve-Path (Join-Path $PSScriptRoot '..' 'check-doc-drift.ps1')).Path

    $script:CleanCompose = @'
services:
  product:
    build:
      context: .
      dockerfile: ./product-microservice/Dockerfile
    ports:
      - "8002:8080"
  order:
    build:
      context: .
      dockerfile: ./order-microservice/Dockerfile
    ports:
      - "8001:8080"
  basket:
    build:
      context: .
      dockerfile: ./basket-microservice/Dockerfile
    ports:
      - "8000:8080"
  inventory:
    build:
      context: .
      dockerfile: ./inventory-microservice/Dockerfile
    ports:
      - "8005:8080"
  shipping:
    build:
      context: .
      dockerfile: ./shipping-microservice/Dockerfile
    ports:
      - "8006:8080"
  payment:
    build:
      context: .
      dockerfile: ./payment-microservice/Dockerfile
    ports:
      - "8007:8080"
  saga:
    build:
      context: .
      dockerfile: ./saga-microservice/Dockerfile
    ports:
      - "8008:8080"
  auth:
    build:
      context: .
      dockerfile: ./auth-microservice/Dockerfile
    ports:
      - "8003:8080"
  gateway:
    build:
      context: .
      dockerfile: ./api-gateway/Dockerfile
    ports:
      - "8004:8080"
  rabbitmq:
    image: rabbitmq:3
    ports:
      - "5672:5672"
'@

    $script:FullCatalog = @'
# Catalog

| Service | Port |
|---------|------|
| basket | 8000 |
| order | 8001 |
| product | 8002 |
| auth | 8003 |
| gateway | 8004 |
| inventory | 8005 |
| shipping | 8006 |
| payment | 8007 |
| saga | 8008 |
'@

    $script:CatalogMissingSaga = @'
# Catalog

| Service | Port |
|---------|------|
| basket | 8000 |
| order | 8001 |
| product | 8002 |
| auth | 8003 |
| gateway | 8004 |
| inventory | 8005 |
| shipping | 8006 |
| payment | 8007 |
'@

    function script:New-FixtureRepo {
        param([hashtable]$Files)
        $root = Join-Path ([IO.Path]::GetTempPath()) ("doc-drift-" + [Guid]::NewGuid().Guid)
        New-Item -ItemType Directory -Path $root -Force | Out-Null
        foreach ($k in $Files.Keys) {
            $p = Join-Path $root $k
            $dir = Split-Path $p -Parent
            if ($dir -and -not (Test-Path $dir)) {
                New-Item -ItemType Directory -Path $dir -Force | Out-Null
            }
            Set-Content -Path $p -Value $Files[$k] -NoNewline -Encoding utf8
        }
        return $root
    }

    function script:New-CleanFixture {
        param([hashtable]$Extra)
        $files = @{
            'docker-compose.yaml'                = $script:CleanCompose
            'README.md'                          = $script:FullCatalog
            'CONTEXT.md'                         = $script:FullCatalog
            'AGENTS.md'                          = $script:FullCatalog
            'CLAUDE.md'                          = $script:FullCatalog
            '.github/copilot-instructions.md'    = $script:FullCatalog
            'scripts/doc-drift-allowlist.txt'    = "# allowlist`n"
        }
        if ($Extra) {
            foreach ($k in $Extra.Keys) { $files[$k] = $Extra[$k] }
        }
        return (script:New-FixtureRepo -Files $files)
    }

    function script:Invoke-Drift {
        param([string]$Root)
        $output = & pwsh -NoProfile -File $script:ScriptPath -RepoRoot $Root 2>&1
        return [PSCustomObject]@{
            ExitCode = $LASTEXITCODE
            Output   = ($output -join [Environment]::NewLine)
        }
    }
}

Describe 'check-doc-drift.ps1' {
    It 'Given_Clean_Repo_When_Script_Runs_Then_Exit_Zero' {
        $root = script:New-CleanFixture
        try {
            $r = script:Invoke-Drift -Root $root
            $r.ExitCode | Should -Be 0
        } finally {
            Remove-Item -Recurse -Force $root
        }
    }

    It 'Given_Banned_Phrase_Outside_Allowlist_When_Script_Runs_Then_Exit_Non_Zero' {
        $root = script:New-CleanFixture -Extra @{
            'docs/notes.md' = "This describes saga choreography across services."
        }
        try {
            $r = script:Invoke-Drift -Root $root
            $r.ExitCode | Should -Not -Be 0
            $r.Output | Should -Match 'docs/notes\.md'
        } finally {
            Remove-Item -Recurse -Force $root
        }
    }

    It 'Given_Banned_Phrase_Inside_Allowlist_When_Script_Runs_Then_Exit_Zero' {
        $root = script:New-CleanFixture -Extra @{
            'docs/notes.md'                   = "This describes saga choreography across services."
            'scripts/doc-drift-allowlist.txt' = "docs/notes.md`n"
        }
        try {
            $r = script:Invoke-Drift -Root $root
            $r.ExitCode | Should -Be 0
        } finally {
            Remove-Item -Recurse -Force $root
        }
    }

    It 'Given_Full_Eight_Service_Table_When_Script_Runs_Then_Exit_Zero' {
        $root = script:New-CleanFixture
        try {
            $r = script:Invoke-Drift -Root $root
            $r.ExitCode | Should -Be 0
        } finally {
            Remove-Item -Recurse -Force $root
        }
    }

    It 'Given_Catalog_Missing_8008_Row_When_Script_Runs_Then_Exit_Non_Zero' {
        $root = script:New-CleanFixture -Extra @{
            'README.md' = $script:CatalogMissingSaga
        }
        try {
            $r = script:Invoke-Drift -Root $root
            $r.ExitCode | Should -Not -Be 0
            $r.Output | Should -Match '8008'
            $r.Output | Should -Match 'README\.md'
        } finally {
            Remove-Item -Recurse -Force $root
        }
    }

    It 'Given_Banned_Phrase_In_Link_Url_When_Script_Runs_Then_Not_Flagged' {
        $root = script:New-CleanFixture -Extra @{
            'docs/notes.md' = "See [ADR-0008](docs/adr/0008-saga-choreography.md) for history."
        }
        try {
            $r = script:Invoke-Drift -Root $root
            $r.ExitCode | Should -Be 0
        } finally {
            Remove-Item -Recurse -Force $root
        }
    }

    It 'Given_Banned_Phrase_In_Inline_Code_When_Script_Runs_Then_Not_Flagged' {
        $root = script:New-CleanFixture -Extra @{
            'docs/notes.md' = "Filename ``0008-saga-choreography.md`` is historical."
        }
        try {
            $r = script:Invoke-Drift -Root $root
            $r.ExitCode | Should -Be 0
        } finally {
            Remove-Item -Recurse -Force $root
        }
    }

    It 'Given_Each_Banned_Phrase_Variant_When_Script_Runs_Then_All_Flagged' {
        $root = script:New-CleanFixture -Extra @{
            'docs/a.md' = 'choreography is bad'
            'docs/b.md' = 'no central orchestrator'
            'docs/c.md' = 'no orchestrator at all'
            'docs/d.md' = 'saga choreography'
        }
        try {
            $r = script:Invoke-Drift -Root $root
            $r.ExitCode | Should -Not -Be 0
            $r.Output | Should -Match 'docs/a\.md'
            $r.Output | Should -Match 'docs/b\.md'
            $r.Output | Should -Match 'docs/c\.md'
            $r.Output | Should -Match 'docs/d\.md'
        } finally {
            Remove-Item -Recurse -Force $root
        }
    }
}
