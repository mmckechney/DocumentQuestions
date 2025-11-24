targetScope = 'subscription'

@minLength(1)
@description('Primary location for all resources')
param location string
param resourceGroupName string
param keyVaultName string
param storageAccountName string
param docIntelligenceAccountName string
param aiSearchName string
param currentUserObjectId string
param aiFoundryName string
param appInsightsName string


var safeStorageAccountName = toLower(replace(storageAccountName, '-', ''))
resource rg 'Microsoft.Resources/resourceGroups@2022-09-01' = {
    name: resourceGroupName
    location: location
}

module keyVault 'keyvault.bicep' = {
    name: 'keyVault'
    scope: resourceGroup(resourceGroupName)
    params: {
        location: location
        keyVaultName: keyVaultName
    }
    dependsOn: [
        rg
    ]
}

module appInsights 'appinsights.bicep' = {
    name: 'appInsights'
    scope: resourceGroup(resourceGroupName)
    params: {
        appInsightsName: appInsightsName
        location: location
    }
    dependsOn: [
        rg
    ]
}

module aiFoundry 'aifoundryresource.bicep' = {
    name: 'aiFoundry'
    scope: resourceGroup(resourceGroupName)
    params: {
        aiFoundryName: aiFoundryName
        location: location
        appInsightsConnectionString: appInsights.outputs.connectionString
        appInsightsResourceId: appInsights.outputs.appInsightsId
        appInsightsResourceName: appInsightsName
    }
    dependsOn: [
        rg
    ]
}

module aiSearch 'aisearch.bicep' = {
    name: 'aiSearch'
    scope: resourceGroup(resourceGroupName)
    params: {
        aiSearchName: aiSearchName
        location: location
        keyVaultName: keyVaultName
    }
    dependsOn: [
        rg
        keyVault
    ]
}
module docIntelligence 'docintelligence.bicep' = {
    name: 'docIntelligence'
    scope: resourceGroup(resourceGroupName)
    params: {
        docIntelAccountName: docIntelligenceAccountName
        location: location
        keyVaultName: keyVaultName
    }
    dependsOn: [
        rg
        keyVault
    ]
}
module storageResources 'storage.bicep' = {
    name: 'storageResources'
    scope: resourceGroup(resourceGroupName)
    params: {
        storageAccountName: safeStorageAccountName
        location: location
    }
    dependsOn: [
        rg
        keyVault
    ]
}


module roleAssignments 'roleassignments.bicep' = {
    name: 'roleAssignments'
    scope: resourceGroup(resourceGroupName)
    params: {
  
        cogSvcsPrincipalId: docIntelligence.outputs.docIntelPrincipalId
        currentUserObjectId : currentUserObjectId
        aiFoundryPrincipalId: aiFoundry.outputs.docIntelPrincipalId

    }
    dependsOn: [
        rg
    ]
}

var openAiUserRole = '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'

resource user_openai_user_role 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
    name: guid(currentUserObjectId, openAiUserRole, subscription().id)
    properties: {
        roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', openAiUserRole)
        principalId: currentUserObjectId
    }
}

output docIntelEndpoint string = docIntelligence.outputs.docIntelEndpoint
output extractedContainerName string = storageResources.outputs.extractedContainerName
output rawContainerName string = storageResources.outputs.rawContainerName
output aiSearchEndpoint string = aiSearch.outputs.aiSearchEndpoint
output embeddingModelName string = aiFoundry.outputs.embeddingModelName
output chatModelName string = aiFoundry.outputs.chatModelName
output storageAccountName string = safeStorageAccountName
output aiFoundryProjectEndpoint string = aiFoundry.outputs.aiFoundryProjectEndpoint
output storageBlobEndpoint string = storageResources.outputs.blobEndpoint
output storageQueueEndpoint string = storageResources.outputs.queueEndpoint
output appInsightsConnectionString string = appInsights.outputs.connectionString


