# Riwaq — Implementation checklist

Legend: ✅ done and tested · 🟡 done, verified only partly (see note) · ⬜ not done

## Phase 1 — Foundation
- ✅ Repository structure: backend (Domain / Application / Infrastructure / Api / Tests), frontend, e2e, docs
- ✅ ASP.NET Core 10, EF Core 10 migrations, SQL Server, seed of the seven subjects
- ✅ Identity + JWT + rotating HttpOnly refresh cookie (reuse detection, CSRF header), lockout, password reset, sessions
- ✅ Profile, preferences, onboarding (optional questions), exams, academic profile
- ✅ Problem-details errors with stable codes, rate limiting, security headers, health checks, Swagger, forwarded headers
- ✅ Design system (tokens, light/dark, components), Arabic RTL default + English LTR runtime switch, bidi isolation

## Phase 2 — Core learning workspace
- ✅ Dashboard, subject workspaces (×7), library, book management (rename, subject, tags, delete, reprocess, chapter titles)
- ✅ Secure upload (streamed size check, header/parse validation, SHA-256 duplicates), progress, cancel
- ✅ Restart-safe DB job queue (atomic claim, heartbeat, recovery, back-off), SignalR progress + polling
- ✅ Pipeline: validate → extract (Arabic RTL repair) → OCR → structure → tables/figures → chunk → embed; warnings
- ✅ Scanned PDFs read by Claude vision (page image → verbatim text + figure descriptions), chapters from recognised
      headings, reuse on reprocess, usage-limit handling; verified live on a real 251-page scanned textbook page
- 🟡 Tesseract OCR alternative: implemented, unit-tested parser; not executed here (not installed)
- ✅ Reader: lazy pdf.js pages, navigation, zoom, fit, thumbnails, TOC, bookmarks, search, highlights + notes,
      selection toolbar, Arabic selection repair, resume, reading-time tracking, night/focus/full-screen, shortcuts, mobile sheets

## Phase 2b — More source kinds
- ✅ YouTube lessons: link parsing, captions via YoutubeExplode, ~2-minute timed segments, AI clean-up and topic sections
      (resumable cache), player + transcript reader with seek/follow, time citations; verified live on a 59-minute Arabic lesson
- ✅ Photos: multi-photo sources (normalised with SkiaSharp, client-side downscaling), Claude-vision reading with figure
      descriptions, chapters from recognised headings, photo viewer with selectable text, add photos later; verified live

## Phase 3 — AI tutoring
- ✅ Provider abstraction: Anthropic (official SDK, streaming, structured output, fallback), OpenAI-compatible, disabled
- ✅ Claude-account provider (`ClaudeCode`): headless Claude Code CLI with all tools/settings disabled; streaming, JSON schema, history
- ✅ Embeddings: Voyage, OpenAI-compatible, local lexical fallback; hybrid BM25 + vector retrieval, page references
- ✅ Tutor: conversations, SSE streaming, stop, retry, feedback, follow-ups, selection actions, cross-book scope
- ✅ Server-side citation validation; prompt-injection containment
- ✅ Summaries (10 styles, map-reduce, page citations, edit, regenerate, export)
- 🟡 Live LLM behaviour: provider verified live through the Claude account (3 opt-in tests); feature prompts
      (summaries, quizzes, grading) tested with a scripted model

## Phase 4 — Active learning
- ✅ FSRS-5 flashcards (manual, selection, AI drafts, summaries, notes, quiz mistakes), review session, stats
- ✅ Quizzes & mock exams: 8 types, blueprint, timer, autosave, hints, reveal mode, validation & grounding check
- ✅ Deterministic grading, AI rubric grading with self-assessment fallback, results & history
- ✅ Chapter- and concept-level mastery (measured vs estimated)

## Phase 5 — Personalisation
- ✅ Adaptive planner (complete / skip / reschedule / availability), focus mode with history
- ✅ Analytics, recommendations from recorded data only, achievements (optional), notifications
- ✅ Notebook (CRUD, tags, pin, search, from tutor answers with sources, to flashcards/quiz, export)
- ✅ Subject tools: periodic table, equation balancer, units, language practice & writing feedback

## Phase 6 — Production readiness
- ✅ Runs directly on the host with a local SQL Server (Docker removed by request); single-process publish documented
- ✅ README, architecture, API, deployment, features/limitations/roadmap docs
- ✅ Tests: backend 111 passed + 7 opt-in skipped (real AI / real YouTube); frontend 33 passed; i18n coverage; browser smoke journey (Edge)
- ✅ Production build (initial 502 kB / 131 kB gzipped); no vulnerable NuGet packages
- ⬜ Email provider for password-reset messages (logged in Development only)
- ⬜ Accessibility audit with assistive technology (keyboard and labels done; screen-reader pass pending)

## Verification log
- Backend: `RIWAQ_TEST_CLAUDE_CLI=1 dotnet test` → 71 passed (incl. 3 live Claude-account tests), 2 skipped (API key), 0 failed.
- Frontend: `ng test --watch=false` → 30 passed; `npm run check:i18n` → 684 keys present in ar/en; `ng build` OK.
- Browser (Edge, headless) against the running stack: register → onboarding → upload Arabic PDF → processed with both
  chapters detected → reader renders → selection repaired to exact page text → highlight → in-book search → dashboard
  RTL/LTR → library → flashcards → balancer → plan → mobile layout. Only console error: the expected 401 of the
  session-restore probe before login.
