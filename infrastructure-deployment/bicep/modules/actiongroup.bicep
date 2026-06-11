// Azure Monitor action group — the shared notification target for the platform
// alert rules.
//
// Emits an email-distribution action group consumed by every Azure Monitor alert
// rule (alerts.bicep — issues #336/#337/#338) via its actions.actionGroups list.
// Provisioned only for staging/prod from main.bicep; dev/sandbox deploy no action
// group because no alert rules run there.
//
// Recipients are parameterized (mirroring the budget-contact pattern in
// budget.bicep) so the on-call distribution list is supplied per environment at
// deploy time rather than hard-coded here.

targetScope = 'resourceGroup'

@description('Name of the action group resource.')
param actionGroupName string

@description('Short name (max 12 chars) shown as the sender label on alert notifications.')
@maxLength(12)
param groupShortName string

@description('On-call email addresses notified when an alert fires. At least one entry is required by the action-group API.')
@minLength(1)
param emailAddresses array

@description('Resource tags.')
param tags object = {}

// Action groups are a global resource; the location is always 'Global' regardless
// of the region the rest of the platform deploys to.
resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: actionGroupName
  location: 'Global'
  tags: tags
  properties: {
    groupShortName: groupShortName
    enabled: true
    emailReceivers: [for (email, i) in emailAddresses: {
      name: 'oncall-${i}'
      emailAddress: email
      useCommonAlertSchema: true
    }]
  }
}

@description('Resource ID of the action group (feeds each alert rule\'s actions.actionGroups).')
output actionGroupId string = actionGroup.id

@description('Name of the action group.')
output actionGroupName string = actionGroup.name
