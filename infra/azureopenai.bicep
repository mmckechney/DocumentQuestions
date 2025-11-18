@description('Name of the resource.')
param name string
param location string
param keyVaultName string
param chatModel string
param chatDeploymentName string
param embeddingModel string
param embeddingDeploymentName string

var kvKeys = loadJsonContent('./kvKeys.json')


resource azureOpenAi 'Microsoft.CognitiveServices/accounts@2023-10-01-preview' = {
  name: name
  location: location
  kind: 'OpenAI'
 
  properties: {
    customSubDomainName: toLower(name)
  }
  sku: {
    name: 'S0'
  }
}


resource chat 'Microsoft.CognitiveServices/accounts/deployments@2023-10-01-preview' =  {
  parent: azureOpenAi
  name: chatDeploymentName
  properties: {
    model:{
      name: chatModel
      format: 'OpenAI'
    }
  }
  sku: {
    name: 'GlobalStandard'
    capacity: 100
  }
}

resource embedding 'Microsoft.CognitiveServices/accounts/deployments@2023-10-01-preview' =  {
  parent: azureOpenAi
  name: embeddingDeploymentName
  properties: {
    model:{
      name: embeddingModel
      format: 'OpenAI'
      
    }
  }
  sku: {
    name: 'Standard'
    capacity: 100
  }
  dependsOn: [
    chat
  ]
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource adminKey 'Microsoft.KeyVault/vaults/secrets@2023-02-01' = {
  parent: keyVault
  name: kvKeys.OPENAI_KEY
  properties: {
    value:  azureOpenAi.listKeys().key1
  }
}

resource endpointKey 'Microsoft.KeyVault/vaults/secrets@2023-02-01' = {
  parent: keyVault
  name: kvKeys.OPENAI_ENDPOINT
  properties: {
    value:  azureOpenAi.properties.endpoint
  }
}


@description('ID for the deployed Cognitive Services resource.')
output id string = azureOpenAi.id
@description('Name for the deployed Cognitive Services resource.')
output name string = azureOpenAi.name
@description('Endpoint for the deployed Cognitive Services resource.')
output endpoint string = azureOpenAi.properties.endpoint

