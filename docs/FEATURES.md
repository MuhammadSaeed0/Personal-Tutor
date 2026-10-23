# Features, known limitations and roadmap

## Implemented
**Accounts & personalisation** — registration, login, logout, password reset, change password, device/session list
and revoke, profile, Arabic/English interface, light/dark/system theme, explanation language (ar/en/fr), bilingual
terms, teaching style, optional onboarding (grade, curriculum, exams, daily time, difficult subjects, target), exam
management, data export, account deletion.

**Library & processing** — drag-and-drop upload with progress and cancel, PDF/size validation, duplicate detection,
subject/year/semester/collection/tags, live processing timeline (SignalR + polling), restart-safe background jobs with
retries, embedded-text extraction with Arabic RTL repair, scanned pages read by Claude vision (verbatim transcription plus
figure descriptions; Tesseract as offline alternative), chapter detection (outline →
headings, never invented), table/figure flags, per-page warnings, chapter title correction, reprocess, delete.

**Reader** — continuous pdf.js rendering with lazy pages, page navigation/jump, zoom, fit width/page, thumbnails,
chapters with progress status, bookmarks, in-book search, highlights in four colours with annotations, selection
toolbar (explain, simplify, summarise, translate, example, step-by-step, questions, flashcard, note), Arabic selection
repair, resume position, real reading-time tracking, chapter completion, night mode, distraction-free mode, full
screen, keyboard shortcuts, page text view (accessibility / scanned pages), mobile bottom sheets.

**AI tutor** — conversations per book or per subject (explicit cross-book scope), streaming with stop/retry/regenerate,
hybrid retrieval, explicit page questions, selected-text context, verified page citations that open the page,
not-grounded notice, "beyond the textbook" labelling, follow-up suggestions, feedback, copy, save to notebook, answer
language selector, prompt-injection containment, per-user quotas.

**Study resources** — ten summary styles for selection/page/range/chapter/book with page citations, extraction
warnings, editing, regeneration in another style/language, Markdown export and print-to-PDF; flashcards (manual, from
selection, AI drafts with review, from summaries, notes and quiz mistakes) scheduled with FSRS-5, interval preview,
due queue, statistics, retention, forecast, suspend.

**Assessment** — 8 question types, subject-aware defaults, difficulty, scope, weak-area focus, exam blueprints, timers
with server-side deadlines, autosave, answer sheet, mark for review, hints with point penalty, immediate-feedback
practice mode, AI-generated questions validated against the source (unsupported/ambiguous ones discarded), deterministic
grading, AI rubric grading with self-assessment fallback, results with topic breakdown, review pages, explanations and
personal bests, attempt history.

**Planning & progress** — adaptive 7-day plan (exams, weakness, difficulty, backlog, due cards, availability, no
pile-up), complete/skip/reschedule, focus timer with Pomodoro presets, goals, notes and history, dashboard, subject
workspaces, analytics (study time, quiz trend, retention, subject progress, concept/chapter mastery heatmap with
measured vs estimated), data-derived recommendations, private achievements (can be disabled), notifications.

**Tools** — periodic table (118 elements, Arabic names) with molar-mass calculator, exact chemical-equation balancer
with "check my coefficients", SI units and converters, language practice with writing feedback.

**Platform** — clean architecture, EF Core migrations, problem-details errors, rate limiting, health checks, Swagger,
AI through your own Claude account (Claude Code) or an API key, 73 backend tests (incl. end-to-end journey, ownership
isolation and opt-in live-model tests), 30 frontend tests,
i18n coverage check, browser smoke test.

## Known limitations
- **Live AI verified at the provider level, not yet for every feature.** The Claude-account provider passed real
  streaming, structured-output and multi-turn tests; summaries, quiz generation and rubric grading were tested end to
  end with a scripted model. Prompt quality should still be reviewed on real textbooks.
- **Claude-account mode is personal.** It uses the server owner's plan limits and must not be offered to other users;
  multi-user deployments need an API key.
- **Embeddings default to a lexical fallback.** Semantic search quality requires Voyage or another embedding provider.
- **Scanned pages** cost one Claude request each and count against the plan's limits; pages with low legibility are
  flagged and recognition can contain small errors. With Tesseract instead, Arabic quality is much lower.
- **Figures on pages with embedded text are not interpreted**; figures on scanned pages are described by Claude. Questions based
  on geological maps, cross-sections, rock/fossil photos or other diagrams are therefore not offered.
- **Tables** lose row/column structure in extracted text.
- **Arabic PDF text** is repaired heuristically (RTL order, mirrored brackets, detached diacritics are dropped).
  Unusual encodings (custom fonts without Unicode maps) can still yield garbled text; reprocessing with OCR is the
  fallback when Tesseract is installed.
- **Chapter detection** relies on PDF outlines or recognisable headings; some books will need manual titles.
- **Reprocessing** resets chapter progress (chapter ids are recreated); highlights, notes and cards are kept.
- **Password-reset email** is not sent in this build (link is logged in Development); plug in an `IEmailSender`.
- **Vector search** runs in memory per student library; very large libraries would benefit from SQL Server 2025
  `VECTOR` indexes.
- **No admin area**: deliberately omitted for a single-tenant student deployment.
- Quiz generation runs synchronously (20–60 s); there is a progress state but no background queue for it.

## Roadmap (prioritised)
1. Validate prompts with real textbooks and a live Claude key; tune effort/model per feature and add prompt caching.
2. Email provider for password resets and optional study reminders.
3. Vision support for figures/diagrams (render page regions and send them to a vision-capable model).
4. SQL Server 2025 native vector search; background re-embedding when the embedding model changes.
5. Teacher/parent sharing (explicit, permissioned) and an admin area for multi-tenant deployments.
6. Offline-friendly PWA for the reader and flashcard review.
7. Richer subject tools: formula library extraction per chapter, conjugation drills, rock, mineral and fossil reference images for geology.
8. Background quiz generation with progress, and exam blueprints saved per subject.
9. Accessibility audit with screen-reader users; reduced-motion and high-contrast themes.
10. Observability: OpenTelemetry traces for AI calls, processing dashboards.
