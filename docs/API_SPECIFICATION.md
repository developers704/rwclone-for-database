# Valliani Data Sync — API Specification

This document describes the REST API **your remote server** should implement. The desktop client talks only to this API.

Base URL example: `https://api.yourdomain.com`

All endpoints require:

| Header | Required | Description |
|--------|----------|-------------|
| `X-Api-Key` | Yes | Client API key |
| `X-Client-Id` | No | Optional client identifier |
| `X-Store-Id` | No | Default store scope |
| `Idempotency-Key` | Recommended on POST sync/upload | Prevents duplicate submissions |
| `Content-Type` | Yes on JSON bodies | `application/json` |

HTTPS is mandatory.

---

## POST `/api/v1/auth/validate`

Validates the API key.

**Response 200**
```json
{ "success": true, "message": "ok" }
```

**Response 401**
```json
{ "success": false, "message": "invalid api key" }
```

---

## GET `/api/v1/stores`

Returns stores available to this API key.

**Response 200**
```json
[
  { "id": "1", "code": "STORE01", "name": "Main Store", "isActive": true }
]
```

---

## POST `/api/v1/products/sync`

Upserts product master data.

**Request**
```json
{
  "items": [
    {
      "storeCode": "STORE01",
      "sku": "ABC123",
      "itemNumber": "IT-001",
      "name": "Product Name",
      "description": "Description",
      "brand": "Brand",
      "category": "Category",
      "vendor": "Vendor",
      "cost": 100.00,
      "price": 299.00,
      "salePrice": 249.00,
      "quantity": 5,
      "barcode": "123456789",
      "metal": "Gold",
      "stone": "Diamond",
      "shape": "Round",
      "weight": 2.5,
      "imageUrls": [],
      "contentHash": "A1B2..."
    }
  ]
}
```

**Response 200**
```json
{
  "success": true,
  "message": "ok",
  "uploaded": 10,
  "updated": 5,
  "failed": 0,
  "errors": []
}
```

Identity key: `(storeCode, sku)`. Prefer `sku`; `itemNumber` is secondary.

---

## POST `/api/v1/inventory/sync`

Same item shape; server may update quantity/stock fields only.

---

## POST `/api/v1/prices/sync`

Same item shape; server may update cost/price/salePrice only.

---

## POST `/api/v1/images/upload`

`multipart/form-data`:

| Part | Type | Description |
|------|------|-------------|
| `storeCode` | text | Store code |
| `sku` | text | Product SKU |
| `contentHash` | text | SHA-256 hex of file bytes |
| `file` | file | Original-resolution image |

**Response 200**
```json
{ "url": "https://cdn.yourdomain.com/STORE01/ABC123.jpg", "message": "uploaded" }
```

If `contentHash` matches an existing object, return the existing URL (no duplicate storage).

---

## POST `/api/v1/sync/complete`

Marks a client sync run finished.

```json
{
  "runId": "uuid",
  "storeCode": "STORE01",
  "uploaded": 10,
  "updated": 5,
  "failed": 0,
  "completedAtUtc": "2026-09-26T01:00:00Z"
}
```

---

## GET `/api/v1/sync/status?runId={uuid}`

```json
{
  "runId": "uuid",
  "status": "completed",
  "lastUpdatedUtc": "2026-09-26T01:00:00Z"
}
```

---

## Error model

```json
{ "success": false, "message": "human readable", "errors": ["detail1"] }
```

Use HTTP 4xx for client issues, 5xx for server faults (client retries with exponential backoff).

---

## Idempotency

Clients send `Idempotency-Key` such as `{store}:{sku}:{contentHash}` for product batches and images. Replaying the same key within 24h must not create duplicate product revisions.
