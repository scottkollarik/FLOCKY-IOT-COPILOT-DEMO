#!/usr/bin/env bash
set -euo pipefail

PREFIX=${1:-flockfoundry}
RG_NAME=${2:-rg-${PREFIX}}

# Optional: Clean up APIM configuration (from shared services)
APIM_NAME=${APIM_NAME:-apim-shared}
APIM_RG=${APIM_RG:-rg-shared-services}

if [ "${CLEANUP_APIM:-false}" = "true" ]; then
  echo "➡️ Removing FlockCopilot API from shared APIM..."

  # Check if APIM exists and has FlockCopilot API
  if az apim api show --api-id flockcopilot-api --resource-group "$APIM_RG" --service-name "$APIM_NAME" >/dev/null 2>&1; then
    echo "   Deleting API: flockcopilot-api"
    az apim api delete \
      --api-id flockcopilot-api \
      --resource-group "$APIM_RG" \
      --service-name "$APIM_NAME" \
      --yes
  fi

  # Remove backend
  if az apim backend show --backend-id flockcopilot-backend --resource-group "$APIM_RG" --service-name "$APIM_NAME" >/dev/null 2>&1; then
    echo "   Deleting backend: flockcopilot-backend"
    az apim backend delete \
      --backend-id flockcopilot-backend \
      --resource-group "$APIM_RG" \
      --service-name "$APIM_NAME"
  fi

  echo "✅ FlockCopilot resources removed from APIM (shared APIM instance preserved)"
else
  echo "ℹ️  APIM cleanup skipped (set CLEANUP_APIM=true to remove FlockCopilot from shared APIM)"
fi

echo ""
echo "⚠️  This will delete resource group: $RG_NAME"
echo "    (Shared APIM in $APIM_RG will NOT be touched)"
az group delete --name "$RG_NAME" --yes --no-wait

echo "🧨 Teardown requested. Resources are being deleted asynchronously."
