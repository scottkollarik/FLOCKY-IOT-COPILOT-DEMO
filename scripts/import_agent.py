#!/usr/bin/env python3
"""
Utility script to import an Azure AI Foundry agent definition from JSON.

Usage:
    python scripts/import_agent.py \
        --endpoint https://<your-project-id>.cognitiveservices.azure.com \
        --definition agents/diagnostic_agent.json

Environment:
    - Relies on DefaultAzureCredential, so make sure `az login` (or other
      supported auth) is available in the shell that runs this script.
    - Requires the `azure-ai-projects` package (`pip install azure-ai-projects`).
"""

import argparse
import asyncio
import json
from pathlib import Path
from typing import Any, Dict

from azure.ai.projects import AIProjectClient
from azure.identity import DefaultAzureCredential


async def import_agent(
    endpoint: str,
    definition_path: Path,
    agent_name: str | None,
    model: str,
) -> Dict[str, Any]:
    credential = DefaultAzureCredential()
    project_client = AIProjectClient(endpoint=endpoint, credential=credential)

    with definition_path.open("r", encoding="utf-8") as handle:
        definition = json.load(handle)

    target_name = agent_name or definition.get("name")
    if not target_name:
        raise ValueError("Agent name missing. Supply --agent-name or ensure JSON includes a 'name' field.")

    definition["name"] = target_name
    definition["model"] = model

    if isinstance(definition.get("instructions"), list):
        definition["instructions"] = "\n".join(definition["instructions"])

    # Portal memory settings aren't supported in the create_agent payload yet.
    definition.pop("memory", None)

    result = await project_client.agents.create_agent(body=definition)
    return result


async def main() -> None:
    parser = argparse.ArgumentParser(description="Import Azure AI Foundry agent from JSON.")
    parser.add_argument("--endpoint", required=True, help="Project endpoint (from Azure AI Foundry portal).")
    parser.add_argument("--definition", required=True, help="Path to the agent JSON definition.")
    parser.add_argument("--agent-name", help="Optional override for the agent name.")
    parser.add_argument("--model", required=True, help="Model deployment name (e.g., gpt-4o-mini).")
    args = parser.parse_args()

    definition_path = Path(args.definition).expanduser().resolve()
    if not definition_path.exists():
        raise FileNotFoundError(f"Definition file not found: {definition_path}")

    result = await import_agent(args.endpoint, definition_path, args.agent_name, args.model)
    print("✅ Agent imported:")
    print(f"  Name:        {result.get('name')}")
    print(f"  Version ID:  {result.get('id')}")
    print(f"  Status:      {result.get('status')}")


if __name__ == "__main__":
    asyncio.run(main())
