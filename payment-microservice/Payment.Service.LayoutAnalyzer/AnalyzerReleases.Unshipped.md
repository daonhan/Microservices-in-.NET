; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
PAYLAY001 | Layout | Error | Domain may not reference Infrastructure or Features
PAYLAY002 | Layout | Error | Feature slice may not reference another feature slice
PAYLAY003 | Layout | Error | Infrastructure may not reference Features
PAYLAY004 | Layout | Error | Contracts may not reference any other internal Payment.Service.* namespace
