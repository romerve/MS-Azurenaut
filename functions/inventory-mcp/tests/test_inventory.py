import json
import tempfile
import unittest
from decimal import Decimal
from pathlib import Path

import function_app
from inventory import Inventory

DATA_PATH = Path(__file__).resolve().parents[1] / "data" / "products.json"


class InventoryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.inventory = Inventory(DATA_PATH)
        with DATA_PATH.open(encoding="utf-8") as source:
            cls.records = json.load(source, parse_float=Decimal)

    def test_exactly_100_unique_complete_products(self):
        self.assertEqual(len(self.records), 100)
        self.assertEqual(
            [record["id"] for record in self.records],
            [f"P{i:03}" for i in range(1, 101)],
        )
        self.assertEqual(len({record["name"] for record in self.records}), 100)
        self.assertEqual(len({record["category"] for record in self.records}), 10)
        for record in self.records:
            self.assertEqual(
                set(record),
                {"id", "name", "category", "description", "price", "stock_quantity"},
            )
            self.assertGreater(record["price"], 0)
            self.assertGreaterEqual(record["stock_quantity"], 0)

    def test_combined_filters_and_inclusive_price(self):
        result = self.inventory.search_products(
            name=" STAND ", category=" ACCESSORIES ",
            min_price=18.99, max_price=18.99, in_stock_only=True,
        )
        self.assertEqual([p["id"] for p in result["products"]], ["P003"])
        self.assertEqual(result["currency"], "USD")

    def test_stock_filter_excludes_zero_stock(self):
        result = self.inventory.search_products(in_stock_only=True, limit=100)
        self.assertEqual(result["total_matches"], 90)
        self.assertTrue(all(p["stock_quantity"] > 0 for p in result["products"]))

    def test_all_pages_cover_dataset_once(self):
        ids = []
        offset = 0
        while offset is not None:
            result = self.inventory.search_products(limit=17, offset=offset)
            self.assertLessEqual(result["returned_count"], 17)
            ids.extend(p["id"] for p in result["products"])
            offset = result["next_offset"]
        self.assertEqual(ids, [p["id"] for p in self.records])

    def test_default_page_and_past_end(self):
        result = self.inventory.search_products()
        self.assertEqual(result["returned_count"], 20)
        self.assertEqual(result["next_offset"], 20)
        result = self.inventory.search_products(offset=100)
        self.assertEqual(result["products"], [])
        self.assertEqual(result["total_matches"], 100)
        self.assertIsNone(result["next_offset"])

    def test_unknown_category_is_empty(self):
        self.assertEqual(self.inventory.search_products(category="unknown")["products"], [])
        summary = self.inventory.summarize_inventory(category="unknown")
        self.assertEqual(summary["product_count"], 0)
        self.assertEqual(summary["inventory_value"], 0)
        self.assertEqual(summary["categories"], [])

    def test_lookup_normalizes_id_and_reports_missing(self):
        result = self.inventory.get_product(" p003 ")
        self.assertTrue(result["found"])
        self.assertEqual(result["product"]["name"], "Nova Phone Stand")
        self.assertEqual(result["product"]["price"], 18.99)
        missing = self.inventory.get_product("P999")
        self.assertFalse(missing["found"])
        self.assertIsNone(missing["product"])
        self.assertIn("P999", missing["message"])

    def test_search_returns_defensive_record_copies(self):
        result = self.inventory.get_product("P001")
        result["product"]["name"] = "Modified"
        self.assertEqual(
            self.inventory.get_product("P001")["product"]["name"], "Orbit Cable Organizer"
        )

    def test_summary_uses_all_records_not_default_page(self):
        result = self.inventory.summarize_inventory()
        self.assertEqual(result["product_count"], 100)
        self.assertEqual(result["total_units"], sum(p["stock_quantity"] for p in self.records))
        expected = sum(
            (p["price"] * p["stock_quantity"] for p in self.records), Decimal("0")
        )
        self.assertEqual(Decimal(str(result["inventory_value"])), expected)
        self.assertEqual(result["out_of_stock_count"], 10)
        for field in (
            "product_count", "total_units", "low_stock_count", "out_of_stock_count",
        ):
            self.assertEqual(result[field], sum(group[field] for group in result["categories"]))
        self.assertEqual(
            expected,
            sum(Decimal(str(group["inventory_value"])) for group in result["categories"]),
        )

    def test_low_stock_threshold_is_inclusive_and_includes_zero(self):
        result = self.inventory.summarize_inventory(category="ACCESSORIES")
        self.assertEqual(result["product_count"], 10)
        self.assertEqual(result["low_stock_count"], 3)
        result = self.inventory.summarize_inventory(low_stock_threshold=0)
        self.assertEqual(result["low_stock_count"], 10)

    def test_invalid_search_arguments(self):
        invalid = [
            {"limit": 0}, {"limit": 101}, {"limit": True}, {"limit": 2.5},
            {"offset": -1}, {"offset": False}, {"offset": "2"},
            {"min_price": -1}, {"max_price": "10"}, {"min_price": True},
            {"min_price": float("nan")}, {"max_price": float("inf")},
            {"min_price": 10, "max_price": 5}, {"in_stock_only": "true"},
            {"in_stock_only": 1}, {"name": ""}, {"name": 12}, {"category": " "},
        ]
        for arguments in invalid:
            with self.subTest(arguments=arguments), self.assertRaises(ValueError):
                self.inventory.search_products(**arguments)

    def test_invalid_lookup_and_summary_arguments(self):
        for product_id in ("", " ", None, 3):
            with self.subTest(product_id=product_id), self.assertRaises(ValueError):
                self.inventory.get_product(product_id)
        for threshold in (-1, True, 2.5, "5"):
            with self.subTest(threshold=threshold), self.assertRaises(ValueError):
                self.inventory.summarize_inventory(low_stock_threshold=threshold)

    def test_duplicate_or_malformed_inventory_fails(self):
        record = {
            **self.records[0],
            "price": float(self.records[0]["price"]),
        }
        for records in (
            [record, record], [], {}, [None],
            [{**record, "stock_quantity": -1}], [{**record, "price": 1.234}],
        ):
            with self.subTest(records=records), tempfile.TemporaryDirectory() as folder:
                path = Path(folder) / "products.json"
                path.write_text(json.dumps(records), encoding="utf-8")
                with self.assertRaises(ValueError):
                    Inventory(path)


