@description('Short, brand-neutral name for this deployment. Used for resource name prefixes.')
@maxLength(15)
@minLength(3)
param projectName string = 'flockfoundry'

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Logical tenant identifier used during normalization and partitioning.')
param tenantId string = 'tenant-demo-123'

@description('Cosmos DB database name.')
param cosmosDatabaseName string = 'flockdata'

@description('Cosmos DB container name that stores normalized flock snapshots.')
param cosmosContainerName string = 'normalized'

@description('Cosmos DB container name that stores raw telemetry snapshots (batched sensors[]).')
param cosmosTelemetryContainerName string = 'raw_telemetry'

@description('Azure AI Foundry / Cognitive Services SKU.')
param aiServiceSku string = 'S0'

@description('Azure OpenAI endpoint for server-side chat orchestration (e.g., https://<resource>.openai.azure.com). Leave blank to disable /api/chat.')
param azureOpenAiEndpoint string = ''

@description('Azure OpenAI deployment name for server-side chat orchestration (e.g., gpt-4o-mini). Leave blank to disable /api/chat.')
param azureOpenAiDeployment string = ''

@secure()
@description('Azure OpenAI API key for server-side chat orchestration (demo only; move to Key Vault after demo). Leave blank to disable /api/chat.')
param azureOpenAiApiKey string = ''

var tags = {
  project: projectName
}

var sanitizedAlphaNum = toLower(replace(replace(projectName, '-', ''), '_', ''))
var dashedProjectName = toLower(replace(replace(projectName, '_', '-'), ' ', ''))

var storageSuffix = uniqueString(resourceGroup().id, 'sa')
var keyVaultSuffix = uniqueString(resourceGroup().id, 'kv')
var aiSuffix = uniqueString(resourceGroup().id, 'ai')

var storageAccountName = take('sa${sanitizedAlphaNum}${storageSuffix}', 24)
var keyVaultName = toLower(take('${dashedProjectName}-kv-${keyVaultSuffix}', 24))
var aiAccountName = toLower('ai-${dashedProjectName}-${aiSuffix}')
var appInsightsName = toLower('appi-${dashedProjectName}')
var cosmosSuffix = uniqueString(resourceGroup().id, 'cos')
var cosmosAccountName = take('cos${sanitizedAlphaNum}${cosmosSuffix}', 44)
var cosmosEndpoint = 'https://${cosmosAccountName}.documents.azure.com:443/'
var acrSuffix = uniqueString(resourceGroup().id, 'acr')
var acrName = take('acr${sanitizedAlphaNum}${acrSuffix}', 50)
var containerAppName = toLower('ca-${dashedProjectName}')
var containerEnvName = toLower('cae-${dashedProjectName}')
var logAnalyticsName = toLower('law-${dashedProjectName}')

var tenantContainerName = toLower(replace(replace('tenant-${tenantId}', '_', '-'), ' ', '-'))
var knowledgeContainerName = 'flock-knowledge-base'
var searchServiceName = toLower('search-${dashedProjectName}')
var knowledgeIndexName = 'flockcopilot-knowledge'
var knowledgeDataSourceName = 'flockcopilot-docs'
var knowledgeIndexerName = 'flockcopilot-indexer'
var searchServiceEndpoint = 'https://${searchServiceName}.search.windows.net'

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  tags: tags
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  name: 'default'
  parent: storageAccount
  properties: {}
}

resource ingestContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  name: tenantContainerName
  parent: blobService
  properties: {
    publicAccess: 'None'
  }
}

resource knowledgeContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  name: knowledgeContainerName
  parent: blobService
  properties: {
    publicAccess: 'None'
  }
}

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: logAnalyticsName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: acrName
  location: location
  tags: tags
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false
    publicNetworkAccess: 'Enabled'
  }
}

resource containerAppsEnv 'Microsoft.App/managedEnvironments@2023-05-01' = {
  name: containerEnvName
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
  }
}

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2023-04-15' = {
  name: cosmosAccountName
  location: location
  tags: tags
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    enableFreeTier: false
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    capabilities: [
      {
        name: 'EnableServerless'
      }
    ]
    publicNetworkAccess: 'Enabled'
  }
}

