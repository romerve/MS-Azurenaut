targetScope = 'resourceGroup'

@description('Short name used to derive globally unique resource names.')
@minLength(3)
@maxLength(20)
param environmentName string

@description('Azure region for all regional resources.')
param location string = resourceGroup().location

@description('Object ID of the Microsoft Entra user, group, or application that administers Azure SQL.')
param entraAdminObjectId string

@description('Display name of the Microsoft Entra SQL administrator.')
param entraAdminName string

@description('Principal type of the Microsoft Entra SQL administrator.')
@allowed([
  'User'
  'Group'
  'Application'
])
param entraAdminPrincipalType string = 'User'

@description('Edge API image reference.')
param edgeImage string

@description('Orders service image.')
param ordersImage string

@description('Inventory service image.')
param inventoryImage string

@description('Fulfillment service image.')
param fulfillmentImage string

@description('Azure Container Registry SKU.')
@allowed([
  'Basic'
  'Standard'
  'Premium'
])
param registrySku string = 'Basic'

@description('Azure SQL Database SKU used independently by Orders, Inventory, and Fulfillment.')
@allowed([
  'Basic'
  'S0'
])
param sqlDatabaseSku string = 'Basic'

@description('SQL network posture. Private is the secure default; AllowAzureServices is only for time-boxed demos.')
@allowed([
  'Private'
  'AllowAzureServices'
])
param sqlNetworkMode string = 'Private'

@description('Minimum replicas for each Container App. Zero minimizes idle workshop cost.')
@minValue(0)
@maxValue(3)
param minReplicas int = 1

@description('Maximum replicas for each Container App.')
@minValue(1)
@maxValue(10)
param maxReplicas int = 3

@description('Log Analytics retention in days.')
@minValue(30)
@maxValue(730)
param logRetentionDays int = 30

@description('Tags applied to workshop resources.')
param tags object = {
  workload: 'github-copilot-azure-modernization'
  environment: environmentName
}

var resourceToken = take(uniqueString(subscription().id, resourceGroup().id, environmentName), 8)
var namePrefix = 'copilot-${resourceToken}'
var registryName = 'copilot${resourceToken}'
var sqlServerName = '${namePrefix}-sql'
var serviceNames = [
  'edge'
  'orders'
  'inventory'
  'fulfillment'
]
var acrPullRoleDefinitionId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '7f951dda-4ed3-4680-a7ca-43fe172d538d'
)

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: '${namePrefix}-logs'
  location: location
  tags: tags
  properties: {
    retentionInDays: logRetentionDays
    sku: {
      name: 'PerGB2018'
    }
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${namePrefix}-appinsights'
  location: location
  kind: 'web'
  tags: tags
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

resource identities 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = [
  for serviceName in serviceNames: {
    name: '${namePrefix}-${serviceName}'
    location: location
    tags: union(tags, { service: serviceName })
  }
]

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: registryName
  location: location
  tags: tags
  sku: {
    name: registrySku
  }
  properties: {
    adminUserEnabled: false
    dataEndpointEnabled: false
    publicNetworkAccess: 'Enabled'
    zoneRedundancy: registrySku == 'Premium' ? 'Enabled' : 'Disabled'
  }
}

resource registryPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for index in range(0, length(serviceNames)): {
    name: guid(registry.id, identities[index].id, acrPullRoleDefinitionId)
    scope: registry
    properties: {
      roleDefinitionId: acrPullRoleDefinitionId
      principalId: identities[index].properties.principalId
      principalType: 'ServicePrincipal'
    }
  }
]

resource virtualNetwork 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: '${namePrefix}-vnet'
  location: location
  tags: tags
  properties: {
    addressSpace: {
      addressPrefixes: [
        '10.20.0.0/16'
      ]
    }
    subnets: [
      {
        name: 'container-apps-infrastructure'
        properties: {
          addressPrefix: '10.20.0.0/23'
          delegations: [
            {
              name: 'Microsoft.App.environments'
              properties: {
                serviceName: 'Microsoft.App/environments'
              }
            }
          ]
        }
      }
      {
        name: 'private-endpoints'
        properties: {
          addressPrefix: '10.20.2.0/24'
          privateEndpointNetworkPolicies: 'Disabled'
        }
      }
    ]
  }
}

