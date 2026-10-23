# Architecture

## Overview

```
 Browser (Angular 22, RTL/LTR)                         ASP.NET Core 10 API                         SQL Server
 ┌────────────────────────────┐   REST + SSE    ┌──────────────────────────────────┐   EF Core  ┌──────────────┐
 │ reader (pdf.js) · tutor     │ ─────────────▶ │ Controllers → Application services │ ─────────▶ │ users, books, │
 │ flashcards · quizzes · plan │ ◀───────────── │ Auth (Identity + JWT) · rate limits │ ◀───────── │ pages, chunks,│
 │ analytics · notes · tools   │   SignalR      │ SignalR hub (processing progress)   │            │ vectors, ...  │
 └────────────────────────────┘                 │ Background worker (DB job queue)    │            └──────────────┘
                                                │   PDF extraction · OCR · embeddings │──▶ file storage (PDFs)
                                                │ AI providers: Claude / OpenAI-compat │──▶ LLM + embedding APIs
                                                └──────────────────────────────────┘
```

The backend follows a pragmatic clean architecture:

| Project | Responsibility | Depends on |
|---|---|---|
| `Riwaq.Domain` | Entities and enums only | — |
| `Riwaq.Application` | Use cases (one service per feature), DTOs, interfaces (`IAppDbContext`, `IChatModel`, `IEmbeddingModel`, `IPdfExtractor`, `IOcrEngine`, `IFileStorage`), and pure algorithms (FSRS, grading, chunking, RTL text repair, structure detection, planning) | Domain, EF Core abstractions |
| `Riwaq.Infrastructure` | EF Core context + migrations, PdfPig extraction, Tesseract OCR, local file storage, AI providers (Claude API, your Claude account via the Claude Code CLI, OpenAI-compatible), background worker | Application |
| `Riwaq.Api` | HTTP surface: controllers, JWT auth, problem-details errors, rate limiting, SignalR, Swagger, health checks | Infrastructure |

Services take the authenticated user id explicitly (from the validated token, never from the request body) and every
query filters by it — ownership is enforced in one obvious place per method.

## Data model (main tables)

- **Identity**: `AspNetUsers` (+ `UserPreferences`, `AcademicProfiles` 1:1), `RefreshTokens` (hashed, rotating).
- **Content**: `Subjects` (7 seeded, configurable colour/icon), `Books` (file metadata + `Sha256` for duplicate detection,
  status, `ContentVersion`), `DocumentProcessingJobs`, `DocumentPages` (text per page, extraction method, scanned flag),
  `Chapters` (tree via `ParentId`/`Level`; chapters and sections share one table), `DocumentChunks` (page range, chapter,
  content hash), `ChunkEmbeddings` (model name, dimensions, float32 vector as `varbinary`), `DocumentWarnings`.
- **Reading**: `ReadingProgress` (bitset of pages actually read), `ChapterProgress`, `Bookmarks`, `Highlights`
  (page-relative rectangles + annotation).
- **Learning**: `Conversations`/`ConversationMessages` (citations stored as JSON), `Summaries`, `Notes`, `Flashcards`
  (FSRS state) + `FlashcardReviewLogs`, `Quizzes`/`QuizQuestions` (answer keys never sent before grading),
  `QuizAttempts`/`QuizResponses`, `TopicMastery`.
- **Planning & progress**: `StudyPlans`/`StudyTasks`, `StudySessions` (focus + automatic reading time), `Exams`,
  `Achievements`, `Notifications`, `AiUsage` (per-user daily limits).

Design decisions:
- **PDFs live in file storage** (`IFileStorage`, local disk by default); SQL Server stores metadata, extracted text and
  vectors. Page text is a few KB per page, so keeping it relational keeps search and ownership simple.
- **Cascades are book-centric.** Deleting a book removes pages, chunks, vectors, chapters, warnings, jobs, highlights,
  bookmarks, progress, summaries and conversations; quizzes, flashcards and notes keep their history with the book link
  set to null. User-level deletion is explicit (`AccountService.DeleteAllDataAsync`) to avoid SQL Server multiple
  cascade paths.
