; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
SAGLAY001 | Layout | Disabled | Domain may not reference Infrastructure or Features
SAGLAY002 | Layout | Disabled | Feature slice may not reference another feature slice
SAGLAY003 | Layout | Disabled | Infrastructure may not reference Features
SAGLAY004 | Layout | Disabled | Contracts may not reference any other internal Saga.Service.* namespace
