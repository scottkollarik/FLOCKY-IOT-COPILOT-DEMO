# FlockCopilot (Demo)

Simulated IoT telemetry + anomaly detection + best-practices retrieval (RAG), delivered as a containerized ASP.NET Core API on Azure Container Apps with a small React chat UI.

## What’s in here
- **IoT simulator**: generates zone-based telemetry (and anomalies) and posts “gateway snapshots” to the API.
- **API**: ingests telemetry, stores **raw** + **normalized** data in Cosmos DB, exposes query endpoints, and provides a server-side chat orchestrator (`POST /api/chat`).
- **Best practices (RAG)**: PDFs in Blob Storage indexed by Azure AI Search; the API retrieves snippets and returns clickable download links.
- **Multi-tenant simulation**: set `X-Tenant-Id-Claim` header (partitioned by `/tenantId` in Cosmos).

Architecture details: `docs/architecture_overview.md`.

## Quick start (local)

### 1) Run the API
```bash
dotnet run --project src/FlockCopilot.Api/FlockCopilot.Api.csproj
```

### 2) Run the chat UI
```bash
cd flockcopilot-chat-ui
npm install
npm start
```

In the UI Settings, set API URL to your local API base URL.

### 3) Run the IoT simulator
```bash
dotnet run --project src/FlockCopilot.IoTSimulator/FlockCopilot.IoTSimulator.csproj
```

Point it at your API base URL and choose an anomaly scenario.

## Deploy to Azure (IaC)

### 1) Provision infrastructure
```bash
./iac/deploy.sh flockfoundry eastus rg-flockfoundry
```

Optional: pass Azure OpenAI settings at deploy time:
```bash
AZURE_OPENAI_ENDPOINT="https://<your-resource>.openai.azure.com" \
AZURE_OPENAI_DEPLOYMENT="<your-deployment-name>" \
AZURE_OPENAI_API_KEY="<your-key>" \
./iac/deploy.sh flockfoundry eastus rg-flockfoundry
```

### 2) Build + deploy the API container image
```bash
./scripts/build-and-push.sh flockfoundry rg-flockfoundry
```

### 3) Create the Search index/data source/indexer
```bash
./scripts/setup-search.sh flockfoundry eastus rg-flockfoundry
```

### 4) Upload your best-practices docs
Upload PDFs to the storage container `flock-knowledge-base`, then re-run the indexer (the setup script triggers a run).

## Key endpoints
- `POST /api/telemetry/ingest`
- `GET /api/flocks/{flockId}/performance`
- `GET /api/flocks/{flockId}/history?window=...`
- `GET /api/flocks/{flockId}/telemetry/latest`
- `POST /api/chat` (auto tool routing + RAG + server-side OpenAI)

## Next steps (post-demo)
- Move OpenAI/Search secrets to Key Vault (avoid plain env vars where possible).
- Add session memory + clarify-before-guess behavior to make the orchestrator more “agentic”.
- Evolve ingestion to IoT Hub/Event Hubs for buffering, retries, and high-volume scale.
