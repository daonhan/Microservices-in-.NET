// Azure Monitor scheduled-query (log) alert rules over Application Insights.
//
// Re-creates the cloud-relevant subset of the local Prometheus rules
// (observability/alerts.yaml) as Azure Monitor alerts, now that AKS ships
// metrics to App Insights rather than Prometheus.
//
// This module currently emits the HTTP error-rate + latency alerts (issue #336).
// The low-stock (#337) and service-down (#338) rules extend this module in their
// own slices once the shared action group (#335) lands.
//
// Both alerts query the App Insights `requests` table and are grouped per service
// by `cloud_RoleName` (the App Insights equivalent of the local `service_name`
// Prometheus label), so a single rule pages for whichever service regresses.

@description('Resource ID of the Application Insights component the alerts query (appinsights.bicep → appInsightsId).')
param appInsightsId string

@description('Resource ID of the action group notified when an alert fires (action-group module → #335).')
param actionGroupId string

@description('Azure region for the alert rules. Must match the App Insights component region.')
param location string

@description('Prefix for alert resource names, e.g. "ecom-staging". Names become "<prefix>-high-http-error-rate".')
param namePrefix string

@description('5xx error-rate percentage (per service, over 5 minutes) above which HighHttpErrorRate fires.')
@minValue(1)
@maxValue(100)
param errorRatePercentThreshold int = 5

@description('p95 request duration in milliseconds (per service, over 5 minutes) above which HighHttpLatencyP95 fires.')
@minValue(1)
param latencyP95MillisecondsThreshold int = 1000

@description('Resource tags.')
param tags object = {}

// >5% 5xx responses per service over the trailing 5 minutes.
// Mirrors the local HighHttpErrorRate Prometheus rule (severity: warning → Sev 2).
resource highHttpErrorRate 'Microsoft.Insights/scheduledQueryRules@2023-03-15' = {
  name: '${namePrefix}-high-http-error-rate'
  location: location
  tags: tags
  kind: 'LogAlert'
  properties: {
    description: 'A service is serving more than ${errorRatePercentThreshold}% 5xx responses over the last 5 minutes.'
    severity: 2
    enabled: true
    scopes: [
      appInsightsId
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT5M'
    criteria: {
      allOf: [
        {
          query: 'requests\n| summarize Total = count(), Failed = countif(toint(resultCode) >= 500) by cloud_RoleName\n| where Total > 0\n| extend ErrorRatePct = 100.0 * Failed / Total\n| project cloud_RoleName, ErrorRatePct'
          timeAggregation: 'Average'
          metricMeasureColumn: 'ErrorRatePct'
          operator: 'GreaterThan'
          threshold: errorRatePercentThreshold
          dimensions: [
            {
              name: 'cloud_RoleName'
              operator: 'Include'
              values: [
                '*'
              ]
            }
          ]
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    autoMitigate: true
    actions: {
      actionGroups: [
        actionGroupId
      ]
    }
  }
}

// p95 request duration above the threshold per service over the trailing 5 minutes.
// Mirrors the local HighHttpLatencyP95 Prometheus rule (severity: warning → Sev 2).
// App Insights `requests.duration` is in milliseconds, so the 1s local threshold
// maps to 1000ms by default.
resource highHttpLatencyP95 'Microsoft.Insights/scheduledQueryRules@2023-03-15' = {
  name: '${namePrefix}-high-http-latency-p95'
  location: location
  tags: tags
  kind: 'LogAlert'
  properties: {
    description: 'A service p95 request duration is above ${latencyP95MillisecondsThreshold}ms over the last 5 minutes.'
    severity: 2
    enabled: true
    scopes: [
      appInsightsId
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT5M'
    criteria: {
      allOf: [
        {
          query: 'requests\n| summarize P95DurationMs = percentile(duration, 95) by cloud_RoleName\n| project cloud_RoleName, P95DurationMs'
          timeAggregation: 'Average'
          metricMeasureColumn: 'P95DurationMs'
          operator: 'GreaterThan'
          threshold: latencyP95MillisecondsThreshold
          dimensions: [
            {
              name: 'cloud_RoleName'
              operator: 'Include'
              values: [
                '*'
              ]
            }
          ]
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    autoMitigate: true
    actions: {
      actionGroups: [
        actionGroupId
      ]
    }
  }
}

@description('Resource ID of the HighHttpErrorRate alert rule.')
output highHttpErrorRateId string = highHttpErrorRate.id

@description('Resource ID of the HighHttpLatencyP95 alert rule.')
output highHttpLatencyP95Id string = highHttpLatencyP95.id