class FunctionTests(unittest.TestCase):
    def test_exactly_three_mcp_tools_and_required_id(self):
        functions = function_app.app.get_functions()
        self.assertEqual(
            {f.get_function_name() for f in functions},
            {"search_products", "get_product", "summarize_inventory"},
        )
        for function in functions:
            binding = function.get_bindings()[0].get_dict_repr()
            self.assertEqual(binding["type"], "mcpToolTrigger")
            self.assertEqual(binding["toolName"], function.get_function_name())
            properties = json.loads(binding["toolProperties"])
            if function.get_function_name() == "get_product":
                self.assertTrue(properties[0]["isRequired"])

    def test_each_handler_serializes_real_query_result(self):
        cases = [
            (function_app.search_products, {"category": "audio"}, "total_matches", 10),
            (function_app.get_product, {"product_id": "P003"}, "found", True),
            (function_app.summarize_inventory, {}, "product_count", 100),
        ]
        for handler, arguments, field, expected in cases:
            with self.subTest(handler=handler):
                result = json.loads(handler(json.dumps({"arguments": arguments})))
                self.assertEqual(result[field], expected)

    def test_bad_invocations_fail_instead_of_querying_everything(self):
        cases = [
            (function_app.search_products, "not json"),
            (function_app.search_products, "[]"),
            (function_app.search_products, '{"arguments": []}'),
            (function_app.search_products, '{"arguments": {"unexpected": 1}}'),
            (function_app.get_product, '{"arguments": {}}'),
        ]
        for handler, context in cases:
            with self.subTest(context=context), self.assertRaises(ValueError):
                handler(context)


if __name__ == "__main__":
    unittest.main()
