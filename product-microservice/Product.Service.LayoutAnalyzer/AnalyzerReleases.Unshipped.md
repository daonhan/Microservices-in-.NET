; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
PRDLAY001 | Layout | Warning | Domain may not reference Infrastructure or Features
PRDLAY002 | Layout | Warning | Feature slice may not reference another feature slice
PRDLAY003 | Layout | Warning | Infrastructure may not reference Features
PRDLAY004 | Layout | Warning | Contracts may not reference any other internal Product.Service.* namespace
