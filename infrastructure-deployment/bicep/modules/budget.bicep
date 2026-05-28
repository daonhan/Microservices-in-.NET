// Resource group-scoped consumption budget.
//
// Wraps Microsoft.Consumption/budgets with a single Forecasted threshold notification.
// Deployed only for the sandbox environment from main.bicep to cap monthly spend
// and email operators before the hard cap is breached.

targetScope = 'resourceGroup'

@description('Name of the budget resource.')
param budgetName string

@description('Monthly budget amount in the subscription billing currency (USD for personal subs).')
@minValue(1)
param amount int = 100

@description('Email addresses notified when the forecasted threshold is crossed. At least one entry is required by the consumption API.')
@minLength(1)
param contactEmails array

@description('Threshold percentage (1-1000) of amount that triggers the forecasted notification.')
@minValue(1)
@maxValue(1000)
param firstThresholdPercent int = 80

@description('Reset cadence for the budget consumption window.')
@allowed([
  'Monthly'
  'Quarterly'
  'Annually'
])
param timeGrain string = 'Monthly'

@description('Budget time-period start date (YYYY-MM-DD). Must be the first day of the period (1st of month for Monthly). Defaults to the first day of the current UTC month.')
param startDate string = utcNow('yyyy-MM-01')

resource budget 'Microsoft.Consumption/budgets@2023-11-01' = {
  name: budgetName
  properties: {
    category: 'Cost'
    amount: amount
    timeGrain: timeGrain
    timePeriod: {
      startDate: startDate
    }
    notifications: {
      forecastedFirstThreshold: {
        enabled: true
        operator: 'GreaterThan'
        threshold: firstThresholdPercent
        thresholdType: 'Forecasted'
        contactEmails: contactEmails
      }
    }
  }
}

@description('Resource ID of the budget.')
output budgetId string = budget.id

@description('Name of the budget.')
output budgetName string = budget.name
