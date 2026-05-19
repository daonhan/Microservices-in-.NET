#requires -Version 7
<#
.SYNOPSIS
    Documentation drift gate for the Nhamnhi monorepo.

.DESCRIPTION
    Two checks, both run; the script fails if either finds drift.

    Check 1 — banned-phrase grep: case-insensitive search over every *.md file
    in the working tree for "choreograph", "no central orchestrator",
    "no orchestrator", and "saga choreography". Paths listed in
    scripts/doc-drift-allowlist.txt are exempt (historical-context docs).

    Check 2 — service-table sync: parse docker-compose.yaml, take every service
    whose Dockerfile lives under *-microservice/ or api-gateway/, extract the
    host port, then verify each (name, port) appears in the catalog tables in
    README.md, CONTEXT.md, AGENTS.md, CLAUDE.md, and .github/copilot-instructions.md.

    Failures print as a numbered "N. file:line  reason" list and exit non-zero.
#>
param(
    [string]$RepoRoot,
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) { $RepoRoot = (Get-Location).Path }
$RepoRoot = (Resolve-Path $RepoRoot).Path

$bannedPhrases = @('choreograph', 'no central orchestrator', 'no orchestrator', 'saga choreography')
$catalogFiles  = @('README.md', 'CONTEXT.md', 'AGENTS.md', 'CLAUDE.md', '.github/copilot-instructions.md')
$composePath   = Join-Path $RepoRoot 'docker-compose.yaml'
$allowlistPath = Join-Path $RepoRoot 'scripts/doc-drift-allowlist.txt'

$allowlist = @{}
if (Test-Path $allowlistPath) {
    foreach ($line in Get-Content $allowlistPath) {
        $trimmed = $line.Trim()
        if (-not $trimmed -or $trimmed.StartsWith('#')) { continue }
        $allowlist[$trimmed.Replace('\', '/')] = $true
    }
}

$failures = New-Object System.Collections.Generic.List[string]

# --- Check 1: banned phrases ---
$mdFiles = Get-ChildItem -Path $RepoRoot -Recurse -Filter '*.md' -File |
    Where-Object {
        $rel = $_.FullName.Substring($RepoRoot.Length).TrimStart('\', '/').Replace('\', '/')
        ($rel -notmatch '^(bin|obj|node_modules|local-nuget-packages|\.git)/') -and
        ($rel -notmatch '/(bin|obj|node_modules)/')
    }

function Remove-MarkdownNoise {
    param([string]$Line)
    # Strip URL portion of markdown links: [label](url) -> [label]
    $cleaned = [regex]::Replace($Line, '\]\([^)]*\)', ']')
    # Strip inline code spans (single-backtick): `code` -> (empty)
    $cleaned = [regex]::Replace($cleaned, '`[^`]*`', '')
    return $cleaned
}

foreach ($file in $mdFiles) {
    $rel = $file.FullName.Substring($RepoRoot.Length).TrimStart('\', '/').Replace('\', '/')
    if ($allowlist.ContainsKey($rel)) { continue }
    $lineNo = 0
    foreach ($line in Get-Content $file.FullName) {
        $lineNo++
        $cleaned = Remove-MarkdownNoise -Line $line
        foreach ($phrase in $bannedPhrases) {
            if ($cleaned -imatch [regex]::Escape($phrase)) {
                $failures.Add("${rel}:${lineNo}  banned phrase '$phrase'")
                break
            }
        }
    }
}

# --- Check 2: service-table sync ---
if (Test-Path $composePath) {
    $services = New-Object System.Collections.Generic.List[object]
    $currentName = $null
    $currentDockerfile = $null
    $currentPort = $null
    $inServices = $false

    $flush = {
        if ($currentName -and $currentDockerfile -and $currentPort -and
            ($currentDockerfile -match '-microservice/|api-gateway/')) {
            $services.Add([pscustomobject]@{ Name = $currentName; Port = $currentPort })
        }
    }

    foreach ($line in Get-Content $composePath) {
        if ($line -match '^services:\s*$') {
            $inServices = $true
            continue
        }
        if (-not $inServices) { continue }

        if ($line -match '^  ([A-Za-z0-9_-]+):\s*$') {
            & $flush
            $currentName = $matches[1]
            $currentDockerfile = $null
            $currentPort = $null
        }
        elseif ($line -match '^[A-Za-z0-9_]') {
            & $flush
            $currentName = $null
            $inServices = $false
        }
        elseif ($line -match 'dockerfile:\s*(\S+)') {
            $currentDockerfile = $matches[1]
        }
        elseif (-not $currentPort -and ($line -match '-\s*"?(\d+):\d+"?')) {
            $currentPort = [int]$matches[1]
        }
    }
    & $flush

    foreach ($svc in $services) {
        $namePattern = [regex]::Escape($svc.Name)
        $portPattern = "\b$($svc.Port)\b"
        foreach ($cat in $catalogFiles) {
            $catPath = Join-Path $RepoRoot $cat
            if (-not (Test-Path $catPath)) {
                $failures.Add("${cat}:0  missing catalog file")
                continue
            }
            $found = $false
            $lineNo = 0
            foreach ($line in Get-Content $catPath) {
                $lineNo++
                if (($line -imatch $namePattern) -and ($line -match $portPattern)) {
                    $found = $true
                    break
                }
            }
            if (-not $found) {
                $failures.Add("${cat}:0  missing service '$($svc.Name)' at port $($svc.Port)")
            }
        }
    }
}

if ($failures.Count -gt 0) {
    if (-not $Quiet) {
        $i = 0
        foreach ($f in $failures) {
            $i++
            Write-Host "$i. $f"
        }
    }
    exit 1
}

if (-not $Quiet) {
    Write-Host "Documentation drift check passed."
}
exit 0
