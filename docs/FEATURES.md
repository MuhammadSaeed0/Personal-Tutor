# Features, known limitations and roadmap

## Implemented
**Accounts & personalisation** — registration, login, logout, password reset, change password, device/session list
and revoke, profile, Arabic/English interface, light/dark/system theme, explanation language (ar/en/fr), bilingual
terms, teaching style, optional onboarding (grade, curriculum, exams, daily time, difficult subjects, target), exam
management, data export, account deletion.

**Sources** — besides PDFs: **YouTube lessons** (captions, including YouTube's automatic ones, tidied by AI, split into
timed segments and topic sections; embedded player with a clickable, auto-following transcript; citations as video
times) and **photos** of book or notebook pages, worksheets or a board (read by Claude vision with figure descriptions;
photo viewer with the read text underneath; more photos can be added later).

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
language selector, prompt-injection containment, per-user quotas; **voice**: ask by speaking (browser speech recognition,
Egyptian Arabic) and listen to answers (browser speech synthesis), also in the exam assistant. Retrieval matches meaning
with a local multilingual-e5-small model (no key, no network) and falls back to keyword matching.

**Study resources** — five summary styles (older summaries in other styles still open) for selection/page/range/chapter/book with page citations, extraction
warnings, editing, regeneration in another style/language, **PDF download** (rendered on the server by the installed
Edge/Chrome: real selectable Arabic text, KaTeX math, citations as pages, photos or video times, page numbers),
Markdown export and print; flashcards (manual, from
selection, AI drafts with review, from summaries, notes and quiz mistakes) scheduled with FSRS-5, interval preview,
due queue, statistics, retention, forecast, suspend. **Study pack**: on upload (checked by default) or later from the
source page, a quick summary, flashcards and a short quiz are made in the background for each of the first 10 chapters,
with progress on the source page and a notification.

**Assessment** — 8 question types, subject-aware defaults, difficulty (easy, medium, hard or a graded mix ordered from
easy to hard), **diagram questions** (free-body diagrams, circuits, apparatus, labelled biological figures, crystal
systems, rock layers… drawn by Claude as SVG, sanitised on the server and in the browser, with label and answer-leak
checks) in practice quizzes and mock exams, scope, weak-area focus, exam blueprints, timers
with server-side deadlines, autosave, answer sheet, mark for review, hints with point penalty, immediate-feedback
practice mode, AI-generated questions validated against the source (unsupported/ambiguous ones discarded), deterministic
grading, AI rubric grading with self-assessment fallback, results with topic breakdown, review pages, explanations and
personal bests, attempt history; an **exam assistant** side chat available for the whole quiz or exam (no daily
quota) that explains meanings, terms and laws but never gives or confirms answers (replies are checked against the answer
key before the student sees them), with each use counted on the result. Quizzes are written in the background (the
student can leave the page and is told when they are ready); drawings get a second review before use. **Ministry-format
exams** (Thanaweya Amma) follow an editable template per subject (defaults from the Ministry's 2026 specification for the
science subjects: 44 multiple choice of which 12 carry two marks, 2 essay questions, 60 marks, 3 hours) and show a bubble
answer sheet; any source that is a past or sample paper can be **turned into an exam** (questions copied in order, with
the paper's answers or marked suggested answers). **Mistakes notebook**: wrong answers come back after 1 and 3 days in
review quizzes copied from the originals (no AI call) until answered correctly twice in a row.

**Planning & progress** — adaptive 7-day plan (exams, weakness, difficulty, backlog, due cards, availability, no
pile-up), complete/skip/reschedule, **today's study** (one guided session: due cards, due mistakes, today's plan tasks),
focus timer with Pomodoro presets, goals, notes and history, dashboard, subject
workspaces, analytics (study time, quiz trend, retention, subject progress, concept/chapter mastery heatmap with
measured vs estimated), data-derived recommendations, private achievements (can be disabled), notifications, an **AI usage**
page (requests and tokens per day and feature against the daily limits).

**Tools** — periodic table (118 elements, Arabic names) with molar-mass calculator, exact chemical-equation balancer
with "check my coefficients"; the units converter and language practice still exist at /tools/units and /tools/practice
but are no longer linked.

**Platform** — clean architecture, EF Core migrations, problem-details errors, rate limiting, health checks, Swagger,
AI through your own Claude account (Claude Code) or an API key, 160 backend tests (incl. end-to-end journey, ownership
isolation and opt-in live-model tests), 36 frontend tests, `run.ps1`/`stop.ps1`, GitHub Actions CI,
i18n coverage check, browser smoke test.

## Known limitations
- **Diagram questions** are drawn by the AI: they are checked automatically for safety, matching labels and not giving
  the answer away, but not for scientific accuracy — an occasional drawing can be imprecise. They take longer and use
  more of the AI plan than text questions.
- **YouTube videos need captions.** Videos without any captions are rejected; automatic captions can contain
  recognition errors (flagged per video). Captions are read with YoutubeExplode, an unofficial client that can break
  when YouTube changes; only public videos work. What is shown on screen (slides, drawings) is not read — only speech.
- **Photos** are read one request per photo; blurry or skewed photos give lower-quality text (flagged as low legibility).
  HEIC photos only work where the browser or server can decode them; JPEG/PNG are safest.
- **Live AI verified at the provider level, not yet for every feature.** The Claude-account provider passed real
  streaming, structured-output and multi-turn tests; summaries, quiz generation and rubric grading were tested end to
  end with a scripted model. Prompt quality should still be reviewed on real textbooks.
- **Claude-account mode is personal.** It uses the server owner's plan limits and must not be offered to other users;
  multi-user deployments need an API key.
- **Semantic search needs the local model files.** Without them (`scripts/get-model.ps1`) search falls back to word overlap.
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
4. SQL Server 2025 native vector search.
5. Teacher/parent sharing (explicit, permissioned) and an admin area for multi-tenant deployments.
6. Offline-friendly PWA for the reader and flashcard review.
7. Richer subject tools: formula library extraction per chapter, conjugation drills, rock, mineral and fossil reference images for geology.
8. Background quiz generation with progress, and exam blueprints saved per subject.
9. Accessibility audit with screen-reader users; reduced-motion and high-contrast themes.
10. Observability: OpenTelemetry traces for AI calls, processing dashboards.
