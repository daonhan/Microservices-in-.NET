#requires -Version 7
<#
.SYNOPSIS
    Publish docs/wiki/ to the GitHub Wiki remote.

.DESCRIPTION
    Clones the GitHub Wiki repository into a temporary directory, mirrors the
    repository's docs/wiki/ contents into that clone, commits with a timestamp
    and current HEAD SHA, then pushes the wiki branch. The source docs/wiki/
    directory is never modified.

.PARAMETER DryRun
    Print the clone, copy, commit, and push steps that would run without
    cloning the wiki remote, writing a commit, or pushing.

.EXAMPLE
    ./scripts/publish-wiki.ps1
    ./scripts/publish-wiki.ps1 -DryRun
#>
[CmdletBinding()]
param(
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$wikiRemote = 'https://github.com/daonhan/Microservices-in-.NET.wiki.git'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$wikiSource = Join-Path $repoRoot 'docs/wiki'

function Invoke-Git {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments,
        [string]$WorkingDirectory = $repoRoot
    )

    Push-Location $WorkingDirectory
    try {
        & git @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
        }
    }
    finally {
        Pop-Location
    }
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'git CLI is required.'
}

if (-not (Test-Path $wikiSource)) {
    throw "Wiki source directory not found: $wikiSource"
}

$headSha = (& git -C $repoRoot rev-parse --short HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or -not $headSha) {
    throw 'Unable to resolve repository HEAD SHA.'
}

$timestamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')
$message = "Sync wiki from docs/wiki $timestamp (repo $headSha)"

if ($DryRun) {
    Write-Host '==> Dry run: wiki publish plan' -ForegroundColor Cyan
    Write-Host "    repo root: $repoRoot"
    Write-Host "    source:    $wikiSource"
    Write-Host "    remote:    $wikiRemote"
    Write-Host "    commit:    $message"
    Write-Host '    would clone wiki remote into a temporary directory'
    Write-Host '    would replace clone contents with docs/wiki/ files'
    Write-Host '    would commit if the clone has changes'
    Write-Host '    would push to the wiki remote'
    return
}

$tempRootSeed = [System.IO.Path]::GetTempFileName()
Remove-Item -LiteralPath $tempRootSeed -Force
$tempRoot = "$tempRootSeed-nhamnhi-wiki-publish"
$cloneDir = Join-Path $tempRoot 'wiki'

try {
    New-Item -ItemType Directory -Path $tempRoot | Out-Null

    Write-Host "==> 1/4 Cloning wiki remote" -ForegroundColor Cyan
    Invoke-Git -Arguments @('clone', '--depth', '1', $wikiRemote, $cloneDir)

    $resolvedTempRoot = (Resolve-Path $tempRoot).Path
    $resolvedCloneDir = (Resolve-Path $cloneDir).Path
    if (-not $resolvedCloneDir.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to mirror outside temp root: $resolvedCloneDir"
    }

    Write-Host "==> 2/4 Mirroring docs/wiki/ into temp clone" -ForegroundColor Cyan
    Get-ChildItem -LiteralPath $cloneDir -Force |
        Where-Object { $_.Name -ne '.git' } |
        Remove-Item -Recurse -Force
    Copy-Item -Path (Join-Path $wikiSource '*') -Destination $cloneDir -Recurse -Force

    Write-Host "==> 3/4 Committing wiki changes" -ForegroundColor Cyan
    Invoke-Git -Arguments @('add', '-A') -WorkingDirectory $cloneDir
    $status = (& git -C $cloneDir status --porcelain)
    if ($LASTEXITCODE -ne 0) {
        throw 'git status failed in wiki clone.'
    }
    if (-not $status) {
        Write-Host '    no wiki changes to publish' -ForegroundColor Yellow
        return
    }
    Invoke-Git -Arguments @('commit', '-m', $message) -WorkingDirectory $cloneDir

    Write-Host "==> 4/4 Pushing wiki changes" -ForegroundColor Cyan
    Invoke-Git -Arguments @('push') -WorkingDirectory $cloneDir
    Write-Host 'Wiki publish complete.' -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
