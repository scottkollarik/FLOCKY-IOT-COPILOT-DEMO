# FlockCopilot – Architecture Overview

FlockCopilot is a demo-ready reference implementation for IoT-style telemetry ingestion, anomaly detection, and “best practices” retrieval (RAG) using Azure.

## Data Flow (high-level)

1. **IoT simulator** emits periodic “gateway snapshots” (batched `sensors[]` per building) and posts them to the API.
2. **API (Container Apps / ASP.NET Core)** persists:
   - **Raw snapshots** to Cosmos DB container `raw_telemetry` (TTL-enabled for bounded demo cost).
   - **Normalized rollups** to Cosmos DB container `normalized` (fast query surface for chat + dashboards).
3. **Chat orchestrator** (`POST /api/chat`) grounds answers in telemetry; if the user asks for “how-to” guidance or anomalies are detected, it retrieves best-practice snippets from **Azure AI Search** and cites the sources.

## Storage Model

- **Cosmos DB database:** `flockdata`
- **Containers:**
  - `normalized`: curated rollups (tenant-scoped, time-series friendly)
  - `raw_telemetry`: full-fidelity snapshots for traceability and future analytics (Fabric/Databricks)

## Multi-tenant Model

- Requests include `X-Tenant-Id-Claim` (simulating a claim). Cosmos partition key is `/tenantId`.

## Key Endpoints

- `POST /api/telemetry/ingest` (ingest gateway snapshot)
- `GET /api/flocks/{flockId}/performance` (latest rollup)
- `GET /api/flocks/{flockId}/history?window=...` (windowed rollups)
- `GET /api/flocks/{flockId}/telemetry/latest` (latest raw snapshot)
- `POST /api/chat` (orchestrated chat, server-side OpenAI + Search)
