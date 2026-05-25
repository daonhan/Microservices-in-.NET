; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
AGWLAY001 | Layout | Disabled | Domain may not exist in ApiGateway (gateway owns no aggregate)
AGWLAY002 | Layout | Disabled | Feature slice may not reference another feature slice
AGWLAY003 | Layout | Disabled | Infrastructure may not reference Features
AGWLAY004 | Layout | Disabled | Contracts may not exist in ApiGateway (gateway publishes no integration events)
