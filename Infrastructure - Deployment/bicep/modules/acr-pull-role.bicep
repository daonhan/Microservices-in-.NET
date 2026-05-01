@description('Name of the existing ACR to assign AcrPull on.')
param acrName string

@description('Object ID of the principal (typically the AKS kubelet identity) that needs AcrPull.')
param principalId string

@description('Stable seed used to generate a deterministic role assignment GUID.')
param assignmentSeed string

resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' existing = {
  name: acrName
}

var acrPullRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')

resource acrPullAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, principalId, assignmentSeed, 'AcrPull')
  scope: acr
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: acrPullRoleId
  }
}
