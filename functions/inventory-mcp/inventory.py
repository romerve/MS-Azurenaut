"""Read-only queries over a bundled fictional inventory."""

import json
from dataclasses import asdict, dataclass
from decimal import Decimal
from pathlib import Path


@dataclass(frozen=True)
class Product:
    id: str
    name: str
    category: str
    description: str
    price: Decimal
    stock_quantity: int

    def to_dict(self) -> dict:
        return {**asdict(self), "price": float(self.price)}


def _text(value: object, name: str) -> str:
    if not isinstance(value, str) or not value.strip():
        raise ValueError(f"{name} must be a non-empty string.")
    return value.strip()


def _integer(value: object, name: str, minimum: int, maximum: int | None = None) -> int:
    if (
        type(value) is not int
        or value < minimum
        or (maximum is not None and value > maximum)
    ):
        bounds = f"{minimum} to {maximum}" if maximum is not None else f"at least {minimum}"
        raise ValueError(f"{name} must be an integer {bounds}.")
    return value


def _price(value: object, name: str) -> Decimal:
    if type(value) not in (int, float, Decimal):
        raise ValueError(f"{name} must be a finite, non-negative number.")
    number = Decimal(str(value))
    if not number.is_finite() or number < 0:
        raise ValueError(f"{name} must be a finite, non-negative number.")
    return number


class Inventory:
    def __init__(self, path: Path):
        with path.open(encoding="utf-8") as source:
            records = json.load(source, parse_float=Decimal)
        if not isinstance(records, list) or not records:
            raise ValueError("Inventory must be a non-empty JSON array.")
        products = []
        ids = set()
        for record in records:
            if not isinstance(record, dict):
                raise ValueError("Each inventory record must be an object.")
            product = Product(**record)
            for field in ("id", "name", "category", "description"):
                _text(getattr(product, field), field)
            price = _price(product.price, "price")
            if price != price.quantize(Decimal("0.01")):
                raise ValueError("Product prices must have at most two decimal places.")
            _integer(product.stock_quantity, "stock_quantity", 0)
            if product.id in ids:
                raise ValueError(f"Duplicate product ID: {product.id}")
            ids.add(product.id)
            products.append(product)
        self._products = tuple(sorted(products, key=lambda product: product.id))

    def search_products(
        self,
        name: str | None = None,
        category: str | None = None,
        min_price: float | None = None,
        max_price: float | None = None,
        in_stock_only: bool = False,
        limit: int = 20,
        offset: int = 0,
    ) -> dict:
        name_filter = _text(name, "name").casefold() if name is not None else None
        category_filter = (
            _text(category, "category").casefold() if category is not None else None
        )
        lower = _price(min_price, "min_price") if min_price is not None else None
        upper = _price(max_price, "max_price") if max_price is not None else None
        if lower is not None and upper is not None and lower > upper:
            raise ValueError("min_price must not exceed max_price.")
        if type(in_stock_only) is not bool:
            raise ValueError("in_stock_only must be a boolean.")
        _integer(limit, "limit", 1, 100)
        _integer(offset, "offset", 0)
        matches = [
            product
            for product in self._products
            if (name_filter is None or name_filter in product.name.casefold())
            and (category_filter is None or category_filter == product.category.casefold())
            and (lower is None or product.price >= lower)
            and (upper is None or product.price <= upper)
            and (not in_stock_only or product.stock_quantity > 0)
        ]
        page = matches[offset : offset + limit]
        return {
            "currency": "USD",
            "products": [product.to_dict() for product in page],
            "total_matches": len(matches),
            "returned_count": len(page),
            "next_offset": offset + limit if offset + limit < len(matches) else None,
        }

    def get_product(self, product_id: str) -> dict:
        product_id = _text(product_id, "product_id").upper()
        product = next((p for p in self._products if p.id == product_id), None)
        return {
            "found": product is not None,
            "currency": "USD",
            "product": product.to_dict() if product else None,
            "message": None if product else f"Product '{product_id}' was not found.",
        }

    def summarize_inventory(
        self, category: str | None = None, low_stock_threshold: int = 5
    ) -> dict:
        category_filter = (
            _text(category, "category").casefold() if category is not None else None
        )
        _integer(low_stock_threshold, "low_stock_threshold", 0)
        selected = [
            p for p in self._products
            if category_filter is None or p.category.casefold() == category_filter
        ]

        def totals(products: list[Product]) -> dict:
            return {
                "product_count": len(products),
                "total_units": sum(p.stock_quantity for p in products),
                "inventory_value": float(
                    sum((p.price * p.stock_quantity for p in products), Decimal("0"))
                ),
                "low_stock_count": sum(
                    p.stock_quantity <= low_stock_threshold for p in products
                ),
                "out_of_stock_count": sum(p.stock_quantity == 0 for p in products),
            }

        return {
            "currency": "USD",
            "low_stock_threshold": low_stock_threshold,
            **totals(selected),
            "categories": [
                {"category": name, **totals([p for p in selected if p.category == name])}
                for name in sorted({p.category for p in selected})
            ],
        }
