# LLM interaction logger (Azure Functions)

An HTTP-triggered Azure Function (.NET 10, isolated worker) that captures **LLM interactions**: the full
prompt, the full response, token usage and latency, and every attachment sent to or returned by the model
(images, audio, video, PDFs, other files).

- **Binary content** goes to **Azure Blob Storage**.
- **Everything else** (prompt/response text, usage, latency, attachment manifest, metrics) goes to
  **Application Insights** through OpenTelemetry.

```mermaid
flowchart LR
    Client[App / LLM gateway] -- "POST /api/llm/interactions<br/>JSON or multipart" --> Func[CaptureLlmInteraction]
    APIM[API Management policy] -- "POST /api/llm/interactions/request|response<br/>raw provider body + x-llm-* headers" --> Func
    Func -- "images · audio · video · files" --> Blob[(Blob Storage<br/>llm-attachments)]
    Func -- "customEvents · traces · customMetrics" --> AI[(Application Insights)]
```

## How it works

1. The caller posts one interaction per request, after the LLM call completes.
2. Inline binary content is found anywhere in the JSON (request, response or `attachments`) and
   uploaded to blob storage, then replaced in place with a reference string such as
   `llm-attachment://att-001`. It finds:
   - `data:<mime>;base64,...` URIs, for example OpenAI `image_url.url` and `file.file_data`.
   - Base64 fields such as `data`, `b64_json` and `file_data`, including OpenAI `input_audio`,
     Anthropic `source: {type: base64}`, Gemini `inlineData`, and generated images or audio in responses.
     It uses sibling `mimeType`, `media_type`, `contentType` or `format` hints when present and checks
     magic bytes when they are not.
   - Remote URLs (`https://...`) are **not** fetched; they're logged as-is. This avoids SSRF.
3. Multipart file parts are **streamed** straight to blob storage (never buffered in memory), with
   SHA-256 and a size limit enforced while streaming.
4. The sanitized prompt and response (with binaries replaced by references) are written to
   Application Insights **in full**, split into ordered chunks.
5. If the capture fails, any blobs already written are deleted.

## API

Two endpoints, both requiring a function key (`x-functions-key` header or `?code=`):

- `POST /api/llm/interactions`: one complete interaction per call (options A and B).
- `POST /api/llm/interactions/{request|response}`: one half of an interaction per call, as the raw provider
  body (option C). This is the shape an API Management inbound/outbound policy produces.

### Option A: `application/json`

```jsonc
{
  "interactionId": "optional client id",      // falls back to response.id
  "conversationId": "optional",               // or sessionId / threadId
  "userId": "optional",                       // falls back to request.user
  "application": "optional",
  "provider": "azure-openai",
  "operation": "chat.completions",
  "startedAt": "2026-09-29T10:00:00Z",        // optional
  "completedAt": "2026-09-29T10:00:01.2Z",    // optional; latency is derived if latencyMs is missing
  "latencyMs": 1200,
  "status": "success",                        // defaults to "error" when "error" is present
  "request":  { /* raw provider request, e.g. { model, messages: [...] } */ },
  "response": { /* raw provider response */ },
  "error":    { /* optional */ },
  "usage":    { "inputTokens": 900, "outputTokens": 12 }, // optional; read from response.usage / usageMetadata otherwise
  "attachments": [                            // optional extra files not embedded in request/response
    { "name": "clip.mp4", "contentType": "video/mp4", "data": "<base64>" },
    { "name": "remote.png", "url": "https://example.com/remote.png" }
  ],
  "metadata": { "team": "search" }            // optional; emitted as llm.metadata.* dimensions
}
```

At least one of `request` or `response` is required.

### Option B: `multipart/form-data` (recommended for large video/audio)

- One field named `interaction` holds the JSON envelope above.
- Any number of file parts hold the binaries, streamed to blob storage.

```bash
curl -X POST "$ENDPOINT?code=$KEY" \
  -F 'interaction={"interactionId":"demo-2","request":{"messages":[{"role":"user","content":"Summarize the video"}]},"response":{"output_text":"A cat."}};type=application/json' \
  -F 'files=@clip.mp4;type=video/mp4'
```

### Option C: raw provider body, one phase per call (API Management)

`POST /api/llm/interactions/request` with the body the client sent to the LLM, and
`POST /api/llm/interactions/response` with the body the LLM returned. Nothing needs to be rewritten; the
interaction is described with headers:

