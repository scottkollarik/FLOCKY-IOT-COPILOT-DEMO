// APIM Configuration Module for FlockCopilot
// References existing shared services APIM instance (does NOT create/destroy it)
// Configures FlockCopilot API backend, operations, and policies

@description('Name of an existing shared APIM instance (this module never creates/destroys APIM).')
param apimName string = 'apim-shared'
param containerAppUrl string

var passthroughMethods = [
  'GET'
  'POST'
  'PUT'
  'PATCH'
  'DELETE'
  'OPTIONS'
]

// Reference existing APIM instance (shared service - never destroyed)
// NOTE: Deploy this to the same resource group where APIM exists
resource apim 'Microsoft.ApiManagement/service@2023-05-01-preview' existing = {
  name: apimName
}

// Create FlockCopilot API backend pointing to Container App
resource flockCopilotBackend 'Microsoft.ApiManagement/service/backends@2023-05-01-preview' = {
  name: 'flockcopilot-backend'
  parent: apim
  properties: {
    description: 'FlockCopilot Container App Backend'
    url: containerAppUrl
    protocol: 'http'
    tls: {
      validateCertificateChain: true
      validateCertificateName: true
    }
  }
}

// Create FlockCopilot API definition
// Note: Using blank API template initially, can import OpenAPI later once app is fully running
resource flockCopilotApi 'Microsoft.ApiManagement/service/apis@2023-05-01-preview' = {
  name: 'flockcopilot-api'
  parent: apim
  properties: {
    displayName: 'FlockCopilot Diagnostic API'
    description: 'Multi-sensor zone-based IoT monitoring and AI diagnostics for poultry facilities'
    path: 'flockcopilot'
    protocols: [
      'https'
    ]
    subscriptionRequired: false
    type: 'http'
    serviceUrl: containerAppUrl
  }
}

// Passthrough operations for each HTTP method (allows /flockcopilot/<any-path>)
resource passthroughOperations 'Microsoft.ApiManagement/service/apis/operations@2023-05-01-preview' = [for method in passthroughMethods: {
  name: 'proxy-${toLower(method)}'
  parent: flockCopilotApi
  properties: {
    displayName: '${method} passthrough'
    method: method
    urlTemplate: '/{*path}'
    templateParameters: [
      {
        name: 'path'
        required: false
        type: 'string'
      }
    ]
  }
}]

// Policy: Route to backend and add CORS
resource flockCopilotApiPolicy 'Microsoft.ApiManagement/service/apis/policies@2023-05-01-preview' = {
  name: 'policy'
  parent: flockCopilotApi
  properties: {
    format: 'rawxml'
    value: '''
<policies>
  <inbound>
    <base />
    <cors allow-credentials="false">
      <allowed-origins>
        <origin>*</origin>
      </allowed-origins>
      <allowed-methods>
        <method>GET</method>
        <method>POST</method>
        <method>PUT</method>
        <method>DELETE</method>
        <method>OPTIONS</method>
      </allowed-methods>
      <allowed-headers>
        <header>*</header>
      </allowed-headers>
    </cors>
    <set-backend-service backend-id="flockcopilot-backend" />
  </inbound>
  <backend>
    <base />
  </backend>
  <outbound>
    <base />
  </outbound>
  <on-error>
    <base />
  </on-error>
</policies>
'''
  }
}

// Output the APIM gateway URLs
output apimGatewayUrl string = 'https://${apim.name}.azure-api.net'
output flockCopilotApiUrl string = 'https://${apim.name}.azure-api.net/flockcopilot'
output flockCopilotOpenApiUrl string = 'https://${apim.name}.azure-api.net/flockcopilot/swagger/v1/swagger.json'
