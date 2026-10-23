# API reference

Interactive OpenAPI documentation is served at **`/swagger`** in Development (enable elsewhere with `Swagger:Enabled=true`).
This page summarises the conventions and endpoints.

## Conventions
- JSON, camelCase; enums are camelCase strings (`"readyWithWarnings"`, `"multipleChoice"`).
- Authentication: `Authorization: Bearer <accessToken>` (from login/register/refresh). Everything requires authentication
  unless marked *anonymous*.
- Errors are RFC 7807 problem details with a stable `code`, e.g.
  `{"status":409,"title":"This PDF has already been uploaded.","code":"duplicate_book","bookId":"…","traceId":"…"}`.
  Validation errors: `code: "validation_failed"` with `errors`. AI errors: `ai_not_configured`, `ai_unavailable` (503),
  `ai_rate_limited`, `ai_quota_exceeded` (429), `ai_refused`, `ai_invalid_output`.
- Paged lists return `{ items, page, pageSize, totalCount, totalPages }` (`page`, `pageSize` ≤ 100).
- Rate limits: auth 20/min per IP; AI endpoints 20/min per user; uploads 30/hour; global 600/min (configurable).
- Ids are GUIDs; requests for another student's resources return **404**.

## Auth `/api/auth`
| Method | Path | Notes |
|---|---|---|
| POST | `/register` | *anonymous* · `{email,password,displayName,uiLanguage}` → `{accessToken,expiresAt,profile}` + refresh cookie |
| POST | `/login` | *anonymous* · lockout after repeated failures |
| POST | `/refresh` | *anonymous* · cookie + header `X-Riwaq-Client` · rotates the refresh token |
| POST | `/logout` | *anonymous* · header `X-Riwaq-Client` |
| POST | `/forgot-password` | *anonymous* · always 202 |
| POST | `/reset-password` | *anonymous* · `{email,token,newPassword}` · revokes all sessions |
| POST | `/change-password` | `{currentPassword,newPassword}` |
| GET / DELETE | `/sessions`, `/sessions/{id}` | list / revoke signed-in devices |

## Profile & reference data
| Method | Path | Notes |
|---|---|---|
| GET / PATCH | `/api/me` | profile, `{displayName}` |
| PATCH | `/api/me/preferences` | language, theme, explanation language, teaching style, focus defaults, weekly goal, motivation |
| PATCH | `/api/me/academic` | grade, curriculum, year, target score, daily minutes, difficult subjects |
| PUT | `/api/me/onboarding` | onboarding answers (all optional) + exams |
| GET | `/api/subjects` | *anonymous* · the seven subjects |
| GET / POST / PUT / DELETE | `/api/exams`, `/api/exams/{id}` | `?upcoming=true` |
| GET | `/api/system/status` | *anonymous* · active AI model, embedding model, OCR availability |

## Books
| Method | Path | Notes |
|---|---|---|
| GET | `/api/books?subjectId=` | with reading progress and live processing status |
| POST | `/api/books/upload` | multipart: `file`, `subjectId`, `title?`, `academicYear?`, `semester?`, `collection?`, `tags?` (comma separated) |
| POST | `/api/books/images` | multipart: `files` (photos, in page order), `subjectId`, optional fields as above → a photo source |
| POST | `/api/books/{id}/images` | multipart `files`: append photos to a photo source (only new pages are read) |
| POST | `/api/books/youtube` | `{url, subjectId, title?, …}` → a video source; rejects invalid links (`video_url_invalid`), unreadable videos (`video_unavailable`) and videos without captions (`video_no_captions`) |
| GET / PATCH / DELETE | `/api/books/{id}` | detail = book (incl. `kind`: `pdf` / `images` / `youTube`) + chapters + warnings + page info (video pages carry `startSeconds`/`endSeconds`) |
| GET | `/api/books/{id}/processing-status` | stage, progress, error code |
| POST | `/api/books/{id}/reprocess` | |
| GET | `/api/books/{id}/chapters` · PATCH `/chapters/{chapterId}` | correct chapter titles |
| GET | `/api/books/{id}/pages/{n}/text` | extracted text of one page |
| GET | `/api/books/{id}/pages/text` | text of every page in order (video transcript) |
| GET | `/api/books/{id}/images/{n}` | one photo of a photo source (JPEG) |
| GET | `/api/books/{id}/file` | the PDF (range requests supported; 404 for photo and video sources) |
| GET | `/api/books/{id}/reader-state` | last page, read pages, bookmarks |
| PUT | `/api/books/{id}/progress` | `{page, secondsOnPage}` reading heartbeat |
| PUT | `/api/books/{id}/chapters/{chapterId}/status` | `notStarted` / `inProgress` / `completed` / `alreadyKnown` |
| POST / DELETE | `/api/books/{id}/bookmarks[/{bookmarkId}]` | |
| GET / POST / PATCH / DELETE | `/api/books/{id}/highlights[/{highlightId}]` | rects are page-relative fractions |