resource infrastructureSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' existing = {
  parent: virtualNetwork
  name: 'container-apps-infrastructure'
}

resource privateEndpointSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' existing = {
  parent: virtualNetwork
  name: 'private-endpoints'
}

resource sqlServer 'Microsoft.Sql/servers@2022-05-01-preview' = {
  name: sqlServerName
  location: location
  tags: tags
  properties: {
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: entraAdminPrincipalType
      login: entraAdminName
      sid: entraAdminObjectId
      tenantId: subscription().tenantId
      azureADOnlyAuthentication: true
    }
    minimalTlsVersion: '1.2'
    publicNetworkAccess: sqlNetworkMode == 'Private' ? 'Disabled' : 'Enabled'
    restrictOutboundNetworkAccess: 'Enabled'
  }
}

resource sqlDatabases 'Microsoft.Sql/servers/databases@2022-05-01-preview' = [
  for serviceName in skip(serviceNames, 1): {
    parent: sqlServer
    name: serviceName
    location: location
    tags: union(tags, { dataOwner: serviceName })
    sku: {
      name: sqlDatabaseSku
      tier: sqlDatabaseSku == 'Basic' ? 'Basic' : 'Standard'
    }
    properties: {
      collation: 'SQL_Latin1_General_CP1_CI_AS'
      maxSizeBytes: 2147483648
      zoneRedundant: false
    }
  }
]

resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2022-05-01-preview' = if (sqlNetworkMode == 'AllowAzureServices') {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource privateDnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: 'privatelink${environment().suffixes.sqlServerHostname}'
  location: 'global'
  tags: tags
}

resource privateDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: privateDnsZone
  name: '${namePrefix}-sql-link'
  location: 'global'
  tags: tags
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: virtualNetwork.id
    }
  }
}

resource sqlPrivateEndpoint 'Microsoft.Network/privateEndpoints@2024-05-01' = {
  name: '${namePrefix}-sql-private-endpoint'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: privateEndpointSubnet.id
    }
    privateLinkServiceConnections: [
      {
        name: 'sql'
        properties: {
          privateLinkServiceId: sqlServer.id
          groupIds: [
            'sqlServer'
          ]
        }
      }
    ]
  }
}

resource sqlPrivateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = {
  parent: sqlPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'sql'
        properties: {
          privateDnsZoneId: privateDnsZone.id
        }
      }
    ]
  }
}

resource containerEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-${resourceToken}'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
    vnetConfiguration: {
      infrastructureSubnetId: infrastructureSubnet.id
      internal: false
    }
    zoneRedundant: false
  }
}

var ordersConnectionString = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=orders;Authentication=Active Directory Managed Identity;User Id=${identities[1].properties.clientId};Encrypt=True;TrustServerCertificate=False;'
var inventoryConnectionString = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=inventory;Authentication=Active Directory Managed Identity;User Id=${identities[2].properties.clientId};Encrypt=True;TrustServerCertificate=False;'
var fulfillmentConnectionString = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=fulfillment;Authentication=Active Directory Managed Identity;User Id=${identities[3].properties.clientId};Encrypt=True;TrustServerCertificate=False;'

module inventoryApp 'container-app.bicep' = {
  name: 'inventory-container-app'
  params: {
    name: '${namePrefix}-inventory'
    location: location
    managedEnvironmentId: containerEnvironment.id
    image: inventoryImage
    identityId: identities[2].id
    registryServer: registry.properties.loginServer
    externalIngress: false
    enableHealthProbes: true
    minReplicas: minReplicas
    maxReplicas: maxReplicas
    tags: union(tags, { service: 'inventory' })
    env: [
      {
        name: 'Database__Provider'
        value: 'SqlServer'
      }
      {
        name: 'Database__Initialize'
        value: 'false'
      }
      {
        name: 'ConnectionStrings__Inventory'
        value: inventoryConnectionString
      }
      {
        name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
        value: appInsights.properties.ConnectionString
      }
    ]
  }
  dependsOn: [
    registryPull
    sqlPrivateDnsZoneGroup
  ]
}

