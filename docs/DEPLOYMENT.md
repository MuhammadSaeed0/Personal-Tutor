# Deployment & operations

## Configuration reference
All settings can be supplied via `appsettings.json`, environment variables (`Section__Key`), or a secret store.
**Secrets** (`Jwt:SigningKey`, `Ai:*:ApiKey`, connection-string passwords) must come from environment variables,
user-secrets (development) or a vault — never from committed files.

| Key | Default | Purpose |
|---|---|---|
| `ConnectionStrings:Default` | — | SQL Server connection |
| `Database:MigrateOnStartup` | `true` in Development | apply EF migrations on start |
| `Jwt:SigningKey` | random in Development | HMAC key, 32+ bytes, required in Production |
| `Jwt:AccessTokenMinutes` / `RefreshTokenDays` | 15 / 30 | token lifetimes |
| `Ai:Chat:Provider` | `Anthropic` (`ClaudeCode` in Development) | `ClaudeCode`, `Anthropic`, `OpenAICompatible`, or anything else = disabled |
| `Ai:Chat:Model` | `claude-opus-5-5` (`sonnet` in Development) | chat model id or Claude Code alias (`sonnet`, `opus`) |
| `Ai:Chat:ApiKey` / `BaseUrl` | — | provider credentials / endpoint (BaseUrl required for OpenAI-compatible) |
| `Ai:Chat:Effort` | `medium` | Claude effort for tutoring (`low`/`medium`/`high`) |
| `Ai:Chat:UseServerSideFallback` | `true` | Claude: re-serve declined requests on Anthropic's recommended fallback model |
| `Ai:Chat:CliPath` / `MaxConcurrency` | `claude` / 2 | ClaudeCode: CLI command or full path; parallel CLI processes |
| `Ai:Chat:TimeoutSeconds` | 180 | per-request timeout |
| `Ai:Embeddings:Provider` | `Local` | `Local` (lexical, no key), `Voyage`, `OpenAICompatible` |
| `Ai:Embeddings:Model` / `Dimensions` / `ApiKey` / `BaseUrl` | `voyage-3.5` / 1024 | embedding model settings |
| `Ai:Limits:DailyRequestsPerUser` / `DailyOutputTokensPerUser` | 300 / 600000 | per-student AI quota |
| `Retrieval:TopK` | 8 | passages per tutor answer |
| `Summaries:SingleCallChars` / `BatchChars` / `MaxBatches` | 180000 / 120000 / 12 | map-reduce thresholds |
| `Processing:MaxFileSizeMb` / `MaxPages` | 150 / 2000 | upload limits |
| `Processing:RunWorker` | `true` | run the background worker in this instance |
| `Ocr:Enabled` / `Engine` | true / `Auto` | OCR for scanned pages; `Auto` = Claude vision when an AI provider is configured, else Tesseract; `Ai`; `Tesseract` |
| `Ocr:AiImageHeight` | 2000 | pixel height of the page image sent to Claude |
| `Ocr:TesseractPath` / `Dpi` | `tesseract` / 300 | Tesseract settings |
| `Processing:OcrParallelism` | 2 | scanned pages read at the same time |
| `Processing:MaxImagesPerSource` / `MaxImageMb` | 100 / 25 | photo sources |
| `Processing:MaxVideoMinutes` | 240 | longest YouTube video accepted |
| `Storage:RootPath` | `App_Data/storage` | where PDFs are stored |
| `Email:AppBaseUrl` | `http://localhost:4200` | base for reset links |
| `Cors:AllowedOrigins` | `[]` | only needed when the SPA is served from another origin |
| `RateLimits:*` | see appsettings | per-minute/hour limits |
| `ForwardedHeaders:Enabled` | `false` | trust `X-Forwarded-*` behind a reverse proxy |
| `Swagger:Enabled` | `false` | expose Swagger outside Development |

