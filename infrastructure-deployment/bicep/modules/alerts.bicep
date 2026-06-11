// Azure Monitor scheduled-query (log) alert rules over Application Insights.
//
// Re-creates the cloud-relevant subset of the local Prometheus rules
// (observability/alerts.yaml) as Azure Monitor alerts, now that AKS ships
// metrics to App Insights rather than Prometheus.
//
// This module emits the HTTP error-rate + latency alerts (issue #336), the
// Inventory low-stock / reservation-failure alert (issue #337), and the
// service-down alert (issue #338).
//
// The HTTP alerts query the App Insights `requests` table and the low-stock alert
// queries the `customMetrics` table — all three scoped to the App Insights
// component and grouped per service by `cloud_RoleName` (the App Insights
// equivalent of the local `service_name` Prometheus label). ServiceDown instead
// queries the Container Insights `KubePodInventory` table in the Log Analytics
// workspace (so it also covers the event-driven services that serve no HTTP and
// never appear in the `requests` table), grouped per workload by `ControllerName`.
// Each rule pages for whichever service regresses.

@description('Resource ID of the Application Insights component the HTTP + low-stock alerts query (appinsights.bicep → appInsightsId).')
param appInsightsId string

@description('Resource ID of the Log Analytics workspace the ServiceDown alert queries for Container Insights KubePodInventory (monitor.bicep → workspaceId).')
param workspaceId string

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

@description('Stock-reservation failures (summed over 5 minutes) above which LowStockAlert fires. Default 0 = any failure pages, mirroring the local LowStockAlert Prometheus rule.')
@minValue(0)
param reservationFailuresThreshold int = 0

@description('Unhealthy (not-running / not-ready / crashing) pods per workload above which ServiceDown fires. Default 0 = any unhealthy pod pages, mirroring the local ServiceDown Prometheus rule (up == 0).')
@minValue(0)
param serviceDownUnhealthyPodThreshold int = 0

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
// App Insights `requests.duration` is a timespan. Convert it to numeric
// milliseconds first so the percentile output is comparable to the ms threshold.
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
          query: 'requests\n| extend DurationMs = todouble(duration / 1ms)\n| summarize P95DurationMs = percentile(DurationMs, 95) by cloud_RoleName\n| project cloud_RoleName, P95DurationMs'
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

// Inventory rejecting stock reservations over the trailing 5 minutes.
// Mirrors the local LowStockAlert Prometheus rule (severity: warning → Sev 2).
// Inventory increments the `stock-reservations-failed` counter (dash-cased, the
// App Insights `customMetrics.name`, not the Prometheus `stock_reservations_failed_total`
// form) on each rejected reservation; summing it over the window detects depleted
// stock from telemetry rather than from customer reports.
resource lowStockAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15' = {
  name: '${namePrefix}-low-stock'
  location: location
  tags: tags
  kind: 'LogAlert'
  properties: {
    description: 'Inventory is rejecting stock reservations over the last 5 minutes (likely low/depleted stock).'
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
          query: 'customMetrics\n| where name == "stock-reservations-failed"\n| summarize FailedReservations = sum(valueSum) by cloud_RoleName\n| project cloud_RoleName, FailedReservations'
          timeAggregation: 'Average'
          metricMeasureColumn: 'FailedReservations'
          operator: 'GreaterThan'
          threshold: reservationFailuresThreshold
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

// A workload has one or more pods that are not running/ready or are crash-looping
// over the trailing 5 minutes. Mirrors the local ServiceDown Prometheus rule
// (up == 0, severity: critical → Sev 0). Queries the Container Insights
// `KubePodInventory` table (populated by the AKS omsagent addon) in the Log
// Analytics workspace rather than App Insights, so it also covers the event-driven
// services that serve no HTTP and never appear in the `requests` table. Grouped
// per workload by `ControllerName`.
resource serviceDown 'Microsoft.Insights/scheduledQueryRules@2023-03-15' = {
  name: '${namePrefix}-service-down'
  location: location
  tags: tags
  kind: 'LogAlert'
  properties: {
    description: 'A service has one or more pods that are not running/ready or are crash-looping over the last 5 minutes.'
    severity: 0
    enabled: true
    scopes: [
      workspaceId
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT5M'
    criteria: {
      allOf: [
        {
          query: 'KubePodInventory\n| where Namespace startswith "ecommerce"\n| summarize arg_max(TimeGenerated, PodStatus, ContainerStatus) by Name, ControllerName\n| summarize UnhealthyPods = countif(PodStatus !in~ ("Running", "Succeeded") or ContainerStatus =~ "waiting") by ControllerName\n| project ControllerName, UnhealthyPods'
          timeAggregation: 'Average'
          metricMeasureColumn: 'UnhealthyPods'
          operator: 'GreaterThan'
          threshold: serviceDownUnhealthyPodThreshold
          dimensions: [
            {
              name: 'ControllerName'
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

@description('Resource ID of the LowStockAlert alert rule.')
output lowStockAlertId string = lowStockAlert.id

@description('Resource ID of the ServiceDown alert rule.')
output serviceDownId string = serviceDown.id