module ordersApp 'container-app.bicep' = {
  name: 'orders-container-app'
  params: {
    name: '${namePrefix}-orders'
    location: location
    managedEnvironmentId: containerEnvironment.id
    image: ordersImage
    identityId: identities[1].id
    registryServer: registry.properties.loginServer
    externalIngress: false
    enableHealthProbes: true
    minReplicas: minReplicas
    maxReplicas: maxReplicas
    tags: union(tags, { service: 'orders' })
    env: [
      {
        name: 'Database__Provider'
        value: 'SqlServer'
      }
      {
        name: 'Database__Initialize'
        value: 'false'
      }
      {
        name: 'ConnectionStrings__Orders'
        value: ordersConnectionString
      }
      {
        name: 'Services__Inventory'
        value: 'https://${inventoryApp.outputs.fqdn}'
      }
      {
        name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
        value: appInsights.properties.ConnectionString
      }
    ]
  }
  dependsOn: [
    registryPull
  ]
}

module fulfillmentApp 'container-app.bicep' = {
  name: 'fulfillment-container-app'
  params: {
    name: '${namePrefix}-fulfillment'
    location: location
    managedEnvironmentId: containerEnvironment.id
    image: fulfillmentImage
    identityId: identities[3].id
    registryServer: registry.properties.loginServer
    externalIngress: false
    enableHealthProbes: true
    minReplicas: minReplicas
    maxReplicas: maxReplicas
    tags: union(tags, { service: 'fulfillment' })
    env: [
      {
        name: 'Database__Provider'
        value: 'SqlServer'
      }
      {
        name: 'Database__Initialize'
        value: 'false'
      }
      {
        name: 'ConnectionStrings__Fulfillment'
        value: fulfillmentConnectionString
      }
      {
        name: 'Services__Orders'
        value: 'https://${ordersApp.outputs.fqdn}'
      }
      {
        name: 'Services__Inventory'
        value: 'https://${inventoryApp.outputs.fqdn}'
      }
      {
        name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
        value: appInsights.properties.ConnectionString
      }
    ]
  }
  dependsOn: [
    registryPull
  ]
}

module edgeApp 'container-app.bicep' = {
  name: 'edge-container-app'
  params: {
    name: '${namePrefix}-edge'
    location: location
    managedEnvironmentId: containerEnvironment.id
    image: edgeImage
    identityId: identities[0].id
    registryServer: registry.properties.loginServer
    externalIngress: true
    enableHealthProbes: true
    minReplicas: minReplicas
    maxReplicas: maxReplicas
    tags: union(tags, { service: 'edge' })
    env: [
      {
        name: 'Services__Orders'
        value: 'https://${ordersApp.outputs.fqdn}'
      }
      {
        name: 'Services__Inventory'
        value: 'https://${inventoryApp.outputs.fqdn}'
      }
      {
        name: 'Services__Fulfillment'
        value: 'https://${fulfillmentApp.outputs.fqdn}'
      }
      {
        name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
        value: appInsights.properties.ConnectionString
      }
    ]
  }
  dependsOn: [
    registryPull
  ]
}

output registryName string = registry.name
output registryLoginServer string = registry.properties.loginServer
output edgeAppName string = edgeApp.outputs.name
output edgeUrl string = 'https://${edgeApp.outputs.fqdn}'
output ordersAppName string = ordersApp.outputs.name
output inventoryAppName string = inventoryApp.outputs.name
output fulfillmentAppName string = fulfillmentApp.outputs.name
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output ordersDatabaseName string = sqlDatabases[0].name
output inventoryDatabaseName string = sqlDatabases[1].name
output fulfillmentDatabaseName string = sqlDatabases[2].name
output ordersIdentityName string = identities[1].name
output ordersIdentityClientId string = identities[1].properties.clientId
output inventoryIdentityName string = identities[2].name
output inventoryIdentityClientId string = identities[2].properties.clientId
output fulfillmentIdentityName string = identities[3].name
output fulfillmentIdentityClientId string = identities[3].properties.clientId
