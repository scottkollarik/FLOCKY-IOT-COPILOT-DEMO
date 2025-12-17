#!/usr/bin/env bash
set -euo pipefail

PREFIX=${1:-flockfoundry}
LOCATION=${2:-eastus}
RG_NAME=${3:-rg-${PREFIX}}
DEPLOY_NAME="${PREFIX}-infra"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TEMPLATE_PATH="${SCRIPT_DIR}/main.bicep"

command -v jq >/dev/null 2>&1 || {
  echo "jq is required (brew install jq)." >&2
  exit 1
}

echo "➡️ Logging in to Azure (if needed)..."
az account show >/dev/null 2>&1 || az login

echo "➡️ Creating resource group: $RG_NAME in $LOCATION"
az group create --name "$RG_NAME" --location "$LOCATION" >/dev/null

echo "➡️ Deploying Bicep template..."
PARAM_ARGS=(projectName="$PREFIX" location="$LOCATION")
if [ -n "${AZURE_OPENAI_ENDPOINT:-}" ]; then
  PARAM_ARGS+=(azureOpenAiEndpoint="$AZURE_OPENAI_ENDPOINT")
fi
if [ -n "${AZURE_OPENAI_DEPLOYMENT:-}" ]; then
  PARAM_ARGS+=(azureOpenAiDeployment="$AZURE_OPENAI_DEPLOYMENT")
fi
if [ -n "${AZURE_OPENAI_API_KEY:-}" ]; then
  PARAM_ARGS+=(azureOpenAiApiKey="$AZURE_OPENAI_API_KEY")
fi

az deployment group create \
  --name "$DEPLOY_NAME" \
  --resource-group "$RG_NAME" \
  --template-file "$TEMPLATE_PATH" \
  --parameters "${PARAM_ARGS[@]}"

echo "➡️ Reading deployment outputs..."
DEPLOY_OUTPUTS=$(az deployment group show \
  --resource-group "$RG_NAME" \
  --name "$DEPLOY_NAME" \
  --query "properties.outputs" -o json)

get_output() {
  local key="$1"
  echo "$DEPLOY_OUTPUTS" | jq -r --arg k "$key" '.[$k].value // ""'
}

CONTAINER_APP_NAME=$(get_output containerAppName)
CONTAINER_APP_URL=$(get_output apiUrl)
OPENAPI_URL=$(get_output openapiUrl)
CONTAINER_APP_MI=$(get_output containerAppPrincipalId)
ACR_NAME=$(get_output acrName)
ACR_SERVER=$(get_output acrLoginServer)
STORAGE_ACCOUNT=$(get_output storageAccountName)
INGEST_CONTAINER=$(get_output ingestContainer)
KNOWLEDGE_CONTAINER=$(get_output knowledgeContainer)
COSMOS_ACCOUNT=$(get_output cosmosAccountEndpoint)
COSMOS_DB=$(get_output cosmosDatabase)
COSMOS_CONTAINER=$(get_output cosmosContainer)
LOG_ANALYTICS=$(get_output logAnalyticsWorkspaceId)
KEY_VAULT=$(get_output keyVaultName)
AI_ENDPOINT=$(get_output aiServiceEndpoint)
SEARCH_SERVICE=$(get_output azureSearchServiceName)
SEARCH_ENDPOINT=$(get_output azureSearchEndpoint)
SEARCH_INDEX=$(get_output azureSearchIndex)
SEARCH_DATA_SOURCE=$(get_output azureSearchDataSource)
SEARCH_INDEXER=$(get_output azureSearchIndexer)

echo ""
echo "✅ Core infrastructure deployment complete."
echo "Resource group:            $RG_NAME"
echo "Container App Name:        $CONTAINER_APP_NAME"
echo "Container App URL:         $CONTAINER_APP_URL"
echo "OpenAPI Spec:              $OPENAPI_URL"
echo "Container App MI (object): $CONTAINER_APP_MI"
echo "ACR:                       $ACR_NAME ($ACR_SERVER)"
echo "Storage Account:           $STORAGE_ACCOUNT"
echo "  Ingest Container:        $INGEST_CONTAINER"
echo "  Knowledge Container:     $KNOWLEDGE_CONTAINER"
echo "Cosmos DB:                 $COSMOS_ACCOUNT (db: $COSMOS_DB / container: $COSMOS_CONTAINER)"
echo "Key Vault:                 $KEY_VAULT"
echo "Azure AI Endpoint:         $AI_ENDPOINT"
echo "Log Analytics Workspace:   $LOG_ANALYTICS"
echo "Azure AI Search:           $SEARCH_SERVICE ($SEARCH_ENDPOINT)"
echo "  Index/DataSource/Indexer: $SEARCH_INDEX / $SEARCH_DATA_SOURCE / $SEARCH_INDEXER"
echo "  Admin key is not printed by this script."

# Optional: Configure APIM (requires shared services APIM instance)
if [ "${CONFIGURE_APIM:-false}" = "true" ]; then
  echo ""
  echo "➡️ Configuring APIM integration..."

  APIM_NAME=${APIM_NAME:-apim-shared}
  APIM_RG=${APIM_RG:-rg-shared-services}

  if [ -z "$CONTAINER_APP_URL" ]; then
    echo "⚠️  Container App URL not available; skipping APIM configuration."
  elif az apim show --name "$APIM_NAME" --resource-group "$APIM_RG" >/dev/null 2>&1; then
    echo "   Found shared APIM: $APIM_NAME"

    az deployment group create \
      --name "${PREFIX}-apim-config" \
      --resource-group "$APIM_RG" \
      --template-file "${SCRIPT_DIR}/apim-config.bicep" \
      --parameters \
        apimName="$APIM_NAME" \
        containerAppUrl="$CONTAINER_APP_URL"

    APIM_GATEWAY_URL=$(az deployment group show \
      --resource-group "$APIM_RG" \
      --name "${PREFIX}-apim-config" \
      --query "properties.outputs.apimGatewayUrl.value" -o tsv)

    APIM_API_URL=$(az deployment group show \
      --resource-group "$APIM_RG" \
      --name "${PREFIX}-apim-config" \
      --query "properties.outputs.flockCopilotApiUrl.value" -o tsv)

    echo ""
    echo "✅ APIM configuration complete."
    echo "APIM Gateway URL:    $APIM_GATEWAY_URL"
    echo "FlockCopilot API:    $APIM_API_URL"
    echo ""
    echo "🔗 Access API via APIM: $APIM_API_URL/health"
  else
    echo "⚠️  Shared APIM not found: $APIM_NAME in $APIM_RG"
    echo "   Skipping APIM configuration."
  fi
else
  echo ""
  echo "ℹ️  APIM configuration skipped (set CONFIGURE_APIM=true to enable)"
  echo "   Direct API access: $CONTAINER_APP_URL"
fi

echo ""
echo "📘 Reminder: Run ./scripts/setup-search.sh $PREFIX $LOCATION $RG_NAME to (re)create"
echo "             the Azure AI Search data source, index, and indexer after deployments."
