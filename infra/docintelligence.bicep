param docIntelAccountName string 
param location string = resourceGroup().location

resource docIntelAccount 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: docIntelAccountName
  location: location
  kind: 'CognitiveServices'
  identity: {
    type: 'SystemAssigned'
  }
  sku: {
    name: 'S0'
  }
 properties: {
   publicNetworkAccess: 'Enabled'
   disableLocalAuth: true
   customSubDomainName: docIntelAccountName
  }
}

output docIntelPrincipalId string = docIntelAccount.identity.principalId
output docIntelEndpoint string = docIntelAccount.properties.endpoint
