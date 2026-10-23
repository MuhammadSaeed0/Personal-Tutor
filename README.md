# رِواق — Riwaq Learning

**Every textbook becomes a personal tutor, an interactive classroom, a revision system and an exam-preparation space.**

Riwaq is a full-stack, AI-assisted study platform for Arabic-speaking high-school students. A student adds a PDF
textbook, photos of pages or a YouTube lesson; Riwaq extracts its text page by page, detects chapters, indexes it for search, and builds a study environment
around it: an interactive PDF reader, a tutor whose answers cite the exact pages they rely on, summaries, FSRS
flashcards, validated quizzes and mock exams, an adaptive study plan, focus sessions and learning analytics.
The interface is Arabic-first (RTL) with a full English (LTR) mode.

> رِواق منصة مذاكرة ذكية: ارفع كتابك المدرسي بصيغة PDF، فيصبح معلّمًا خاصًا يجيب من صفحات كتابك نفسها،
> مع بطاقات مراجعة واختبارات وخطة مذاكرة وتحليلات للتقدّم. الواجهة عربية أولًا مع دعم كامل للإنجليزية.

| | |
|---|---|
| Backend | ASP.NET Core 10 (LTS) Web API · EF Core 10 · SQL Server (tested on 2025) · ASP.NET Core Identity + JWT · SignalR · background job worker |
| Frontend | Angular 22 (standalone, zoneless, signals, strict) · Angular CDK · pdf.js 6 · KaTeX + mhchem · Chart.js · Lucide icons |
| AI | Provider abstraction: your own Claude account through Claude Code, Anthropic Claude API (official C# SDK, default `claude-opus-5-5`), any OpenAI-compatible endpoint; embeddings via Voyage, OpenAI-compatible, or a built-in lexical fallback |
| Sources | PDFs (PdfPig extraction with Arabic RTL repair, Claude vision or Tesseract for scanned pages), photos (SkiaSharp + Claude vision), YouTube lessons (captions via YoutubeExplode, tidied and split by topic with Claude) |

## Quick start (local development)

### Prerequisites
- .NET SDK 10.0.x — `dotnet --list-sdks`
- Node.js 24 + npm — the Angular CLI is used through `npx`
- SQL Server 2019+ (Developer/Express/LocalDB) installed locally
- Optional: [Tesseract OCR](https://github.com/tesseract-ocr/tesseract) with `ara`, `eng`, `fra` language packs (for scanned PDFs)
- For the AI features (tutor, summaries, quizzes, flashcard drafts), one of:
  - [Claude Code](https://claude.com/claude-code) signed in to **your Claude account** (Pro/Max, e.g. signed up with Google), or
  - an Anthropic API key

### 1. Database
The development connection string (`backend/src/Riwaq.Api/appsettings.Development.json`) targets a local SQL Server with
Windows authentication:

```
Server=localhost;Database=Riwaq;Trusted_Connection=True;TrustServerCertificate=True
```

Change it if needed (or use user-secrets — see below). The schema is created automatically on start in Development
(`Database:MigrateOnStartup`). To create it manually:

```bash
cd backend
dotnet tool install --global dotnet-ef        # once
dotnet ef database update -p src/Riwaq.Infrastructure -s src/Riwaq.Api
```

With SQL authentication instead of Windows authentication use
`Server=localhost;Database=Riwaq;User Id=<login>;Password=<password>;TrustServerCertificate=True` and keep it in
user-secrets: `dotnet user-secrets set "ConnectionStrings:Default" "..."`.

### 2. AI: use your Claude account (default in Development)
Development is configured with `Ai:Chat:Provider=ClaudeCode`: the API runs the Claude Code CLI on the same machine,
signed in to your own Claude account, so no API key is needed.

```bash
claude auth login      # opens the browser: choose your Claude account (e.g. "Continue with Google")
claude auth status     # should show "loggedIn": true
```
Usage counts against your Claude plan's limits. This mode is for running Riwaq **for yourself**: the account belongs to
whoever runs the server, so a deployment used by other students must use an API key (below). See
[AI providers](docs/DEPLOYMENT.md#ai-providers).

### 3. Secrets (never commit them)
```bash
cd backend/src/Riwaq.Api
dotnet user-secrets init
dotnet user-secrets set "Ai:Chat:Provider" "Anthropic"         # optional: API key instead of your Claude account
dotnet user-secrets set "Ai:Chat:ApiKey" "sk-ant-..."
dotnet user-secrets set "Ai:Embeddings:Provider" "Voyage"       # optional: semantic search
dotnet user-secrets set "Ai:Embeddings:ApiKey" "pa-..."
```
In Development a random JWT signing key is generated at start when none is configured. Outside Development
`Jwt:SigningKey` (32+ chars) is required.

### 4. Run
```bash
# terminal 1 — API on http://localhost:5080 (Swagger at /swagger)
cd backend
dotnet run --project src/Riwaq.Api --launch-profile http

# terminal 2 — web app on http://localhost:4200 (proxies /api and /hubs to the API)
cd frontend
npm install
npm start
```
Open http://localhost:4200, create an account, and upload a PDF.
`GET /api/system/status` shows which AI provider, embedding model and OCR engine are active.

### 5. Tests
```bash
cd backend && dotnet test            # unit + integration (uses a throw-away SQL Server database)
cd frontend && npm run test:ci       # Vitest unit/component tests
cd frontend && npm run check:i18n    # every UI string key exists in Arabic and English
cd e2e && npm install && npm run journey   # browser smoke test against the running app
```
Integration tests create and drop a database named `Riwaq_Test_<guid>`; set `RIWAQ_TEST_SQL` to target another server.
Real-provider tests run only when opted in: `RIWAQ_TEST_CLAUDE_CLI=1` (your Claude account) or
`RIWAQ_TEST_ANTHROPIC_KEY=sk-ant-...` (API).

### Single-process build (optional)
```bash
cd frontend && npx ng build
cd ../backend && dotnet publish src/Riwaq.Api -c Release -o ../publish
# copy frontend/dist/riwaq-web/browser to publish/wwwroot, then run publish/Riwaq.Api.exe
```
The API serves the Angular build from `wwwroot` when present, so one process hosts the whole app.

## Repository layout
```
backend/
  src/Riwaq.Domain          entities and enums
  src/Riwaq.Application     use cases: ingestion pipeline, retrieval, tutor, summaries, flashcards (FSRS),
                            quizzes, planner, analytics; pure algorithms (Text/, Learning/)
  src/Riwaq.Infrastructure  EF Core + migrations, PDF extraction, OCR, storage, AI providers, background worker
  src/Riwaq.Api             controllers, auth, SignalR hub, rate limiting, error handling
  tests/Riwaq.Tests         unit + integration tests (incl. the full student journey)
frontend/                   Angular app (core/, shared/, layout/, features/)
e2e/                        browser smoke test
docs/                       architecture, API, deployment, features & limitations, checklist
```

## Documentation
- [Architecture](docs/ARCHITECTURE.md) — components, data model, ingestion pipeline, RAG, security
- [API](docs/API.md) — endpoints and conventions (live OpenAPI at `/swagger`)
- [Deployment & operations](docs/DEPLOYMENT.md) — configuration reference, AI providers, OCR, hosting, production checklist
- [Features, limitations & roadmap](docs/FEATURES.md)
- [Implementation checklist](docs/IMPLEMENTATION_CHECKLIST.md)
