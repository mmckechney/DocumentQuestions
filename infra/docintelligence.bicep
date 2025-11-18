param docIntelAccountName string 
param keyVaultName string
param location string = resourceGroup().location

var kvKeys = loadJsonContent('./kvKeys.json')

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
  properties:{
    
  }
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-02-01' existing = {
  name: keyVaultName
}

resource adminKey 'Microsoft.KeyVault/vaults/secrets@2023-02-01' = {
  parent: keyVault
  name: kvKeys.DOCUMENTINTELLIGENCE_KEY
  properties: {
    value:  docIntelAccount.listKeys().key1
  }
}
output docIntelPrincipalId string = docIntelAccount.identity.principalId
output docIntelEndpoint string = docIntelAccount.properties.endpoint