resource cosmosDatabase 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2023-04-15' = {
  name: cosmosDatabaseName
  parent: cosmosAccount
  properties: {
    resource: {
      id: cosmosDatabaseName
    }
  }
}

resource cosmosContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2023-04-15' = {
  name: cosmosContainerName
  parent: cosmosDatabase
  properties: {
    resource: {
      id: cosmosContainerName
      partitionKey: {
        paths: [
          '/tenantId'
        ]
        kind: 'Hash'
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        includedPaths: [
          {
            path: '/*'
          }
        ]
        excludedPaths: [
          {
            path: '/"_etag"/?'
          }
        ]
      }
      uniqueKeyPolicy: {
        uniqueKeys: [
          {
            paths: [
              '/tenantId'
              '/flockId'
              '/timestamp'
            ]
          }
        ]
      }
    }
  }
}

resource cosmosTelemetryContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2023-04-15' = {
  name: cosmosTelemetryContainerName
  parent: cosmosDatabase
  properties: {
    resource: {
      id: cosmosTelemetryContainerName
      partitionKey: {
        paths: [
          '/tenantId'
        ]
        kind: 'Hash'
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        includedPaths: [
          {
            path: '/*'
          }
        ]
        excludedPaths: [
          {
            path: '/"_etag"/?'
          }
        ]
      }
      // Bound demo costs: expire raw telemetry automatically (seconds)
      // 86400 = 1 day
      defaultTtl: 86400
    }
  }
}

resource aiAccount 'Microsoft.CognitiveServices/accounts@2023-05-01' = {
  name: aiAccountName
  location: location
  tags: tags
  kind: 'AIServices'
  sku: {
    name: aiServiceSku
  }
  properties: {
    customSubDomainName: toLower('${dashedProjectName}${aiSuffix}')
    publicNetworkAccess: 'Enabled'
  }
}

resource searchService 'Microsoft.Search/searchServices@2023-11-01' = {
  name: searchServiceName
  location: location
  tags: tags
  sku: {
    name: 'standard'
  }
  properties: {
    hostingMode: 'default'
    partitionCount: 1
    replicaCount: 1
    publicNetworkAccess: 'enabled'
    disableLocalAuth: false
  }
}

// Azure Search service is provisioned above. Data-plane assets (data source, index, indexer)
// are created via scripts/setup-search.sh after deployment because the Search management plane
// does not currently support declarative creation of those resources.

resource containerApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: containerAppName
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: containerAppsEnv.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        allowInsecure: false
        transport: 'auto'
      }
      registries: [
        {
          server: acr.properties.loginServer
          identity: 'system'
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'flockcopilot-api'
          // Use placeholder image for initial deployment, will be updated by build-and-push.sh
          image: 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              value: appInsights.properties.ConnectionString
            }
            {
              name: 'DEFAULT_TENANT_ID'
              value: tenantId
            }
            {
              name: 'INGEST_STORAGE_ACCOUNT'
              value: storageAccount.name
            }
            {
              name: 'INGEST_STORAGE_CONTAINER'
              value: tenantContainerName
            }
            {
              name: 'AI_SERVICE_ENDPOINT'
              value: aiAccount.properties.endpoint
            }
            {
              name: 'AI_SERVICE_RESOURCE_ID'
              value: aiAccount.id
            }
            {
              name: 'COSMOS_DB_ACCOUNT'
              value: cosmosEndpoint
            }
            {
              name: 'COSMOS_DB_DATABASE'
              value: cosmosDatabaseName
            }
            {
              name: 'COSMOS_DB_CONTAINER'
              value: cosmosContainerName
            }
            {
              name: 'COSMOS_DB_TELEMETRY_CONTAINER'
              value: cosmosTelemetryContainerName
            }
            {
              name: 'KNOWLEDGE_STORAGE_CONTAINER'
              value: knowledgeContainerName
            }
            {
              name: 'AZURE_SEARCH_ENDPOINT'
              value: searchServiceEndpoint
            }
            {
              name: 'AZURE_SEARCH_INDEX'
              value: knowledgeIndexName
            }
            {
              name: 'AZURE_SEARCH_API_KEY'
              value: searchService.listAdminKeys().primaryKey
            }
            {
              name: 'AZURE_OPENAI_ENDPOINT'
              value: azureOpenAiEndpoint
            }
            {
              name: 'AZURE_OPENAI_DEPLOYMENT'
              value: azureOpenAiDeployment
            }
            {
              name: 'AZURE_OPENAI_API_KEY'
              value: azureOpenAiApiKey
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 10
        rules: [
          {
            name: 'http-scaling'
            http: {
              metadata: {
                concurrentRequests: '50'
              }
            }
          }
        ]
      }
    }
  }
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enabledForTemplateDeployment: true
    enabledForDeployment: false
    enabledForDiskEncryption: false
    enableRbacAuthorization: false
    accessPolicies: [
      {
        tenantId: subscription().tenantId
        objectId: containerApp.identity.principalId
        permissions: {
          secrets: [
            'Get'
            'List'
            'Set'
          ]
        }
      }
    ]
  }
}

var blobContributorRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
var acrPullRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')

resource storageBlobRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(subscription().id, storageAccount.id, containerApp.name, 'blob-data-contributor')
  scope: storageAccount
  properties: {
    principalId: containerApp.identity.principalId
    roleDefinitionId: blobContributorRoleId
    principalType: 'ServicePrincipal'
  }
}

// Cosmos DB data-plane access uses Cosmos SQL RBAC (not Azure RBAC roleAssignments).
// Assign the built-in "Cosmos DB Built-in Data Contributor" role to the Container App managed identity.
resource cosmosSqlRoleAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2023-04-15' = {
  name: guid(cosmosAccount.id, containerApp.name, 'sql-data-contributor')
  parent: cosmosAccount
  properties: {
    roleDefinitionId: '${cosmosAccount.id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002'
    principalId: containerApp.identity.principalId
    scope: '${cosmosAccount.id}/dbs/${cosmosDatabaseName}/colls/${cosmosContainerName}'
  }
}

// Optional broader scope to support additional containers (e.g., raw telemetry) without adding more role assignments.
resource cosmosSqlRoleAssignmentDbScope 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2023-04-15' = {
  name: guid(cosmosAccount.id, containerApp.name, 'sql-data-contributor-db')
  parent: cosmosAccount
  properties: {
    roleDefinitionId: '${cosmosAccount.id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002'
    principalId: containerApp.identity.principalId
    // Cosmos SQL RBAC "scope" must use the data-plane resource path format (/dbs/...), not the ARM resource id (/sqlDatabases/...).
    scope: '${cosmosAccount.id}/dbs/${cosmosDatabaseName}'
  }
}

resource acrPullRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(subscription().id, acr.id, containerApp.name, 'acr-pull')
  scope: acr
  properties: {
    principalId: containerApp.identity.principalId
    roleDefinitionId: acrPullRoleId
    principalType: 'ServicePrincipal'
  }
}

output containerAppName string = containerApp.name
output apiUrl string = 'https://${containerApp.properties.configuration.ingress.fqdn}'
output openapiUrl string = 'https://${containerApp.properties.configuration.ingress.fqdn}/swagger/v1/swagger.json'
output containerAppPrincipalId string = containerApp.identity.principalId
output acrLoginServer string = acr.properties.loginServer
output acrName string = acr.name
output storageAccountName string = storageAccount.name
output ingestContainer string = tenantContainerName
output aiServiceEndpoint string = aiAccount.properties.endpoint
output keyVaultName string = keyVault.name
output cosmosAccountEndpoint string = cosmosEndpoint
output cosmosDatabase string = cosmosDatabaseName
output cosmosContainer string = cosmosContainerName
output cosmosTelemetryContainer string = cosmosTelemetryContainerName
output logAnalyticsWorkspaceId string = logAnalytics.id
output knowledgeContainer string = knowledgeContainerName
output azureSearchServiceName string = searchService.name
output azureSearchEndpoint string = searchServiceEndpoint
output azureSearchIndex string = knowledgeIndexName
output azureSearchDataSource string = knowledgeDataSourceName
output azureSearchIndexer string = knowledgeIndexerName
