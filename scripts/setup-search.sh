#!/usr/bin/env bash
set -euo pipefail

PREFIX=${1:-flockfoundry}
REGION=${2:-eastus}
RG_NAME=${3:-rg-${PREFIX}}

info() {
  printf '\n➡️  %s\n' "$1"
}

fail() {
  printf '\n❌ %s\n' "$1" >&2
  exit 1
}

command -v az >/dev/null 2>&1 || fail "Azure CLI is required."

SEARCH_SERVICE=$(az resource list -g "$RG_NAME" --resource-type Microsoft.Search/searchServices --query "[0].name" -o tsv)
if [ -z "$SEARCH_SERVICE" ]; then
  fail "No Azure AI Search service found in $RG_NAME. Run ./iac/deploy.sh first."
fi

STORAGE_ACCOUNT=$(az storage account list -g "$RG_NAME" --query "[0].name" -o tsv)
if [ -z "$STORAGE_ACCOUNT" ]; then
  fail "No storage account found in $RG_NAME."
fi

KNOWLEDGE_CONTAINER=${KNOWLEDGE_CONTAINER:-flock-knowledge-base}
DATA_SOURCE=${DATA_SOURCE:-flockcopilot-docs}
INDEX_NAME=${INDEX_NAME:-flockcopilot-knowledge}
INDEXER_NAME=${INDEXER_NAME:-flockcopilot-indexer}
SEARCH_VERSION=${SEARCH_VERSION:-2023-11-01}

info "Fetching Search admin key..."
SEARCH_KEY=$(az search admin-key show -g "$RG_NAME" --service-name "$SEARCH_SERVICE" --query "primaryKey" -o tsv)
[ -n "$SEARCH_KEY" ] || fail "Unable to retrieve Search admin key."

info "Building storage connection string..."
CONNECTION_STRING=$(az storage account show-connection-string -n "$STORAGE_ACCOUNT" -g "$RG_NAME" --query "connectionString" -o tsv)
[ -n "$CONNECTION_STRING" ] || fail "Unable to retrieve storage connection string."

ENDPOINT="https://$SEARCH_SERVICE.search.windows.net"

call_search() {
  local method=$1
  local path=$2
  local body=$3
  curl -sSf -X "$method" \
    -H "Content-Type: application/json" \
    -H "api-key: $SEARCH_KEY" \
    --data "$body" \
    "$ENDPOINT$path?api-version=$SEARCH_VERSION" >/dev/null
}

info "Creating/Updating data source $DATA_SOURCE..."
DATA_SOURCE_BODY=$(cat <<JSON
{
  "name": "$DATA_SOURCE",
  "type": "azureblob",
  "credentials": {
    "connectionString": "$CONNECTION_STRING"
  },
  "container": {
    "name": "$KNOWLEDGE_CONTAINER"
  }
}
JSON
)
call_search PUT "/datasources/$DATA_SOURCE" "$DATA_SOURCE_BODY"

info "Creating/Updating index $INDEX_NAME..."
INDEX_BODY=$(cat <<'JSON'
{
  "name": "__INDEX_NAME__",
  "fields": [
    {"name": "id", "type": "Edm.String", "key": true, "filterable": true, "sortable": true},
    {"name": "title", "type": "Edm.String", "searchable": true, "retrievable": true},
    {"name": "content", "type": "Edm.String", "searchable": true, "retrievable": true, "analyzer": "en.microsoft"},
    {"name": "source", "type": "Edm.String", "filterable": true, "retrievable": true},
    {"name": "metadata_storage_path", "type": "Edm.String", "filterable": true, "retrievable": true},
    {"name": "metadata_storage_name", "type": "Edm.String", "filterable": true, "retrievable": true},
    {"name": "metadata_content_type", "type": "Edm.String", "filterable": true, "retrievable": true},
    {"name": "lastUpdated", "type": "Edm.DateTimeOffset", "filterable": true, "sortable": true, "retrievable": true}
  ],
  "suggesters": [
    {"name": "sg", "sourceFields": ["title", "content"]}
  ],
  "semantic": {
    "configurations": [
      {
        "name": "default",
        "prioritizedFields": {
          "titleField": {"fieldName": "title"},
          "prioritizedContentFields": [{"fieldName": "content"}]
        }
      }
    ]
  }
}
JSON
)
INDEX_BODY=${INDEX_BODY/__INDEX_NAME__/$INDEX_NAME}
call_search PUT "/indexes/$INDEX_NAME" "$INDEX_BODY"

info "Creating/Updating indexer $INDEXER_NAME..."
INDEXER_BODY=$(cat <<JSON
{
  "name": "$INDEXER_NAME",
  "dataSourceName": "$DATA_SOURCE",
  "targetIndexName": "$INDEX_NAME",
  "schedule": {"interval": "PT30M"},
  "parameters": {
    "maxFailedItems": 10,
    "configuration": {
      "dataToExtract": "contentAndMetadata",
      "parsingMode": "default",
      "failOnUnsupportedContentType": false,
      "failOnUnprocessableDocument": false
    }
  },
  "fieldMappings": [
    {"sourceFieldName": "metadata_storage_name", "targetFieldName": "title"},
    {"sourceFieldName": "metadata_storage_path", "targetFieldName": "source"},
    {"sourceFieldName": "metadata_storage_last_modified", "targetFieldName": "lastUpdated"}
  ]
}
JSON
)
call_search PUT "/indexers/$INDEXER_NAME" "$INDEXER_BODY"

info "Running indexer $INDEXER_NAME..."
curl -sSf -X POST -H "api-key: $SEARCH_KEY" -d '' "$ENDPOINT/indexers/$INDEXER_NAME/run?api-version=$SEARCH_VERSION" >/dev/null || true

printf '\n✅ Azure AI Search data source, index, and indexer are configured.\n'
