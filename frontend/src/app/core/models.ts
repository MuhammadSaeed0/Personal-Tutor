// Types mirroring the backend DTOs (enums are serialised as camelCase strings).

export type Lang = 'ar' | 'en';
export type ExplanationLang = 'ar' | 'en' | 'fr';

export interface Preferences {
  uiLanguage: Lang;
  theme: 'light' | 'dark' | 'system';
  explanationLanguage: ExplanationLang;
  bilingualTerms: boolean;
  teachingStyle: 'guided' | 'concise';
  motivationEnabled: boolean;
  notificationsEnabled: boolean;
  focusMinutes: number;
  breakMinutes: number;
  weeklyGoalMinutes: number;
}

export interface AcademicProfile {
  grade: string | null;
  curriculum: string | null;
  academicYear: string | null;
  targetScore: number | null;
  dailyStudyMinutes: number;
  difficultSubjectIds: number[];
  onboardingCompleted: boolean;
}

export interface Profile {
  id: string;
  email: string;
  displayName: string;
  createdAt: string;
  preferences: Preferences;
  academic: AcademicProfile;
}

export interface AuthResponse {
  accessToken: string;
  expiresAt: string;
  profile: Profile;
}

export interface Subject {
  id: number;
  code: string;
  nameAr: string;
  nameEn: string;
  color: string;
  icon: string;
  sortOrder: number;
  contentLanguage: string;
}

export interface Exam {
  id: string;
  title: string;
  date: string;
  subjectId: number | null;
  notes: string | null;
  daysLeft: number;
}

export type BookStatus = 'uploaded' | 'processing' | 'ready' | 'readyWithWarnings' | 'failed';
export type ProcessingStage =
  | 'queued' | 'validating' | 'extractingText' | 'recognizingScannedPages' | 'detectingStructure'
  | 'processingTablesAndFigures' | 'preparingSearch' | 'generatingResources' | 'completed';

export interface ProcessingStatus {
  bookId: string;
  jobId: string | null;
  bookStatus: BookStatus;
  jobStatus: 'queued' | 'running' | 'succeeded' | 'failed' | 'cancelled' | null;
  stage: ProcessingStage;
  progress: number;
  attempt: number;
  errorCode: string | null;
  updatedAt: string | null;
}

export interface Book {
  id: string;
  subjectId: number;
  title: string;
  academicYear: string | null;
  semester: string | null;
  collection: string | null;
  tags: string[];
  status: BookStatus;
  pageCount: number;
  detectedLanguage: string | null;
  fileSizeBytes: number;
  originalFileName: string;
  createdAt: string;
  lastOpenedAt: string | null;
  lastPage: number | null;
  readingProgress: number;
  processing: ProcessingStatus | null;
}

export type ChapterStatus = 'notStarted' | 'inProgress' | 'completed' | 'alreadyKnown';

export interface Chapter {
  id: string;
  parentId: string | null;
  title: string;
  detectedTitle: string;
  level: number;
  order: number;
  startPage: number;
  endPage: number;
  source: 'pdfOutline' | 'headingHeuristic' | 'user';
  status: ChapterStatus;
}

export interface DocWarning {
  id: number;
  pageNumber: number | null;
  code: string;
  message: string;
  severity: 'info' | 'warning' | 'error';
}

export interface PageInfo {
  pageNumber: number;
  charCount: number;
  extractionMethod: 'none' | 'embeddedText' | 'ocr';
  isScanned: boolean;
  imageCount: number;
  hasMath: boolean;
  hasTable: boolean;
}

export interface BookDetail {
  book: Book;
  chapters: Chapter[];
  warnings: DocWarning[];
  pages: PageInfo[];
}

export interface ReaderState {
  lastPage: number;
  readPages: number[];
  readPageCount: number;
  totalSeconds: number;
  bookmarks: Bookmark[];
}

export interface Bookmark {
  id: string;
  pageNumber: number;
  label: string | null;
  createdAt: string;
}

export interface HighlightRect { x: number; y: number; w: number; h: number; }