| Header | Meaning |
| --- | --- |
| `x-llm-interaction-id` | **Correlates the two phases.** Use the same value for both (APIM: `context.RequestId`). |
| `x-llm-provider`, `x-llm-model`, `x-llm-operation`, `x-llm-application`, `x-llm-user-id`, `x-llm-conversation-id` | Interaction details. `x-llm-model` fills in the Azure OpenAI deployment (it's in the URL, not the body); a `model` in the body takes precedence for responses. |
| `x-llm-status-code` | Backend HTTP status (response phase). `>= 400` logs the body as `error` with status `error`. Also stored as `llm.metadata.http_status_code`. |
| `x-llm-latency-ms` | Latency in milliseconds (APIM: `context.Elapsed`). |
| `x-llm-body-encoding: base64` | The body is base64 text; it's decoded first. Lets APIM forward binary bodies safely. |
| `x-llm-metadata-{name}` | Any number of custom dimensions, logged as `llm.metadata.{name}`. |

How the body is handled, by `Content-Type`:

| Body | Captured as |
| --- | --- |
| `application/json` / `*+json` | The JSON, with inline base64 and data URIs moved to blob storage as in option A. |
| `text/event-stream` (streaming) | Parsed into `{ "stream": true, "id", "model", "output_text", "finish_reason", "usage", "events": [...] }`. Every event is kept; text deltas, usage and finish reasons are aggregated for OpenAI/Azure OpenAI chat and Responses API, Anthropic and Gemini streams. |
| `multipart/form-data` (e.g. audio transcription uploads) | Files are streamed to blob storage; the form fields (plus file references) are logged as the prompt. |
| Other `text/*`, XML, form-urlencoded | Logged as a string. |
| Anything else (e.g. `audio/mpeg` from text-to-speech, images) | The whole body is stored as one blob attachment and logged as `{ "body": "llm-attachment://att-001", ... }`. |

### Response: `201 Created`

```json
{
  "captureId": "01a0ee1f0d3875b6927f960f98b1f4c7",
  "interactionId": "demo-1",
  "attachments": [
    {
      "id": "att-001",
      "source": "InlineJson",
      "location": "$.request.messages[0].content[1].image_url.url",
      "contentType": "image/png",
      "sizeBytes": 68,
      "sha256": "63ef31...",
      "container": "llm-attachments",
      "blobName": "2026/09/29/01a0ee1f.../att-001.png",
      "blobUri": "https://<account>.blob.core.windows.net/llm-attachments/2026/09/29/01a0ee1f.../att-001.png",
      "reference": "llm-attachment://att-001"
    }
  ],
  "contentChunks": { "prompt": 1, "response": 1 }
}
```

Errors return `application/problem+json`: `400` (invalid payload), `413` (limit exceeded),
`415` (unsupported content type).

## Where the data goes

| Data | Destination |
| --- | --- |
| Binary attachments | Blob `llm-attachments/{yyyy}/{MM}/{dd}/{captureId}/{attachmentId}[-{fileName}]`. Blob metadata includes `capture_id`, `attachment_id`, `source`, `location` and `sha256`. |
| One summary per capture | `customEvents` named `LlmInteraction`: `llm.phase` (`complete`, `request` or `response`), model, provider, status, finish reasons, `gen_ai.usage.*` tokens, latency, character and chunk counts, attachment ids and types, `llm.metadata.*` |
| One record per attachment | `customEvents` named `LlmAttachment`: id, reference, location, content type, size, SHA-256, blob URI |
| Full prompt / response / error / attachments JSON | `traces` with `llm.content.kind` = `prompt`, `response`, `error` or `attachments`, chunked with `llm.content.chunk_index` and `llm.content.chunk_count` |
| Metrics | `customMetrics`: `llm.interactions`, `gen_ai.client.token.usage`, `gen_ai.client.operation.duration`, `llm.attachments`, `llm.attachment.bytes` |
| (Optional) sanitized interaction JSON | Blob `llm-interactions/{yyyy}/{MM}/{dd}/{captureId}/interaction.json` when `LlmLogging__ArchiveInteractionJson=true` |

Every record carries `llm.capture_id`, plus `llm.interaction_id` when one is available, so you can join them.
With option C a single LLM call produces two captures (phases `request` and `response`) that share
`llm.interaction_id`. `llm.interactions` and the token/duration metrics are recorded only for `response` and
`complete` captures, so each call is counted once; a `request` capture has no status until its response arrives.

> **Sampling is disabled on purpose.** Azure Monitor OpenTelemetry Exporter 1.6+ rate-limits traces to
> 5/s by default and drops logs tied to unsampled traces. That would silently lose prompts, so
> `Program.cs` sets `SamplingRatio = 1`, `TracesPerSecond = null` and `EnableTraceBasedLogsSampler = false`.
> Plan your Application Insights ingestion budget (daily cap) accordingly.

### KQL: rebuild a full prompt

```kusto
traces
| where customDimensions["llm.capture_id"] == "<captureId>"
| where customDimensions["llm.content.kind"] == "prompt"
| extend idx = toint(customDimensions["llm.content.chunk_index"])
| order by idx asc
| summarize prompt = strcat_array(make_list(tostring(customDimensions["llm.content"])), "")
```

### KQL: prompt and response for one API Management call

```kusto
traces
| where customDimensions["llm.interaction_id"] == "<APIM request id>"
| extend kind = tostring(customDimensions["llm.content.kind"]), idx = toint(customDimensions["llm.content.chunk_index"])
| order by kind asc, idx asc
| summarize content = strcat_array(make_list(tostring(customDimensions["llm.content"])), "") by kind
```

### KQL: token usage and latency by model

```kusto
customEvents
| where name == "LlmInteraction" and tostring(customDimensions["llm.phase"]) != "request"
| extend model = tostring(customDimensions["gen_ai.request.model"]),
         inTok = tolong(customDimensions["gen_ai.usage.input_tokens"]),
         outTok = tolong(customDimensions["gen_ai.usage.output_tokens"]),
         latency = todouble(customDimensions["llm.latency_ms"])
| summarize calls = count(), inTok = sum(inTok), outTok = sum(outTok), p95LatencyMs = percentile(latency, 95) by model
```

## Using it behind Azure API Management

[`apim/llm-logging-policy.xml`](apim/llm-logging-policy.xml) is a ready-to-adapt policy for an Azure OpenAI
API in APIM:

```mermaid
sequenceDiagram
    participant C as Client
    participant A as API Management
    participant F as Logger function
    participant L as LLM backend
    C->>A: POST chat/completions
    A-)F: inbound: send-one-way-request → /api/llm/interactions/request
    A->>L: forward request (unchanged)
    L-->>A: response
    A-)F: outbound: send-one-way-request → /api/llm/interactions/response
    A-->>C: response (unchanged)
```

- **No added latency or failure coupling.** `send-one-way-request` is fire-and-forget, so the LLM call never
  waits for, or fails because of, the logger. The trade-off is best-effort delivery: if the function is
  down, that capture is lost (APIM doesn't retry).
- **Bodies are read with `preserveContent: true`**, so the original request/response still reaches the
  backend/client untouched.
- **Binary safety.** JSON and text bodies are forwarded as text. Everything else (multipart audio uploads,
  TTS audio, images) is sent as `Convert.ToBase64String(Body.As<byte[]>(...))` with
  `x-llm-body-encoding: base64`, because `Body.As<string>` would corrupt binary data.
- **Correlation.** Both phases use `context.RequestId` as `x-llm-interaction-id`. `on-error` also posts a
  `response` capture, so backend timeouts and gateway failures are recorded against the same id.
- **Setup.** Create named values `llm-logger-base-url` (the function app URL) and `llm-logger-function-key`
  (a function key; mark it secret or reference Key Vault). Apply the policy at API or operation scope. When
  deploying it with Bicep/ARM use `format: 'rawxml'`. Policy expressions may contain unescaped quotes and
  generics, which is valid for APIM but not strict XML.

Limits and caveats:

| Topic | Impact | Mitigation |
| --- | --- | --- |
| **Streaming (`stream: true`)** | Reading the response body in `outbound` buffers the whole SSE stream, so the client gets all tokens at once when the model finishes. | Keep it if full capture matters more than token-by-token UX. Otherwise skip the outbound capture for `text/event-stream` (see the comment in the policy) and use APIM's built-in [LLM logging](https://learn.microsoft.com/azure/api-management/api-management-howto-llm-logs) (`ApiManagementGatewayLlmLog`), which logs streamed prompts/completions without buffering (up to 2 MB each, in 32 KB chunks, no attachment offload). |
| **APIM buffered payload size** | v2 and Consumption tiers can buffer at most **2 MiB** of body in a policy (classic tiers: 500 MiB). Larger bodies (big images/video) can't be read by the policy. | Use a classic tier for large multimodal payloads, or have the app post attachments directly to the function (option B). |
| **Base64 overhead** | Binary bodies grow ~33% on the APIM → function hop and must fit `MaxAttachmentBytes`. | Raise `LlmLogging__MaxAttachmentBytes` if needed. |
| **Consumption tier** | 30-second total request duration and no long-running SSE connections. | Use a classic or v2 tier for LLM traffic. |
| **Order** | The `response` capture can arrive before the `request` capture. | Join on `llm.interaction_id`, never on time. |
| **Auth** | The function key lives in an APIM named value. | Prefer Key Vault-backed named values, restrict the function app's inbound access to APIM, or put Entra (Easy Auth) in front of the function and use `authentication-managed-identity` in the policy. |

## Configuration

| Setting | Default | Description |
| --- | --- | --- |
| `LlmLogging__BlobServiceUri` | | Blob endpoint for managed identity auth (production). |
| `LlmLogging__BlobConnectionString` | | Connection string (local/Azurite). Takes precedence. |
| `LlmLogging__ManagedIdentityClientId` | | User-assigned identity client ID (blob storage and Application Insights). |
| `LlmLogging__AttachmentsContainer` | `llm-attachments` | Container for binaries. |
| `LlmLogging__InteractionsContainer` | `llm-interactions` | Container for optional JSON archive. |
| `LlmLogging__ArchiveInteractionJson` | `false` | Also archive the sanitized JSON to blob storage. |
| `LlmLogging__MaxJsonBytes` | 100 MB | Max JSON envelope size. |
| `LlmLogging__MaxAttachmentBytes` | 100 MB | Max size per attachment. |
| `LlmLogging__MaxAttachments` | 100 | Max attachments per interaction. |
| `LlmLogging__MinInlineBinaryChars` | 256 | Min length for bare base64 fields without a MIME hint. |
| `LlmLogging__ContentChunkSize` | 8000 | Characters per trace chunk (Application Insights caps property values at 8192). |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | | Enables the Azure Monitor exporter. |
| `APPLICATIONINSIGHTS_AUTHENTICATION_STRING` | | `ClientId=...;Authorization=AAD` switches the exporter to Entra ID auth. |

The Functions host also limits HTTP request bodies (about 100 MB by default). Use multipart for large media,
and raise the limits only if your plan allows it.

## Run locally

Prerequisites: .NET 10 SDK, Azure Functions Core Tools v4 and Azurite.

```bash
cd functions/llm-interaction-logger
azurite --silent --location /tmp/azurite &          # blob + queue + table emulator
cp src/LlmInteractionLogger/local.settings.sample.json src/LlmInteractionLogger/local.settings.json
cd src/LlmInteractionLogger && func start --port 7203   # or: dotnet run
```

```bash
curl -X POST http://localhost:7203/api/llm/interactions \
  -H 'Content-Type: application/json' \
  -d '{"request":{"model":"gpt-4o","messages":[{"role":"user","content":"hi"}]},"response":{"choices":[{"message":{"content":"hello"}}]}}'
```

To send telemetry to a real Application Insights resource while running locally, set
`APPLICATIONINSIGHTS_CONNECTION_STRING` in `local.settings.json`.

## Test

```bash
cd functions/llm-interaction-logger
dotnet test
```

## Deploy

`infra/main.bicep` provisions these resources:

- A Flex Consumption (FC1) Function App on .NET 10 with a user-assigned managed identity.
- A host storage account and a separate ZRS **data** storage account with the `llm-attachments` and
  `llm-interactions` containers. Both accounts have shared keys disabled and public access off, and the
  data account has lifecycle tiering and deletion rules.
- A Log Analytics workspace and Application Insights with local auth disabled, so ingestion uses Entra ID.
- The role assignments the identity needs: Storage Blob Data Owner (host), Storage Blob Data Contributor
  (data) and Monitoring Metrics Publisher (Application Insights).

```bash
cd functions/llm-interaction-logger
azd up          # or: az deployment group create -g <rg> -f infra/main.bicep -p environmentName=<name>
```

## Security and privacy

Prompts, responses and attachments often contain PII or confidential data.

- Restrict access to the Application Insights resource and the data storage account (RBAC).
- Set retention to match your policy: `logRetentionInDays`, `attachmentsCoolAfterDays` and `attachmentsDeleteAfterDays`.
- Consider adding redaction before calling this function if your compliance rules require it.
- The endpoint uses function-key auth. For production, put it behind API Management or enable
  App Service Authentication (Easy Auth), and consider private endpoints.
