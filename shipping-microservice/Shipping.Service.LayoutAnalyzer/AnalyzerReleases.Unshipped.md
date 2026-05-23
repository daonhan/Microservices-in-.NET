; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
SHPLAY001 | Layout | Error | Domain may not reference Infrastructure or Features
SHPLAY002 | Layout | Error | Feature slice may not reference another feature slice
SHPLAY003 | Layout | Error | Infrastructure may not reference Features
