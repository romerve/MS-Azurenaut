# Fictional inventory MCP server

A standalone Python Azure Functions project exposing exactly three read-only MCP
tools. `data/products.json` is the mock database: 100 fictional products, IDs
`P001` through `P100`, across ten categories. All prices are in USD. There is no
database connection, AI model, or dependency on other projects in this repository.

## Tools

| Tool | Arguments | Result |
| --- | --- | --- |
| `search_products` | Optional `name`, `category`, `min_price`, `max_price`, `in_stock_only` (default false), `limit` (default 20, maximum 100), `offset` (default 0) | Products, total matches, returned count, next offset |
| `get_product` | Required `product_id`, such as `P003` | Found flag and product, or explicit not-found result |
| `summarize_inventory` | Optional `category`, `low_stock_threshold` (default 5) | Overall and per-category counts, units, inventory value, low-stock and out-of-stock counts |

Names use substring matching; categories use exact matching. Both ignore case.
Price bounds are inclusive. Filters combine with AND. Results are ordered by ID.
Low stock means quantity **less than or equal to** the threshold, including zero.
Unknown categories return empty results, not the unfiltered inventory.
Malformed inputs fail the tool call rather than returning misleading results.
Values are calculated using decimal arithmetic before JSON serialization.

## Run locally

Prerequisites: Python 3.11-3.13 (3.13 recommended), Azure Functions Core Tools v4
(4.0.7030 or later), and Azurite. No Azure subscription is required locally.

Run these commands from this directory:

```bash
python3.13 -m venv .venv
source .venv/bin/activate
python -m pip install -r requirements.txt
cp local.settings.example.json local.settings.json
```

Start Azurite in one terminal:

```bash
azurite --location .azurite --silent
```

Then, with the virtual environment activated, start Functions in another:

```bash
func start
```

The first start downloads the Functions extension bundle and needs internet
access. This project uses the Preview extension bundle from Microsoft's Python
MCP sample; the installed stable bundle did not contain the MCP trigger.
Azurite provides host storage for Functions, **not the product database**.
The mock inventory remains the bundled, read-only JSON file.

### Connect an MCP client

Use Streamable HTTP at:

```text
http://localhost:7071/runtime/webhooks/mcp
```

Open this directory in VS Code to use the included `.vscode/mcp.json`, or register
that URL in another MCP client's configuration.

Try asking:

- "Find accessories under $20 that are in stock."
- "Tell me about product P003."
- "Summarize inventory by category and flag stock quantities of five or fewer."

Example tool arguments:

```json
{"category": "accessories", "max_price": 20, "in_stock_only": true}
```

```json
{"product_id": "P003"}
```

```json
{"low_stock_threshold": 5}
```

Search returns up to 20 products by default. Pass the returned `next_offset` as
`offset` to retrieve subsequent pages, preserving all other filters.

## Verification

```bash
python -m unittest discover -s tests -v
python tests/smoke_mcp.py
```

Unit tests cover the dataset, filtering, validation, pagination, monetary totals,
and function bindings. The smoke check requires Functions and Azurite to be
running; it exercises MCP initialization, tool discovery, all three tools,
pagination, and invalid inputs over HTTP.

## Security and deployment

Nothing is provisioned or deployed by this project. Core Tools disables key
authorization locally by default; do not expose the local server publicly.
The committed host configuration retains `System` authorization. When deployed,
the MCP endpoint requires the `mcp_extension` **system key** in the
`x-functions-key` header. Store it in the client's secret store, not in source.
For production, consider Microsoft Entra authentication and per-user
authorization instead of relying only on a shared key.

Never commit `local.settings.json`. The JSON dataset is intentionally included
in deployment packages; tests, the virtual environment, and local settings are not.

References: [MCP extension](https://learn.microsoft.com/azure/azure-functions/functions-bindings-mcp)
and [Python tool triggers](https://learn.microsoft.com/azure/azure-functions/functions-bindings-mcp-tool-trigger?pivots=programming-language-python).
