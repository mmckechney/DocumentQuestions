#!/usr/bin/env pwsh

# Get the current user's object ID
$currentUserObjectId = az ad signed-in-user show -o tsv --query id
if (-not $currentUserObjectId) {
    Write-Error "Failed to get current user object ID. Make sure you're logged into Azure CLI."
    exit 1
}

$envValues = azd env get-values --output json | ConvertFrom-Json
$AZURE_LOCATION = $envValues.AZURE_LOCATION
$envValues = azd env get-values --output json | ConvertFrom-Json
$envName = $envValues.AZURE_ENV_NAME
$safeEnvName = $envName -replace '[^a-zA-Z0-9]', ''

# Set the user object ID as an environment variable for the deployment
#azd env set "AZURE_LOCATION" $AZURE_LOCATION
azd env set "AZURE_CURRENT_USER_OBJECT_ID" $currentUserObjectId
azd env set "AZURE_RESOURCEGROUP_NAME" "$envName-rg"
azd env set "AZURE_STORAGEACCT_NAME" "$($safeEnvName)storage"
azd env set "AZURE_DOCUMENTINTELLIGENCE_ACCOUNT_NAME" "$envName-aidoc"
azd env set "AZURE_AISEARCH_NAME" "$envName-aisearch"
azd env set "AZURE_AIFOUNDRY_NAME" "$envName-aifoundry"
azd env set "AZURE_KEYVAULT_NAME" "$envName-keyvault"


azd env get-values

