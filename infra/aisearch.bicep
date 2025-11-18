param aiSearchName string
param location string = resourceGroup().location
param keyVaultName string
var kvKeys = loadJsonContent('./kvKeys.json')
resource aiSearchInstance 'Microsoft.Search/searchServices@2023-11-01' = {
  name: aiSearchName
  location: location
  sku: {
    name: 'basic'
  }
   properties: {
      hostingMode: 'default'
      disableLocalAuth: false
      authOptions: {
          aadOrApiKey: {
              
          }
      }
    }
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-02-01' existing = {
  name: keyVaultName
}

resource adminKey 'Microsoft.KeyVault/vaults/secrets@2023-02-01' = {
  parent: keyVault
  name: kvKeys.AISEARCH_KEY
  properties: {
    value:  aiSearchInstance.listAdminKeys().primaryKey
  }
}

output aiSearchEndpoint string = 'https://${aiSearchInstance.name}.search.windows.net'