## AI providers
**Your Claude account (`ClaudeCode`).** For a personal installation: the API runs the
[Claude Code](https://claude.com/claude-code) CLI on the server, signed in to the server owner's Claude account
(Pro/Max; sign-in with Google works). Sign in once with `claude auth login` as the same OS user that runs the API, then
set `Ai:Chat:Provider=ClaudeCode` (already the Development default) and optionally `Ai:Chat:Model` (`sonnet` by
default to save plan usage; `opus` for the strongest answers).

- Each request starts `claude -p` headless with **no tools, no MCP servers, no settings files, hooks, plugins or saved
  sessions**, in an empty working directory, with the prompt on stdin. The model can only return text, so textbook
  content cannot make it read files or run commands.
- `ANTHROPIC_API_KEY` is removed from the CLI's environment so the subscription is used rather than API billing.
- Streaming, structured JSON output (`--json-schema`), effort and conversation history are supported. Usage counts
  against the plan's 5-hour and weekly limits; a reached limit surfaces as `ai_rate_limited`.
- **Personal use only.** The account belongs to whoever runs the server. Anthropic does not allow offering claude.ai
  sign-in or plan limits to other users of a product, so if other students use your deployment, switch to `Anthropic`
  with an API key.

**Anthropic Claude (recommended).** Set `Ai:Chat:ApiKey`. The default model is `claude-opus-5-5` (change with
`Ai:Chat:Model`, e.g. `claude-sonnet-5-5` for lower cost). The integration uses the official `Anthropic` C# SDK with
streaming, effort control, JSON-schema structured outputs (quizzes, flashcards, grading) and the server-side refusal
fallback (`server-side-fallback-2026-07-01`). Sampling parameters are not sent (current models reject them).

**OpenAI-compatible.** `Ai:Chat:Provider=OpenAICompatible`, `Ai:Chat:BaseUrl=https://api.openai.com/v1` (or Ollama
`http://localhost:11434/v1`, LM Studio, vLLM…), `Ai:Chat:Model`, `Ai:Chat:ApiKey` if required. Structured output uses
`response_format: json_schema`; smaller local models may produce more rejected quiz questions.

**Embeddings.** `Local` works offline but only captures word/character overlap. For meaning-based retrieval use
`Voyage` (`voyage-3.5`, 1024 dims) or an OpenAI-compatible embedding model. Vectors are stored per model name, so after
changing the embedding model **reprocess books** (Library → Manage → Reprocess) to rebuild vectors; until then retrieval
falls back to keyword ranking for those books.

**Without any AI key** the platform still works for reading, highlights, notes, flashcards (manual), planning,
analytics and search; AI features return `ai_not_configured` and the UI says so.

Privacy: relevant textbook passages and the student's question are sent to the configured provider. Use a provider/
account whose data-use terms fit your deployment (e.g. no training on API data).

## OCR
**Claude vision (default).** With an AI provider configured (your Claude account or an API key), each scanned page is
rendered to an image and Claude transcribes it verbatim (Arabic, English, French, equations as LaTeX, tables as
Markdown) and describes its figures, which are stored as `[شكل: …]` / `[Figure: …]` lines so the tutor can use them.
Chapters are then detected from the recognised headings. One model request per scanned page: a 250-page scanned book
uses a noticeable share of a Claude plan's limits. If the limit is reached, the remaining pages get an
`ocr_rate_limited` warning; reprocessing later reads only those pages (pages already read are kept).

**Tesseract (offline alternative).** Install Tesseract with `ara`, `eng` and `fra` traineddata and make `tesseract` available on PATH (or set
`Ocr:TesseractPath`). Windows: the UB Mannheim installer; Debian/Ubuntu: `apt-get install tesseract-ocr tesseract-ocr-ara
tesseract-ocr-fra`. `GET /api/system/status` reports `ocr.available`.

## Hosting
Riwaq runs directly on the host, without containers. You need SQL Server (any edition, 2019+), the .NET 10 runtime and,
optionally, Tesseract and Claude Code. Build the SPA (`npx ng build`), publish the API (`dotnet publish -c Release`),
copy `frontend/dist/riwaq-web/browser` into the publish folder's `wwwroot`, and run it as a service (Windows Service,
IIS or systemd) behind a reverse proxy. Back up the database and `Storage:RootPath` (uploaded PDFs).

## Production checklist
- [ ] Serve over **HTTPS** (reverse proxy such as nginx/Caddy/Azure Front Door). Set `ForwardedHeaders:Enabled=true`
      and restrict the proxy network at the infrastructure level. The refresh cookie is `Secure`, so plain HTTP only works
      on `localhost`.
- [ ] Set a strong `Jwt:SigningKey` and a dedicated SQL login with least privilege (db_datareader/db_datawriter + DDL only
      for migrations, or run migrations as a separate step with `Database:MigrateOnStartup=false`).
- [ ] Configure an email sender for password resets (`IEmailSender`; the default logs links in Development only).
- [ ] Persistent storage for `Storage:RootPath` (or implement `IFileStorage` for blob storage).
- [ ] Set AI quotas (`Ai:Limits`) and rate limits for your audience.
- [ ] Run a single worker per database, or keep `Processing:RunWorker=true` on several instances (job claiming is atomic).
- [ ] Monitor `/health/ready`, application logs (structured), and failed processing jobs (`DocumentProcessingJobs.Status = 3`).

## Migrations
```bash
cd backend
dotnet ef migrations add <Name> -p src/Riwaq.Infrastructure -s src/Riwaq.Api -o Persistence/Migrations
dotnet ef database update -p src/Riwaq.Infrastructure -s src/Riwaq.Api
dotnet ef migrations script -p src/Riwaq.Infrastructure -s src/Riwaq.Api --idempotent -o migrate.sql   # for DBAs
```
