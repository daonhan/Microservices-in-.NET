; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
ORDLAY001 | Layout | Error | Domain may not reference Infrastructure or Features
ORDLAY002 | Layout | Error | Feature slice may not reference another feature slice
ORDLAY003 | Layout | Error | Infrastructure may not reference Features
ORDLAY004 | Layout | Error | Contracts may not reference any other internal Order.Service.* namespace
