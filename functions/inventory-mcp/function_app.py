"""Three MCP tools using the Azure Functions MCP extension."""

import json
import logging
from pathlib import Path

import azure.functions as func

from inventory import Inventory

app = func.FunctionApp()
inventory = Inventory(Path(__file__).parent / "data" / "products.json")


def _property(name: str, kind: str, description: str, required: bool = False) -> dict:
    return {
        "propertyName": name,
        "propertyType": kind,
        "description": description,
        "isRequired": required,
    }


def _invoke(context: str, query, allowed: set[str]) -> str:
    try:
        invocation = json.loads(context)
        if not isinstance(invocation, dict):
            raise ValueError("Tool invocation must be an object.")
        arguments = invocation.get("arguments", {})
        if not isinstance(arguments, dict):
            raise ValueError("Tool arguments must be an object.")
        if arguments.keys() - allowed:
            raise ValueError("Unsupported tool arguments.")
        if query.__name__ == "get_product" and "product_id" not in arguments:
            raise ValueError("product_id is required.")
        return json.dumps(query(**arguments), allow_nan=False)
    except ValueError:
        logging.warning("Invalid arguments for inventory tool %s", query.__name__)
        raise


@app.mcp_tool_trigger(
    arg_name="context",
    tool_name="search_products",
    description=(
        "Search fictional products by name substring, exact category, inclusive USD "
        "price range, and availability. Filters combine with AND and ignore case. "
        "Returns products ordered by ID, total_matches, and next_offset. "
        "Default limit 20; maximum 100. Use next_offset to fetch another page."
    ),
    tool_properties=json.dumps([
        _property("name", "string", "Case-insensitive product name substring."),
        _property("category", "string", "Exact category name, ignoring case."),
        _property("min_price", "number", "Inclusive minimum unit price in USD."),
        _property("max_price", "number", "Inclusive maximum unit price in USD."),
        _property("in_stock_only", "boolean", "Only positive stock; default false."),
        _property("limit", "integer", "Page size from 1 to 100; default 20."),
        _property("offset", "integer", "Number of matching products to skip; default 0."),
    ]),
)
def search_products(context: str) -> str:
    return _invoke(context, inventory.search_products, {
        "name", "category", "min_price", "max_price", "in_stock_only", "limit", "offset",
    })


@app.mcp_tool_trigger(
    arg_name="context",
    tool_name="get_product",
    description=(
        "Get one fictional product by ID, such as P003. Returns found=false and "
        "product=null when the ID does not exist. Prices are in USD."
    ),
    tool_properties=json.dumps([
        _property("product_id", "string", "Product ID such as P003.", required=True),
    ]),
)
def get_product(context: str) -> str:
    return _invoke(context, inventory.get_product, {"product_id"})


@app.mcp_tool_trigger(
    arg_name="context",
    tool_name="summarize_inventory",
    description=(
        "Summarize fictional inventory overall and by category: product count, units, "
        "stock value in USD (unit price times quantity), low-stock and out-of-stock "
        "counts. Low stock includes zero stock and quantities equal to the threshold. "
        "Optionally filter by exact category, ignoring case."
    ),
    tool_properties=json.dumps([
        _property("category", "string", "Optional exact category name, ignoring case."),
        _property(
            "low_stock_threshold", "integer",
            "Non-negative low-stock threshold, inclusive; default 5.",
        ),
    ]),
)
def summarize_inventory(context: str) -> str:
    return _invoke(context, inventory.summarize_inventory, {
        "category", "low_stock_threshold",
    })
