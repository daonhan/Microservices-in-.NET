; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
SHALAY001 | Layout | Error | Abstractions may not reference Impl
SHALAY002 | Layout | Error | Impl may not reference Composition
SHALAY003 | Layout | Error | Cross-package import outside allowlist
