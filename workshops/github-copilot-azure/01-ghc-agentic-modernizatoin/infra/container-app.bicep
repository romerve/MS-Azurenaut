@description('Container App name.')
param name string

@description('Azure region.')
param location string

@description('Managed Environment resource ID.')
param managedEnvironmentId string

@description('Container image.')
param image string

@description('User-assigned managed identity resource ID.')
param identityId string

@description('Container Registry login server.')
param registryServer string

@description('Whether ingress is reachable outside the Container Apps environment.')
param externalIngress bool

@description('Environment variables supplied to the container.')
param env array = []

@description('Whether to enable application-specific health probes.')
param enableHealthProbes bool = true

@minValue(0)
@maxValue(3)
param minReplicas int = 0

@minValue(1)
@maxValue(10)
param maxReplicas int = 3

param tags object = {}

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: managedEnvironmentId
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: externalIngress
        allowInsecure: false
        targetPort: 8080
        transport: 'auto'
      }
      registries: [
        {
          server: registryServer
          identity: identityId
        }
      ]
    }
    template: {
      containers: [
        {
          name: name
          image: image
          env: env
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          probes: enableHealthProbes
            ? [
                {
                  type: 'Liveness'
                  httpGet: {
                    path: '/health/live'
                    port: 8080
                    scheme: 'HTTP'
                  }
                  initialDelaySeconds: 10
                  periodSeconds: 15
                }
                {
                  type: 'Readiness'
                  httpGet: {
                    path: '/health/ready'
                    port: 8080
                    scheme: 'HTTP'
                  }
                  initialDelaySeconds: 10
                  periodSeconds: 10
                }
              ]
            : []
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
      }
    }
  }
}

output name string = app.name
output fqdn string = app.properties.configuration.ingress.fqdn
