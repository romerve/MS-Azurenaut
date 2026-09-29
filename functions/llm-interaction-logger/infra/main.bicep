targetScope = 'resourceGroup'

@description('Environment name used to derive resource names (for example the azd environment name).')
@minLength(1)
@maxLength(32)
param environmentName string

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Maximum Flex Consumption instances.')
@minValue(1)
@maxValue(1000)
param maximumInstanceCount int = 100

@description('Memory per Flex Consumption instance, in MB.')
@allowed([512, 2048, 4096])
param instanceMemoryMB int = 2048

@description('Log Analytics / Application Insights retention in days.')
@minValue(30)
@maxValue(730)
param logRetentionInDays int = 90

var tags = { 'azd-env-name': environmentName, workload: 'llm-interaction-logger' }
var token = toLower(uniqueString(subscription().id, resourceGroup().id, environmentName))
var functionAppName = 'func-llmlog-${token}'
var deploymentContainerName = 'app-package-${take(token, 7)}'

// Built-in role definition IDs.
var storageBlobDataOwner = 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
var monitoringMetricsPublisher = '3913510d-42f4-4e42-8a64-420c390055eb'

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-llmlog-${token}'
  location: location
  tags: tags
}

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-llmlog-${token}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: logRetentionInDays
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-llmlog-${token}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
    RetentionInDays: logRetentionInDays
    DisableLocalAuth: true
  }
}

// Functions host storage (deployment package, triggers, locks).
resource hostStorage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: 'stllmhost${take(token, 14)}'
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    supportsHttpsTrafficOnly: true
  }
}

resource hostBlobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: hostStorage
  name: 'default'
}

resource deploymentContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: hostBlobService
  name: deploymentContainerName
}

resource hostStorageRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(hostStorage.id, identity.id, storageBlobDataOwner)
  scope: hostStorage
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataOwner)
  }
}

resource appInsightsRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(appInsights.id, identity.id, monitoringMetricsPublisher)
  scope: appInsights
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', monitoringMetricsPublisher)
  }
}

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: 'plan-llmlog-${token}'
  location: location
  tags: tags
  kind: 'functionapp'
  sku: { tier: 'FlexConsumption', name: 'FC1' }
  properties: { reserved: true }
}

resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: functionAppName
  location: location
  tags: union(tags, { 'azd-service-name': 'llm-interaction-logger' })
  kind: 'functionapp,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identity.id}': {} }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${hostStorage.properties.primaryEndpoints.blob}${deploymentContainerName}'
          authentication: {
            type: 'UserAssignedIdentity'
            userAssignedIdentityResourceId: identity.id
          }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: maximumInstanceCount
        instanceMemoryMB: instanceMemoryMB
      }
      runtime: { name: 'dotnet-isolated', version: '10.0' }
    }
    siteConfig: {
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      appSettings: [
        { name: 'AzureWebJobsStorage__accountName', value: hostStorage.name }
        { name: 'AzureWebJobsStorage__credential', value: 'managedidentity' }
        { name: 'AzureWebJobsStorage__clientId', value: identity.properties.clientId }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
        { name: 'APPLICATIONINSIGHTS_AUTHENTICATION_STRING', value: 'ClientId=${identity.properties.clientId};Authorization=AAD' }
        { name: 'AZURE_CLIENT_ID', value: identity.properties.clientId }
      ]
    }
  }
  dependsOn: [
    deploymentContainer
    hostStorageRole
    appInsightsRole
  ]
}

output AZURE_FUNCTION_APP_NAME string = functionApp.name
output FUNCTION_ENDPOINT string = 'https://${functionApp.properties.defaultHostName}/api/llm-log'
output APPLICATIONINSIGHTS_NAME string = appInsights.name