SignalR hub `/hubs/realtime` (token via `access_token` query parameter) sends `processingUpdated` events.

## Tutor `/api/tutor/conversations`
| Method | Path | Notes |
|---|---|---|
| GET | `?bookId=&subjectId=` | |
| POST | `` | `{bookId?, subjectId?, title?}` |
| PATCH / DELETE | `/{id}` | rename / delete |
| GET | `/{id}/messages` | |
| POST | `/{id}/messages` | **SSE**. Body `{question, action?, selectedText?, currentPage?, bookIds?, language?}`. Events: `start`, `delta {text}`, `done {message}`, `error {code}`. Abort the request to stop. |
| POST | `/{id}/messages/{messageId}/retry` | **SSE** — regenerate an answer |
| PUT | `/{id}/messages/{messageId}/feedback` | `{value: 1 | -1 | null}` |

Actions: `explain, simplify, summarize, translate, example, steps, questions, deeper, analogy, terminology, check, explain_page, summarize_section`.

## Study resources
| Method | Path | Notes |
|---|---|---|
| GET / POST | `/api/summaries` | POST `{bookId, scope: selection|page|pageRange|chapter|book, style, language?, page?, pageStart?, pageEnd?, chapterId?, selectedText?}` |
| GET / PATCH / DELETE | `/api/summaries/{id}` | edit title/content |
| GET | `/api/summaries/{id}/export` | Markdown file |
| GET | `/api/flashcards?subjectId&bookId&state&search&page` | state: `new|due|learning|mastered|suspended` |
| GET | `/api/flashcards/due` · `/stats` | review queue / statistics & 7-day forecast |
| POST | `/api/flashcards` · `/batch` · `/generate` · `/from-attempt/{attemptId}` | manual, bulk save, AI drafts (not saved), from quiz mistakes |
| PUT / DELETE | `/api/flashcards/{id}` | edit, suspend |
| GET / POST | `/api/flashcards/{id}/preview` · `/review` | interval preview per rating; `{rating: again|hard|good|easy, durationMs}` |
| GET / POST | `/api/quizzes` | POST `{bookId, chapterId?, pageStart?, pageEnd?, difficulty, count, types[], blueprint?, timeLimitMinutes?, hintsAllowed, revealImmediately, focusWeakAreas, mode: practice|exam, language?, title?}` |
| GET / DELETE | `/api/quizzes/{id}` | questions without answer keys |
| POST | `/api/quizzes/{id}/attempts` | start or resume |
| GET | `/api/attempts?subjectId&quizId` · `/api/attempts/{id}` | |
| PUT | `/api/attempts/{id}/responses/{questionId}` | `{answer, markedForReview, hintUsed}` → `{response, reveal?}` |
| POST | `/api/attempts/{id}/submit` · GET `/result` | graded result with explanations and source pages |
| POST | `/api/attempts/{id}/responses/{questionId}/self-assess` | `{correct}` for answers the AI could not grade |
| GET / POST / PUT / DELETE | `/api/notes[/{id}]` · POST `/from-message` · GET `/export` | |
| POST | `/api/search` | `{query, bookIds?, subjectId?, chapterId?, limit}` — scope is explicit in the response |

## Planning & progress
| Method | Path | Notes |
|---|---|---|
| GET | `/api/plan` | auto-generates when empty |
| POST | `/api/plan/generate` | rebuild from current data |
| PATCH | `/api/plan/tasks/{id}` | `{status?, date?}` complete / skip / reschedule |
| GET / POST | `/api/sessions` · POST `/{id}/complete` · DELETE `/{id}` | focus sessions |
| GET | `/api/dashboard` | everything the home screen needs |
| GET | `/api/analytics/overview?subjectId` · `/subjects` · `/topics` | |
| GET | `/api/achievements` · `/api/notifications` · POST `/api/notifications/read` | |
| GET | `/api/account/export` | JSON export |
| POST | `/api/account/delete` | `{password}` — deletes account, data and files |

Health: `/health/live`, `/health/ready` (includes the database).
