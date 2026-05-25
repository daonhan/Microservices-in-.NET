; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
AGWLAY001 | Layout | Error | Domain may not exist in ApiGateway (gateway owns no aggregate)
AGWLAY002 | Layout | Error | Feature slice may not reference another feature slice
AGWLAY003 | Layout | Error | Infrastructure may not reference Features
AGWLAY004 | Layout | Error | Contracts may not exist in ApiGateway (gateway publishes no integration events)