- **No soft delete** — deletions are real (privacy), with confirmation in the UI.
- All timestamps are UTC; ids are time-ordered GUIDs (`Guid.CreateVersion7`) to avoid index fragmentation.

## Document ingestion pipeline

A study source is one of three kinds (`Book.Kind`), and each is turned into numbered **pages of text** so that structure,
search, the tutor, summaries, cards and quizzes work the same for all of them:

| Kind | Created by | A "page" is | Text comes from |
|---|---|---|---|
| `Pdf` | `POST /api/books/upload` | a PDF page | embedded text; Claude vision (or Tesseract) for scanned pages |
| `Images` | `POST /api/books/images` | one photo (normalised: EXIF orientation, ≤ 2400 px, JPEG) | Claude vision: verbatim text + figure descriptions |
| `YouTube` | `POST /api/books/youtube` | a ~2-minute transcript segment with start/end seconds | the video's captions (YoutubeExplode), punctuated and tidied by Claude, which also names the topics (→ sections) |

Recognised pages and cleaned transcript segments are saved as soon as they are produced (`RecognizedPages`,
`*.transcript.json`), so an interrupted or repeated run never pays for the same page twice. Video passages reach the
tutor with a `time` attribute so it can say "at 12:30", and the reader shows page references of videos as times.

The PDF path in detail:

Upload (`POST /api/books/upload`) streams the file to a temp file while hashing and enforcing the size limit, checks the
`%PDF-` header, opens it with PdfPig (rejects encrypted/corrupt files), rejects duplicates per student, stores the file
and queues a `DocumentProcessingJob`. The background worker then runs:

1. **Validate** — page count limits, parseability.
2. **Extract text** — PdfPig words → Docstrum blocks → reading order. Arabic repair: right-to-left word order, per-word
   reversal of visually-ordered glyph runs, presentation forms → normal letters (without NFKC-destroying `m²`/`H₂O`),
   detached diacritics dropped, mirrored brackets fixed, embedded Latin/formula runs restored to LTR order
   (`RtlLineAssembler`). Right-to-left multi-column pages are ordered right-to-left.
3. **OCR** scanned pages (fewer than 25 letters) with Tesseract (`ara+eng`, `fra+eng` or `eng` by subject). Without
   Tesseract each scanned page gets a `scanned_page_no_ocr` warning instead of being silently skipped.
4. **Detect structure** — PDF outline (bookmarks) first; otherwise headings by font size/weight and chapter keywords
   (الفصل/الوحدة/الدرس, Chapter/Unit/Lesson, Chapitre/Leçon), ignoring running headers. Nothing is invented when evidence
   is weak (`no_structure_detected`). Corrected chapter titles survive reprocessing.
5. **Tables & figures** — flags pages with figures and table-like layouts and tells the student what is not interpreted.
6. **Chunk + embed** — paragraph-aware ~1200-character chunks that never cross chapters and keep exact page ranges;
   vectors are reused by content hash when reprocessing.

Jobs are claimed atomically (`UPDATE … WHERE Status = Queued`), heartbeat while running, are re-queued on start if their
heartbeat is stale (restart recovery), and retry with exponential back-off; permanent document errors fail immediately.
Progress is pushed over SignalR and also available by polling.

## Retrieval and the tutor (RAG)

`RetrievalService` builds an in-memory index per book (cached by book id + content version + embedding model):
BM25 over Arabic-aware tokens (diacritics folded, definite article stripped) plus cosine similarity over the stored
vectors, fused with reciprocal-rank fusion. Explicit page references in the question ("explain page 42", "اشرح صفحة ٤٢")
are always included. Scope is the conversation's book, an explicit list of the student's books, or all books of a
subject — always filtered by owner.

