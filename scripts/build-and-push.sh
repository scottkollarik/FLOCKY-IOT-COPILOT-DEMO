#!/bin/bash
set -euo pipefail

##############################################################################
# FlockCopilot API - Container Build & Deploy Script
#
# This script builds an AMD64 container image, pushes it to Azure Container
# Registry, and updates the Azure Container App.
#
# Prerequisites:
# - Docker (standard build, no buildx required)
# - Azure CLI logged in (az login)
# - Infrastructure deployed via ./iac/deploy.sh
#
# Usage:
#   ./scripts/build-and-push.sh [PREFIX] [RESOURCE_GROUP]
#
# Examples:
#   ./scripts/build-and-push.sh
#   ./scripts/build-and-push.sh flockfoundry rg-flockfoundry
##############################################################################

PREFIX="${1:-flockfoundry}"
RG_NAME="${2:-rg-${PREFIX}}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

echo "========================================="
echo "FlockCopilot API - Build & Deploy"
echo "========================================="
echo "PREFIX: $PREFIX"
echo "RESOURCE_GROUP: $RG_NAME"
echo ""

# Get ACR name and login server from deployment outputs
echo "[1/5] Fetching ACR details from resource group..."
DEPLOY_NAME="${PREFIX}-infra"

ACR_NAME=$(az deployment group show \
  --resource-group "$RG_NAME" \
  --name "$DEPLOY_NAME" \
  --query properties.outputs.acrName.value \
  --output tsv)

ACR_SERVER=$(az deployment group show \
  --resource-group "$RG_NAME" \
  --name "$DEPLOY_NAME" \
  --query properties.outputs.acrLoginServer.value \
  --output tsv)

CONTAINER_APP_NAME=$(az deployment group show \
  --resource-group "$RG_NAME" \
  --name "$DEPLOY_NAME" \
  --query properties.outputs.containerAppName.value \
  --output tsv)

if [ -z "$ACR_NAME" ] || [ -z "$ACR_SERVER" ] || [ -z "$CONTAINER_APP_NAME" ]; then
  echo "Error: Could not retrieve deployment outputs. Did you run ./iac/deploy.sh first?"
  exit 1
fi

echo "  ACR Name: $ACR_NAME"
echo "  ACR Server: $ACR_SERVER"
echo "  Container App: $CONTAINER_APP_NAME"
echo ""

# Log in to ACR
echo "[2/5] Logging into Azure Container Registry..."
az acr login --name "$ACR_NAME"
echo ""

# Build image for AMD64/x64 (Azure Container Apps platform)
IMAGE_TAG="${ACR_SERVER}/flockcopilot-api:latest"
BUILD_TAG="${ACR_SERVER}/flockcopilot-api:build-$(date +%Y%m%d-%H%M%S)"

echo "[3/5] Building container image for AMD64..."
echo "  Image: $IMAGE_TAG"
echo "  Build Tag: $BUILD_TAG"
echo "  Platform: linux/amd64"
echo ""

docker buildx build \
  --platform linux/amd64 \
  --tag "$IMAGE_TAG" \
  --tag "$BUILD_TAG" \
  --push \
  --file "$ROOT_DIR/src/FlockCopilot.Api/Dockerfile" \
  "$ROOT_DIR/src/FlockCopilot.Api"

echo ""
echo "[4/5] Updating Azure Container App with new image..."
az containerapp update \
  --name "$CONTAINER_APP_NAME" \
  --resource-group "$RG_NAME" \
  --image "$IMAGE_TAG"

echo ""
echo "[5/5] Fetching deployment information..."
API_URL=$(az deployment group show \
  --resource-group "$RG_NAME" \
  --name "$DEPLOY_NAME" \
  --query properties.outputs.apiUrl.value \
  --output tsv)

OPENAPI_URL=$(az deployment group show \
  --resource-group "$RG_NAME" \
  --name "$DEPLOY_NAME" \
  --query properties.outputs.openapiUrl.value \
  --output tsv)

echo ""
echo "========================================="
echo "✅ Deployment Complete!"
echo "========================================="
echo "API URL: $API_URL"
echo "OpenAPI Spec: $OPENAPI_URL"
echo "Health Check: ${API_URL}/health"
echo ""
echo "Test endpoints:"
echo "  curl ${API_URL}/api/flocks/flock-a/performance"
echo "  curl ${API_URL}/api/flocks/flock-a/history?window=7d"
echo ""
echo "Next steps:"
echo "1. Verify health check: curl ${API_URL}/health"
echo "2. View Swagger UI: ${API_URL}/swagger"
echo "3. Configure Azure AI Foundry agents with: $OPENAPI_URL"
echo "========================================="