export interface Highlight {
  id: string;
  pageNumber: number;
  text: string;
  color: string;
  rects: HighlightRect[];
  annotation: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface SourceReference {
  marker: number;
  bookId: string;
  bookTitle: string;
  chunkId: number | null;
  chapterTitle: string | null;
  pageStart: number;
  pageEnd: number;
  quote: string | null;
}

export interface Conversation {
  id: string;
  bookId: string | null;
  subjectId: number | null;
  title: string;
  createdAt: string;
  updatedAt: string;
  lastMessagePreview: string | null;
}

export type MessageStatus = 'complete' | 'streaming' | 'stopped' | 'error';

export interface ChatMessage {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  status: MessageStatus;
  citations: SourceReference[];
  followUps: string[];
  grounded: boolean;
  contextPage: number | null;
  selectedText: string | null;
  action: string | null;
  feedback: number | null;
  errorCode: string | null;
  createdAt: string;
}

export type SummaryScope = 'selection' | 'page' | 'pageRange' | 'chapter' | 'book';
export type SummaryStyle =
  | 'quick' | 'simple' | 'detailed' | 'keyPoints' | 'definitions' | 'formulas'
  | 'comparisons' | 'causesEffects' | 'examQuestions' | 'revisionSheet';

export interface Summary {
  id: string;
  bookId: string;
  chapterId: string | null;
  scope: SummaryScope;
  style: SummaryStyle;
  language: string;
  pageStart: number | null;
  pageEnd: number | null;
  title: string;
  content: string;
  citations: SourceReference[];
  warnings: string[];
  isEdited: boolean;
  createdAt: string;
  updatedAt: string;
}

export type CardState = 'new' | 'learning' | 'review' | 'relearning';
export type Rating = 'again' | 'hard' | 'good' | 'easy';

export interface Flashcard {
  id: string;
  subjectId: number;
  bookId: string | null;
  chapterId: string | null;
  front: string;
  back: string;
  tags: string[];
  sourceType: string;
  sourcePage: number | null;
  state: CardState;
  due: string;
  stability: number;
  difficulty: number;
  reps: number;
  lapses: number;
  isMastered: boolean;
  isSuspended: boolean;
  lastReview: string | null;
  createdAt: string;
}

export interface FlashcardDraft { front: string; back: string; page: number | null; tags: string[]; }

export interface GeneratedFlashcards { subjectId: number; bookId: string; chapterId: string | null; drafts: FlashcardDraft[]; warnings: string[]; }

export interface FlashcardStats {
  total: number; dueNow: number; new: number; learning: number; review: number; mastered: number;
  reviewsToday: number; retention30d: number | null; reviewStreak: number; forecast: { date: string; count: number }[];
}

export interface IntervalPreview { again: number; hard: number; good: number; easy: number; }

export type QuestionType = 'multipleChoice' | 'trueFalse' | 'fillBlank' | 'shortAnswer' | 'matching' | 'ordering' | 'numerical' | 'structured';
export type Difficulty = 'easy' | 'medium' | 'hard';

export interface Question {
  id: string;
  ordinal: number;
  type: QuestionType;
  prompt: string;
  options: { choices?: string[]; left?: string[]; right?: string[]; items?: string[]; unit?: string | null };
  hint: string | null;
  difficulty: Difficulty;
  topic: string | null;
  points: number;
}

export interface Quiz {
  id: string; subjectId: number; bookId: string | null; chapterId: string | null; title: string;
  mode: 'practice' | 'exam'; difficulty: Difficulty; pageStart: number | null; pageEnd: number | null;
  timeLimitSeconds: number | null; hintsAllowed: boolean; revealImmediately: boolean; language: string;
  questionCount: number; excludedQuestionCount: number; createdAt: string; attemptCount: number; bestPercent: number | null;
}

export interface QuizDetail { quiz: Quiz; questions: Question[]; }

export interface QuizResponse {
  questionId: string; answer: unknown; markedForReview: boolean; hintUsed: boolean; isCorrect: boolean | null;
  pointsAwarded: number; gradingMethod: 'deterministic' | 'aiRubric' | 'selfAssessed' | 'pending'; feedback: string | null;
}

export interface Reveal { questionId: string; isCorrect: boolean | null; fraction: number; correctAnswer: string; explanation: string; sourcePage: number | null; sourceQuote: string | null; }

export interface Attempt {
  id: string; quizId: string; status: 'inProgress' | 'submitted'; startedAt: string; deadline: string | null; submittedAt: string | null;
  quiz: QuizDetail; responses: QuizResponse[]; revealed: Reveal[];
}

export interface QuestionReview {
  question: Question; response: QuizResponse | null; correctAnswer: string; explanation: string;
  sourcePage: number | null; sourceQuote: string | null; chapterId: string | null;
}

export interface AttemptResult {
  attemptId: string; quizId: string; quizTitle: string; bookId: string | null; score: number; maxScore: number; percent: number;
  durationSeconds: number; correctCount: number; incorrectCount: number; pendingCount: number; questions: QuestionReview[];
  topics: { topic: string; correct: number; total: number }[]; reviewPages: number[]; isPersonalBest: boolean; submittedAt: string;
}

export interface AttemptSummary {
  id: string; quizId: string; quizTitle: string; subjectId: number; mode: 'practice' | 'exam'; status: 'inProgress' | 'submitted';
  score: number; maxScore: number; percent: number; startedAt: string; submittedAt: string | null;
}

export type StudyTaskType = 'read' | 'reviewFlashcards' | 'practiceQuiz' | 'reviseWeakTopic' | 'examPreparation';

export interface StudyTask {
  id: string; date: string; order: number; type: StudyTaskType; status: 'pending' | 'completed' | 'skipped';
  subjectId: number | null; bookId: string | null; bookTitle: string | null; chapterId: string | null; chapterTitle: string | null;
  topic: string | null; estimatedMinutes: number; reasonCode: string; pageStart: number | null; pageEnd: number | null; completedAt: string | null;
}

export interface PlanDay { date: string; plannedMinutes: number; completedMinutes: number; tasks: StudyTask[]; }

export interface Plan { planId: string | null; generatedAt: string | null; dailyMinutes: number; breakEveryMinutes: number; breakMinutes: number; days: PlanDay[]; }

export interface StudySession {
  id: string; type: 'focus' | 'reading'; subjectId: number | null; bookId: string | null; chapterId: string | null; goal: string | null;
  notes: string | null; startedAt: string; endedAt: string | null; durationSeconds: number; plannedMinutes: number;
}

export interface SubjectStats {
  subjectId: number; books: number; studyMinutes30d: number; quizAccuracy: number | null; estimatedMastery: number | null; masteryEvidence: number;
  chaptersCompleted: number; chaptersTotal: number; pagesRead: number; pagesTotal: number; cardsDue: number;
}

export interface TopicStat {
  id: string; subjectId: number; bookId: string | null; chapterId: string | null; topic: string; isChapter: boolean; questionAttempts: number;
  questionCorrect: number; measuredAccuracy: number | null; cardReviews: number; estimatedMastery: number;
  band: 'insufficient' | 'weak' | 'developing' | 'strong'; lastMistakePage: number | null; updatedAt: string;
}

export interface Recommendation { code: string; priority: number; params: Record<string, unknown>; }

export interface Overview {
  studyMinutes7d: number; studyMinutes30d: number; currentStreak: number; longestStreak: number; chaptersCompleted: number; chaptersTotal: number;
  quizzesTaken30d: number; quizAccuracy30d: number | null; cardsReviewed30d: number; retention30d: number | null; weeklyGoalMinutes: number;
  daily: { date: string; minutes: number }[]; quizTrend: { attemptId: string; date: string; percent: number; subjectId: number }[];
  subjects: SubjectStats[]; strongest: TopicStat[]; weakest: TopicStat[]; recommendations: Recommendation[]; hasData: boolean;
}

export interface Dashboard {
  displayName: string; onboardingCompleted: boolean; hasBooks: boolean; motivationEnabled: boolean;
  currentStreak: number; longestStreak: number; weeklyMinutes: number; weeklyGoalMinutes: number;
  readingProgressPercent: number | null; chaptersCompleted: number; chaptersTotal: number;
  today: PlanDay | null; subjects: SubjectStats[]; upcomingExams: Exam[];
  continue: { bookId: string; title: string; subjectId: number; lastPage: number; pageCount: number; progressPercent: number; chapterTitle: string | null; lastOpenedAt: string | null }[];
  weakTopics: TopicStat[]; cardsDue: number;
  recommendedQuizzes: { bookId: string; chapterId: string; chapterTitle: string; subjectId: number; reason: string }[];
  achievements: { code: string; earnedAt: string }[];
  recommendations: Recommendation[];
  processing: ProcessingStatus[];
}

export interface Note {
  id: string; subjectId: number | null; bookId: string | null; chapterId: string | null; pageNumber: number | null; title: string; content: string;
  tags: string[]; isPinned: boolean; sourceType: string; sources: SourceReference[]; createdAt: string; updatedAt: string;
}

export interface Paged<T> { items: T[]; page: number; pageSize: number; totalCount: number; totalPages: number; }

export interface SearchHit {
  chunkId: number; bookId: string; bookTitle: string; chapterId: string | null; chapterTitle: string | null; pageStart: number; pageEnd: number;
  snippet: string; score: number; matchType: string;
}

export interface SystemStatus { ai: { configured: boolean; provider: 'anthropic' | 'claude-account' | 'openai-compatible' | 'none'; model: string | null }; embeddings: { model: string; semantic: boolean }; ocr: { available: boolean }; }

export interface SessionInfo { id: string; createdAt: string; lastUsedAt: string; userAgent: string | null; isCurrent: boolean; }

export interface AppNotification { id: string; type: string; entityId: string | null; data: string | null; isRead: boolean; createdAt: string; }

export interface ApiProblem { status: number; code: string; title?: string; errors?: Record<string, string[]>; [key: string]: unknown; }

export interface PageTextDto { pageNumber: number; text: string; extractionMethod: 'none' | 'embeddedText' | 'ocr'; isScanned: boolean; }