Vector search runs in the application process. For a single student's library (tens of books, thousands of chunks) this
is fast and works on any SQL Server version. **Upgrade path:** SQL Server 2025 / Azure SQL offer a native `VECTOR` type
and `VECTOR_DISTANCE`; switching requires fixing the dimension per embedding model in the schema, so it was deferred.

`TutorService` streams answers as Server-Sent Events (`start`, `delta`, `done`, `error`). Passages are numbered and
wrapped in `<textbook_passages>`; the model cites them as `[n]`. After generation, `CitationProcessor` keeps only markers
that point to supplied passages and replaces them with stored chunk metadata (book, chapter, page range, quote) — page
numbers shown to the student never come from model output. Answers without matching passages are flagged as not grounded.
Stopping the stream saves the partial answer as "stopped"; failures are saved as errors and can be retried.

Summaries, flashcard drafts and quizzes use the same grounding idea with `[p:N]` page markers validated against the pages
actually supplied. Large scopes are summarised map-reduce style. Quiz questions are generated with structured output,
then validated: structure per type, unique choices, a supporting quote that must really occur on the cited page; options
are shuffled server-side. Rejected questions are counted, not shown.

## Learning algorithms
- **FSRS-5** scheduler with the published default weights (learning/relearning steps, same-day reviews, stability and
  difficulty updates). A card is "mastered" only when in review state with stability ≥ 21 days.
- **Deterministic grading** for multiple choice, true/false, fill-in (Arabic-normalised), numerical (relative tolerance,
  Arabic digits, units, scientific notation), matching (partial credit) and ordering; AI rubric grading for open answers
  with self-assessment fallback when the AI is unavailable.
- **Mastery** per chapter and per concept: a recency-weighted estimate shown separately from measured accuracy.
- **Planner**: priority = exam urgency × measured weakness × self-reported difficulty × remaining material; reserves time
  for due cards, rotates subjects, never exceeds the daily availability, and replaces (does not pile up) missed tasks.

## Security
- Identity password hashing and lockout; short-lived JWT access tokens kept in memory; rotating refresh tokens in an
  HttpOnly, Secure, SameSite=Strict cookie scoped to `/api/auth`, stored hashed, with reuse detection (a replayed token
  revokes all sessions). Refresh/logout also require a custom header (CSRF defence).
- Fallback authorization policy: everything requires authentication unless explicitly anonymous.
- Ownership checks in every service; integration tests verify isolation for books, files, pages, conversations,
  summaries and quizzes, including cross-book scopes.
- Prompt-injection containment: textbook text and student answers are wrapped in tagged blocks, `<`/`>` escaped,
  declared untrusted in the system prompt; the model has no tools (the Claude-account provider runs the CLI with
  `--tools ""`, no MCP servers, settings files, hooks or plugins, in an empty directory); retrieval is owner-filtered.
- Upload validation (size while streaming, header, parse, extension), rate limits (auth per IP; AI, uploads and global
  per user), per-user daily AI quotas, security headers, problem-details errors without stack traces, no secrets in the
  frontend, logs without tokens or document text.
- Markdown from the model is sanitised with DOMPurify; KaTeX output (generated locally with `trust: false`) is inserted
  after sanitisation.

## Frontend
- Standalone components, signals, zoneless change detection, lazy-loaded routes per feature.
- Runtime i18n (`I18nService` + `t` pipe) with `ar`/`en` dictionaries typed against each other; switching language flips
  `<html dir/lang>`; all spacing uses CSS logical properties; directional icons mirror in RTL; interpolated values are
  wrapped in Unicode isolates so mixed Arabic/Latin titles render correctly.
- Design tokens (light/dark) in `styles.scss`; Angular CDK for dialogs, menus and focus management.
- The reader renders pages lazily with pdf.js (canvas + selectable text layer), stores highlights as page-relative
  rectangles, tracks real reading time per page, and repairs Arabic selections (pdf.js returns them in visual order) by
  snapping them to the server-extracted page text before they are sent to the tutor.
