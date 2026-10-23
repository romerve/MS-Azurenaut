"""Exercise a running local Functions host using the MCP Streamable HTTP transport."""

import itertools
import json
import sys
import urllib.request


def main():
    url = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:7071/runtime/webhooks/mcp"
    ids = itertools.count(1)
    session_id = None
    protocol_version = None

    def rpc(method, params, notification=False):
        nonlocal session_id
        payload = {"jsonrpc": "2.0", "method": method, "params": params}
        if not notification:
            payload["id"] = next(ids)
        headers = {
            "Content-Type": "application/json",
            "Accept": "application/json, text/event-stream",
        }
        if session_id:
            headers["Mcp-Session-Id"] = session_id
        if protocol_version:
            headers["MCP-Protocol-Version"] = protocol_version
        request = urllib.request.Request(
            url, json.dumps(payload).encode("utf-8"), headers, method="POST"
        )
        with urllib.request.urlopen(request, timeout=30) as response:
            session_id = response.headers.get("Mcp-Session-Id", session_id)
            body = response.read().decode("utf-8")
            if notification:
                return None
            if "text/event-stream" in response.headers.get("Content-Type", ""):
                messages = [
                    json.loads(line[5:].strip())
                    for line in body.splitlines() if line.startswith("data:")
                ]
                return next(message for message in messages if message.get("id") == payload["id"])
            return json.loads(body)

    def call(name, arguments):
        response = rpc("tools/call", {"name": name, "arguments": arguments})
        if "error" in response:
            raise AssertionError(response)
        result = response["result"]
        if result.get("isError"):
            raise AssertionError(result)
        return json.loads(next(item["text"] for item in result["content"] if item["type"] == "text"))

    initialized = rpc("initialize", {
        "protocolVersion": "2025-06-18",
        "capabilities": {},
        "clientInfo": {"name": "inventory-smoke-check", "version": "1.0.0"},
    })
    protocol_version = initialized["result"]["protocolVersion"]
    rpc("notifications/initialized", {}, notification=True)
    tools = rpc("tools/list", {})["result"]["tools"]
    assert {tool["name"] for tool in tools} == {
        "search_products", "get_product", "summarize_inventory",
    }, tools
    product_tool = next(tool for tool in tools if tool["name"] == "get_product")
    assert "product_id" in product_tool["inputSchema"]["required"], product_tool

    found = call("get_product", {"product_id": "P003"})
    assert found["found"] and found["product"]["price"] == 18.99, found
    missing = call("get_product", {"product_id": "P999"})
    assert not missing["found"] and missing["product"] is None, missing

    filtered = call("search_products", {
        "category": "accessories", "max_price": 20, "in_stock_only": True,
    })
    assert filtered["total_matches"] == 7, filtered
    ids_seen = []
    offset = 0
    while offset is not None:
        page = call("search_products", {"limit": 40, "offset": offset})
        ids_seen.extend(product["id"] for product in page["products"])
        offset = page["next_offset"]
    assert ids_seen == [f"P{i:03}" for i in range(1, 101)], ids_seen

    summary = call("summarize_inventory", {})
    assert summary["product_count"] == 100 and len(summary["categories"]) == 10, summary
    assert summary["out_of_stock_count"] == 10, summary
    low_stock = call("summarize_inventory", {"category": "accessories"})
    assert low_stock["low_stock_count"] == 3, low_stock
    for name, arguments in [
        ("search_products", {"limit": 0}),
        ("search_products", {"max_price": -1}),
        ("get_product", {}),
        ("summarize_inventory", {"low_stock_threshold": -1}),
    ]:
        invalid = rpc("tools/call", {"name": name, "arguments": arguments})
        assert "error" in invalid or invalid.get("result", {}).get("isError"), invalid
    print("MCP smoke check passed: discovery, all 3 tools, 100 products, pagination, errors.")


if __name__ == "__main__":
    main()
