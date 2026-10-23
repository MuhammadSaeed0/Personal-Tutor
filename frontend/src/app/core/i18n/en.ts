export const EN = {
  app: { name: 'Riwaq Learning' },
  common: {
    all: 'All', allSubjects: 'All subjects', back: 'Back', cancel: 'Cancel', close: 'Close', copied: 'Copied', copy: 'Copy', date: 'Date',
    delete: 'Delete', details: 'Details', edit: 'Edit', export: 'Export', manage: 'Manage', min: 'min', more: 'More', next: 'Next', open: 'Open',
    pageN: 'p. {n}', pagesShort: 'pages', pagination: 'Pagination', previous: 'Previous', remove: 'Remove', retry: 'Retry', save: 'Save',
    saved: 'Saved', search: 'Search', subject: 'Subject', tryAgain: 'Try again',
    minutesShort: '{n} min', hoursShort: '{n} h', hoursMinutesShort: '{h} h {m} min'
  },
  nav: {
    account: 'Account', analytics: 'Progress', flashcards: 'Flashcards', focus: 'Focus mode', home: 'Home', library: 'Library', logout: 'Sign out',
    mainNavigation: 'Main navigation', notes: 'Notebook', notifications: 'Notifications', openMenu: 'Open menu', plan: 'Study plan',
    practice: 'Practice', quizzes: 'Quizzes', searchPlaceholder: 'Search your books…', settings: 'Settings', skipToContent: 'Skip to content',
    subjects: 'Subjects', summaries: 'Summaries', switchLanguage: 'Switch language', toggleTheme: 'Toggle dark mode', tutor: 'AI tutor', uploadBook: 'Upload textbook'
  },
  auth: {
    heroTitle: 'Every textbook becomes your personal tutor.',
    heroText: 'Upload your books and study them with explanations, flashcards, quizzes and a plan built around your exams.',
    point1: 'Ask about any paragraph and get answers cited to the exact page',
    point2: 'Remember more with spaced-repetition flashcards',
    point3: 'Know your weak spots before the exam does',
    privacyNote: 'Your books and study data stay private to your account.',
    loginTitle: 'Welcome back', loginLead: 'Sign in to continue studying.', email: 'Email', password: 'Password', emailInvalid: 'Enter a valid email address.',
    showPassword: 'Show password', hidePassword: 'Hide password', forgot: 'Forgot password?', login: 'Sign in', noAccount: 'New to Riwaq?',
    createAccount: 'Create account', registerTitle: 'Create your account', registerLead: 'It takes less than a minute.', displayName: 'Your name',
    passwordHint: 'At least 8 characters, including a number.', haveAccount: 'Already have an account?',
    forgotTitle: 'Reset your password', forgotLead: 'Enter your email and we will send you a reset link.',
    forgotSent: 'If an account exists for this email, a reset link is on its way.', sendResetLink: 'Send reset link', backToLogin: 'Back to sign in',
    resetTitle: 'Choose a new password', newPassword: 'New password', resetPassword: 'Reset password', resetDone: 'Your password was changed. You can sign in now.'
  },
  onboarding: {
    skip: 'Skip for now', stepOf: 'Step {n} of {total}', optional: 'Optional — you can change this later in Settings.',
    gradeQ: 'Which grade or academic year are you in?', grade: 'Grade', gradePlaceholder: 'e.g. Grade 12', academicYear: 'Academic year',
    curriculumQ: 'Which curriculum are you following?', curriculumHint: 'Riwaq adapts to the books you upload — this just helps us plan.',
    curriculumOther: 'Curriculum', curricula: { national: 'National curriculum', igcse: 'IGCSE', american: 'American diploma', ib: 'IB Diploma', french: 'French baccalaureate', other: 'Other' },
    examsQ: 'When are your next exams?', examsHint: 'Your plan prioritises subjects with exams coming up.', examTitle: 'Exam name', allSubjects: 'Several subjects', addExam: 'Add exam',
    timeQ: 'How much time can you study each day?', timeHint: 'Plans never schedule more than this.',
    difficultQ: 'Which subjects do you find most difficult?', difficultHint: 'They get a little more time in your plan.',
    goalQ: 'What score are you aiming for?', explainLanguage: 'Explanations language', explainHint: 'The tutor explains in this language, whatever the language of the book.',
    finish: 'Start studying'
  },
  dashboard: {
    morning: 'Good morning, {name}', afternoon: 'Good afternoon, {name}', evening: 'Good evening, {name}',
    leadNoBooks: 'Upload your first textbook to turn it into your study space.',
    leadPlan: 'Today: {done} of {total} tasks done · about {minutes} planned.',
    leadFree: 'Nothing scheduled right now — review a few cards or continue reading.',
    streakDays: 'day streak', weeklyGoal: 'Weekly goal', thisWeek: 'this week of {goal}', daysLeft: 'days left',
    aiNotConfigured: 'The AI tutor is not configured on this server yet. Reading, flashcards, notes and planning still work.',
    processing: 'Preparing a book', welcome1: 'Upload a textbook', welcome1Text: 'Any PDF — Arabic, English or French.',
    welcome2: 'Study with your tutor', welcome2Text: 'Highlight text and ask. Answers cite your book’s pages.',
    welcome3: 'Practise and review', welcome3Text: 'Flashcards, quizzes and a plan that adapts to you.', uploadFirst: 'Upload your first textbook',
    todayPlan: "Today's plan", fullPlan: 'Full plan', noTasks: 'No tasks for today. Generate a plan or take a well-earned break.',
    continue: 'Continue studying', subjects: 'Your subjects', subjectMeta: '{books} book(s) · {chapters}/{total} chapters', cardsDueN: '{n} cards due',
    noBooksYet: 'No books yet', recommendations: 'Recommended for you', noRecommendations: 'Keep studying — recommendations appear as you practise.',
    recommendationsBasis: 'Based only on your recorded activity and your books.', cardsDue: 'flashcards due', reviewNow: 'Review now',
    weakTopics: 'Needs attention', correctOf: '{c}/{n} correct', suggestedQuizzes: 'Check your understanding', testMe: 'Test me',
    exams: 'Upcoming exams', daysShort: 'd', noExams: 'No upcoming exams. Add them to get a better plan.', achievements: 'Milestones'
  },
  library: {
    title: 'Your textbooks', subtitle: 'Upload, organise and manage the books you study from.', allYears: 'All years', filter: 'Filter books…',
    emptyTitle: 'Your library is empty', emptyText: 'Upload a PDF textbook. Riwaq will read it, find its chapters and prepare it for studying.',
    failed: 'Processing failed', lastPage: 'Last read: p. {page}', warnings: 'Notes', open: 'Open', manage: 'Manage', noMatches: 'No books match these filters.',
    deleteTitle: 'Delete this book?', deleteMessage: '"{title}" and everything derived from it (pages, highlights, conversations, summaries) will be deleted. Flashcards and quiz history are kept.',
    deleted: 'Book deleted'
  },
  upload: {
    title: 'Upload a textbook', subtitle: 'PDF files only. Processing continues in the background.', drop: 'Drop a PDF here or click to choose',
    limits: 'PDF · up to {mb} MB', change: 'click to change', chooseSubject: 'Choose a subject', bookTitle: 'Book title', academicYear: 'Academic year',
    tags: 'Tags', tagsPlaceholder: 'e.g. term 1, revision', notPdf: 'This file is not a PDF.', tooLarge: 'The file is larger than {mb} MB.',
    start: 'Upload', uploading: 'Uploading…', cancelUpload: 'Cancel upload', processingLead: 'Your book is being prepared. You can close this window — we will keep working.',
    ready: 'Your book is ready to study.', readyWithWarnings: 'Your book is ready. Some pages had issues — see the details.',
    viewDetails: 'View details', startStudying: 'Start studying', continueInBackground: 'Continue in background', openExisting: 'Open existing book'
  },
  processing: {
    timeline: 'Processing steps',
    stages: {
      uploading: 'Uploaded', queued: 'Waiting to start', validating: 'Checking the document', extractingText: 'Extracting text',
      recognizingScannedPages: 'Reading scanned pages', detectingStructure: 'Finding chapters and headings', processingTablesAndFigures: 'Checking tables and figures',
      preparingSearch: 'Preparing search and the tutor', generatingResources: 'Generating resources', completed: 'Ready to study'
    }
  },
  processingErrors: {
    pdf_invalid_header: 'This file is not a valid PDF.', pdf_unreadable: 'The PDF could not be read. It may be damaged.',
    pdf_encrypted: 'The PDF is password-protected. Remove the password and upload it again.', pdf_no_pages: 'The PDF has no pages.',
    pdf_too_many_pages: 'The PDF has too many pages.', processing_error: 'Something went wrong while processing. Please retry.',
    ai_unavailable: 'The search service was unavailable. Please retry.', ai_rate_limited: 'The AI service was busy. Please retry in a moment.',
    ai_not_configured: 'The embedding provider is not configured correctly.'
  },
  warnings: {
    scanned_page_no_ocr: 'This page is scanned (an image) and OCR is not available on the server, so its text could not be read.',
    ocr_low_confidence: 'Text recognised from this scanned page may contain errors.', ocr_failed: 'Text on this scanned page could not be recognised.',
    no_structure_detected: 'No reliable chapter structure was found. You can still navigate by page.',
    structure_estimated: 'Chapters were estimated from heading styles — please check and correct the titles.',
    figures_not_interpreted: 'Some pages contain figures or diagrams. Their visual content is not interpreted; only surrounding text is searchable.',
    tables_flattened: 'Tables were detected; their text is kept but rows and columns may be merged.',
    embeddings_unavailable: 'Semantic search vectors could not be created; keyword search still works. Reprocess later.',
    no_text_extracted: 'No readable text could be extracted, so the tutor and search cannot use this book yet.'
  },
  book: {
    read: 'Read', quiz: 'Make a quiz', processing: 'Processing', details: 'Book details', semester: 'Semester', collection: 'Collection',
    structure: 'Chapters', estimated: 'Estimated', fromOutline: 'From the PDF outline', noStructure: 'No chapters were detected for this book.',
    structurePending: 'Chapters appear after processing.', chapterTitle: 'Chapter title', pages: 'pp. {from}–{to}', chapterStatus: 'Chapter status',
    renameChapter: 'Rename chapter', extraction: 'Text extraction', textPages: 'Pages with text', scannedPages: 'Scanned pages', ocrPages: 'Read with OCR',
    language: 'Detected language', warnings: 'Processing notes', noWarnings: 'No issues were found.', moreWarnings: '+{n} more notes',
    reprocess: 'Reprocess book', reprocessTitle: 'Reprocess this book?', reprocessMessage: 'The text will be extracted again. Your highlights and notes are kept; chapter progress may reset.',
    delete: 'Delete book'
  },
  chapterStatus: { notStarted: 'Not started', inProgress: 'In progress', completed: 'Completed', alreadyKnown: 'Already known' },
  languages: { ar: 'Arabic', en: 'English', fr: 'French', mixed: 'Mixed' },
  extraction: { none: 'No text', embeddedText: 'Embedded text', ocr: 'Recognised with OCR' },
  reader: {
    toolbar: 'Reader toolbar', toggleContents: 'Show contents', previousPage: 'Previous page', nextPage: 'Next page', goToPage: 'Go to page',
    zoomIn: 'Zoom in', zoomOut: 'Zoom out', fit: 'Fit', fitWidth: 'Width', fitPage: 'Page', bookmark: 'Bookmark this page', nightMode: 'Night reading',
    focusMode: 'Distraction-free mode', fullscreen: 'Full screen', tutor: 'Tutor', notes: 'Notes', tools: 'Tools', contents: 'Contents',
    studyPanel: 'Study panel', loading: 'Opening your book…', loadFailed: 'The book could not be opened.',
    scannedNotice: 'This page is a scanned image without readable text. Text selection is not available here.', askAboutPage: 'Ask about this page',
    selectionActions: 'Actions for selected text', highlight: 'Highlight', annotation: 'Highlight & note', annotationPlaceholder: 'Add a personal note…',
    actions: { explain: 'Explain', simplify: 'Simplify', summarize: 'Summarise', translate: 'Translate', example: 'Example', steps: 'Step by step', questions: 'Questions', flashcard: 'Card' },
    toc: 'Chapters', pages: 'Pages', bookmarks: 'Bookmarks', noToc: 'No chapters were detected.', noTocHint: 'Use page navigation or search instead.',
    noBookmarks: 'No bookmarks yet. Press B or the bookmark button to save a page.', searchBook: 'Search this book…', noResults: 'No matches found.'
  },
  tutor: {
    newConversation: 'New conversation', newChat: 'New chat', untitled: 'Untitled conversation', rename: 'Rename', deleteTitle: 'Delete this conversation?',
    deleteMessage: 'The conversation will be permanently deleted.',
    aiNotConfigured: 'The AI tutor is not configured on this server. An administrator needs to add an AI provider key or sign the server in to a Claude account.',
    introTitle: 'Your tutor for this book', introReader: 'Highlight a passage or ask about any page. Answers cite the exact pages they rely on.',
    introGeneral: 'Ask anything about your books in this subject. Each answer shows which pages it comes from.',
    quick: { explainPage: 'Explain page {page}', summarizeSection: 'Summarise this section', testMe: 'Test my understanding', terms: 'Explain the key terms' },
    actions: {
      explain: 'Explain', simplify: 'Simplify', summarize: 'Summarise', translate: 'Translate', example: 'Example', steps: 'Step by step', questions: 'Practice questions',
      deeper: 'Go deeper', analogy: 'Analogy', terminology: 'Key terms', check: 'Check my understanding', explain_page: 'Explain this page', summarize_section: 'Summarise section'
    },
    notGrounded: 'No matching passage was found in your book — this answer may rely on general knowledge.',
    stopped: 'Stopped', sources: 'Sources', helpful: 'Helpful', notHelpful: 'Not helpful', saveToNotes: 'Save to notebook', savedToNotes: 'Saved to your notebook',
    regenerate: 'Regenerate', thinking: 'Thinking…', contextPage: 'Context: page {page}', placeholder: 'Ask your tutor…', stop: 'Stop', send: 'Send',
    answerIn: 'Answer in', disclaimer: 'AI can make mistakes — check important facts against your book.',
    pageTitle: 'Ask your tutor', pageSubtitle: 'Get explanations grounded in your own textbooks.', noBooksTitle: 'Upload a book first',
    noBooksText: 'The tutor answers from your textbooks, so it needs at least one processed book.', scope: 'What should the tutor search?',
    allBooks: 'All books in this subject', allBooksHint: 'Searches {n} book(s) and cites each separately', scopeNote: 'The tutor never searches other students’ books.'
  },
  tools: {
    summary: 'Summary', flashcards: 'Flashcards', quiz: 'Quiz', pageText: 'Page text', scope: 'Scope', style: 'Style',
    scopes: { selection: 'Selection', page: 'Page {page}', chapter: 'This chapter', book: 'Whole book' },
    generating: 'Generating…', generateSummary: 'Generate summary', noChapter: 'No chapter covers this page.', selectFirst: 'Select some text in the book first.',
    summaryDisclaimer: 'AI-generated from your book. It may be incomplete — it is not an official syllabus summary.',
    manualCard: 'Create a card', front: 'Question / prompt', back: 'Answer', saveCard: 'Save card', cardSaved: 'Card saved', aiCards: 'Generate cards with AI',
    focus: 'Focus', focuses: { general: 'Key ideas', definitions: 'Definitions', formulas: 'Formulas', vocabulary: 'Vocabulary' },
    generateCards: 'Generate cards', reviewDrafts: 'Review the drafts — untick any you do not want.', saveSelected: 'Save selected cards',
    cardsSaved: '{n} cards saved', noDrafts: 'No new cards could be created from this content.',
    quizLead: 'Test yourself on what you are reading. Questions are checked against the book before you see them.',
    quizChapter: 'Quiz on this chapter', quizPages: 'Quiz on pages {from}–{to}', noPageText: 'No text could be extracted from this page.',
    periodicTable: 'Periodic table', balancer: 'Equation balancer', units: 'Units & conversions', languagePractice: 'Language practice',
    titles: { 'periodic-table': 'Periodic table', 'equation-balancer': 'Chemical equation balancer', units: 'Units & conversions', practice: 'Language practice' },
    leads: {
      'periodic-table': 'All 118 elements with atomic masses, plus a molar-mass calculator.',
      'equation-balancer': 'Try balancing yourself first, then check — or reveal the balanced equation.',
      units: 'SI units and quick conversions for physics and chemistry.', practice: 'Practise with exercises from your own language textbooks.'
    },
    categories: {
      alkali: 'Alkali metal', alkaline: 'Alkaline earth', transition: 'Transition metal', post: 'Post-transition', metalloid: 'Metalloid', nonmetal: 'Non-metal',
      halogen: 'Halogen', noble: 'Noble gas', lanthanide: 'Lanthanide', actinide: 'Actinide', unknown: 'Unknown properties'
    },
    atomicMass: 'Atomic mass', period: 'Period', group: 'Group', molarMass: 'Molar mass calculator', invalidFormula: 'Type a valid formula, e.g. Ca(OH)2.',
    equation: 'Equation', equationHint: 'Use -> between sides and + between species, e.g. C3H8 + O2 -> CO2 + H2O',
    yourCoefficients: 'Your coefficients (in order, separated by commas)', check: 'Check', attemptRight: 'Correct — the equation is balanced!',
    attemptWrong: 'Not balanced yet. Count each element on both sides and try again.', showBalanced: 'Show balanced equation',
    balanceErrors: {
      no_arrow: 'Separate reactants and products with ->.', impossible: 'This equation cannot be balanced — check the formulas.',
      ambiguous: 'This equation has more than one independent balance (it may combine two reactions).', element: 'Unknown element symbol: {el}',
      unbalanced: 'Unbalanced brackets in a formula.', invalid: 'The equation could not be read.'
    },
    converter: 'Converter', amount: 'Amount', from: 'From', to: 'To', temperature: 'Temperature', siBase: 'SI base units', derived: 'Common derived units',
    quantities: {
      length: 'Length', mass: 'Mass', time: 'Time', energy: 'Energy', pressure: 'Pressure', volume: 'Volume', current: 'Electric current', temperature: 'Temperature',
      amount: 'Amount of substance', luminous: 'Luminous intensity', force: 'Force', power: 'Power', charge: 'Charge', voltage: 'Voltage', resistance: 'Resistance', frequency: 'Frequency'
    },
    needLanguageBook: 'Upload an Arabic, English or French textbook to practise here.', practiceExercises: 'Exercises from your book',
    grammarVocab: 'Grammar & vocabulary quiz', comprehension: 'Reading comprehension', translationPractice: 'Translation practice with the tutor',
    practiceNote: 'Exercises are generated from your textbook and checked before you see them.', writingFeedback: 'Writing feedback',
    writingPlaceholder: 'Write a paragraph in the language you are studying…', getFeedback: 'Get feedback', writingFeedbackTitle: 'Writing feedback',
    writingPrompt: 'Please correct my writing below. List each mistake with the correct form and the rule behind it, then give an improved version and one tip.',
    unknown: 'This tool does not exist.'
  },
  summaryStyles: {
    quick: 'Quick summary', simple: 'Simple explanation', detailed: 'Detailed summary', keyPoints: 'Key points', definitions: 'Definitions & terms',
    formulas: 'Formulas & equations', comparisons: 'Comparisons', causesEffects: 'Causes & effects', examQuestions: 'Exam-style questions', revisionSheet: 'Revision sheet'
  },
  summaryWarnings: {
    pages_without_text: 'Pages {pages} had no readable text and were not included.',
    ocr_pages_included: 'Pages {pages} were read with OCR and may contain recognition errors.',
    source_truncated: 'The content was too long; this covers up to page {page}.'
  },
  summaries: {
    title: 'Summaries & study guides', subtitle: 'Everything you generated from your books — editable and exportable.', edited: 'Edited',
    emptyTitle: 'No summaries yet', emptyText: 'Open a book and use Tools → Summary to create one for a page, chapter or the whole book.',
    print: 'Print / PDF', content: 'Summary content', editHint: 'Markdown supported. Page references look like [p:12].', next: 'Keep going',
    toCards: 'Turn into flashcards', toQuiz: 'Quiz me on this', regenerate: 'Regenerate', deleteTitle: 'Delete this summary?', deleteMessage: 'This cannot be undone.'
  },
  flashcards: {
    title: 'Flashcards', subtitle: 'Spaced repetition shows each card right before you would forget it.', newCard: 'New card', editCard: 'Edit card',
    startReview: 'Start review', due: 'Due', new: 'New', learning: 'Learning', mastered: 'Mastered', retention: 'Retention (30 days)', streak: 'Review streak',
    forecast: 'Reviews in the next 7 days', algorithmNote: 'Scheduling uses the FSRS-5 algorithm. A card counts as mastered only after successful reviews push its memory stability past 21 days.',
    mathHint: 'You can write math as $...$.', filterState: 'Filter by state', allStates: 'All cards', suspended: 'Suspended',
    emptyTitle: 'No flashcards yet', emptyText: 'Create cards yourself, from highlighted text, or let the AI draft them from a chapter.',
    dueNow: 'Due now', suspend: 'Suspend', resume: 'Resume', deleteTitle: 'Delete this card?', deleteMessage: 'Its review history will be deleted too.',
    states: { new: 'New', learning: 'Learning', review: 'Review', relearning: 'Relearning' }
  },
  review: {
    nothingDue: 'All caught up!', nothingDueText: 'No cards are due right now. Come back later or add new cards.', sessionDone: 'Session complete',
    reviewed: 'Reviewed', forgotten: 'Forgotten', time: 'Time', doneText: 'Cards you forgot will come back soon. Steady practice beats cramming.',
    again: 'Review more', tapToReveal: 'Tap or press Space to reveal the answer', showAnswer: 'Show answer', howWell: 'How well did you remember?',
    ratings: { again: 'Again', hard: 'Hard', good: 'Good', easy: 'Easy' },
    minutes: '{n} min', hours: '{n} h', days: '{n} d', months: '{n} mo', years: '{n} y'
  },
  quizzes: {
    title: 'Quizzes & mock exams', subtitle: 'Practise with questions generated from your books and checked against them.', newQuiz: 'New quiz', newExam: 'Mock exam',
    myQuizzes: 'My quizzes', history: 'History', emptyTitle: 'No quizzes yet', emptyText: 'Generate a quiz on a chapter, a range of pages or a whole book.',
    modes: { practice: 'Practice', exam: 'Exam' }, questionsN: '{n} questions', best: 'Best', attemptsN: '{n} attempts', start: 'Start', retake: 'Retake',
    inProgress: 'In progress', noHistory: 'No attempts yet.', deleteTitle: 'Delete this quiz?', deleteMessage: 'All attempts of this quiz will be deleted.'
  },
  builder: {
    title: 'Create a quiz', examTitle: 'Create a mock exam', subtitle: 'Choose what to practise. Questions that are not supported by your book are discarded.',
    noBooks: 'No books ready yet', noBooksText: 'Upload a textbook and wait for it to finish processing.', book: 'Book', wholeBook: 'Whole book',
    chapter: 'Chapter', pages: 'Page range', fromPage: 'From page', toPage: 'To page', difficulty: 'Difficulty', count: 'Number of questions',
    types: 'Question types', useBlueprint: 'Use an exam blueprint (fixed mix of question types)',
    blueprintHint: 'Match the structure of your real exam when you know it.', total: 'Total questions', timeLimit: 'Time limit (minutes)',
    timeLimitOptional: 'Time limit (minutes, 0 = none)', revealImmediately: 'Show the answer after each question', hints: 'Allow hints (−25% points)',
    focusWeak: 'Focus on my weak areas', language: 'Question language', languageAuto: 'Automatic', customTitle: 'Title (optional)',
    disclaimer: 'Practice questions are generated by AI from your book. They do not predict your real exam questions.',
    generate: 'Generate', generating: 'Writing and checking your questions…', generatingText: 'This usually takes 20–60 seconds.',
    excluded: '{n} question(s) were discarded because they could not be verified against your book.'
  },
  questionTypes: {
    multipleChoice: 'Multiple choice', trueFalse: 'True / false', fillBlank: 'Fill in the blank', shortAnswer: 'Short answer', matching: 'Matching',
    ordering: 'Ordering', numerical: 'Numerical', structured: 'Structured answer'
  },
  difficulty: { easy: 'Easy', medium: 'Medium', hard: 'Hard' },
  attempt: {
    answered: '{n} of {total} answered', saving: 'saving…', timer: 'Time', sheet: 'Answer sheet', submit: 'Submit', questionN: 'Question {n} of {total}',
    points: '{n} points', markReview: 'Mark for review', true: 'True', false: 'False', yourAnswer: 'Your answer',
    numericHint: 'Type a number. You may include the unit; decimals with . or , are accepted.', choose: 'Choose…', moveUp: 'Move up', moveDown: 'Move down',
    confirmOrder: 'Use this order', writeAnswer: 'Write your answer…', openHint: 'Written answers are marked against a model answer and rubric.',
    showHint: 'Show a hint', correct: 'Correct', incorrect: 'Incorrect', correctAnswer: 'Correct answer', check: 'Check answer', finish: 'Finish',
    submitTitle: 'Submit your answers?', submitMessage: 'You will see your results and explanations.',
    submitUnanswered: '{n} question(s) are unanswered and will count as incorrect.', timeUp: 'Time is up — your answers were submitted.',
    legendAnswered: 'Answered', legendMarked: 'Marked for review'
  },
  result: {
    score: 'Score', scoreLine: '{score} out of {max} points', correctN: '{n} correct', wrongN: '{n} incorrect', pendingN: '{n} awaiting marking',
    personalBest: 'Personal best', byTopic: 'By topic', noTopics: 'No topic data for this quiz.', nextSteps: 'What next?',
    reviewPages: 'Review these pages:', makeCards: 'Make flashcards from my mistakes', cardsCreated: 'Flashcards created', practiceWeak: 'Practise again',
    review: 'Review', mistakes: 'Mistakes', pending: 'Needs marking', yourAnswer: 'Your answer', noAnswer: '(no answer)',
    selfAssessPrompt: 'Automatic marking was not available. Compare with the model answer — were you right?', iWasRight: 'I was right', iWasWrong: 'I was wrong',
    explanation: 'Explanation'
  },
  plan: {
    title: 'Your study plan', subtitle: 'A realistic week built from your exams, availability and actual progress.', regenerate: 'Rebuild plan',
    regenerated: 'Your plan was rebuilt.', dailyAvailability: 'Daily study time',
    howItWorks: 'Tasks are prioritised by exam dates, weak topics, difficult subjects and unread chapters. Missed tasks are not piled up — rebuilding reschedules them sensibly.',
    breakReminder: 'Take a {minutes}-minute break about every {every} minutes.', today: 'Today', restDay: 'Nothing planned — a lighter day.',
    emptyTitle: 'No plan yet', emptyText: 'Upload a textbook and your plan will be built automatically.', reschedule: 'Move to…',
    start: 'Start', skip: 'Skip', markDone: 'Mark as done', markPending: 'Mark as not done',
    reasons: { cards_due: 'Cards due', weak_topic: 'Weak spot', check_understanding: 'Check understanding', exam_soon: 'Exam soon', next_chapter: 'Next up' },
    titles: {
      read: 'Read {target}', readPages: 'Read {target} (pp. {from}–{to})', cards: 'Review due flashcards', quiz: 'Quiz: {target}',
      weak: 'Revise: {topic}', exam: 'Mock exam: {target}'
    }
  },
  focus: {
    title: 'Focus mode', lead: 'One task, no distractions. Breaks are part of the plan.', pomodoro: 'Pomodoro', deep: 'Deep work', short: 'Short burst',
    focusMinutes: 'Focus (minutes)', breakMinutes: 'Break (minutes)', book: 'Book (optional)', noBook: 'No specific book', goal: 'Goal for this session',
    goalPlaceholder: 'e.g. Finish chapter 3 exercises', start: 'Start focusing', history: 'Recent sessions', session: 'Focus session', reading: 'Reading',
    wellDone: 'Well done!', summary: 'You focused for {minutes} across {cycles} cycle(s).', notes: 'Session notes (optional)',
    notesPlaceholder: 'What did you get done? What is next?', saveSession: 'Save session', saved: 'Session saved', focusing: 'Focus', breakTime: 'Break',
    timer: 'Timer', pause: 'Pause', resume: 'Resume', skipBreak: 'Skip break', stop: 'End session', progress: '{minutes} focused · {cycles} cycle(s)'
  },
  analytics: {
    title: 'Your progress', subtitle: 'Measured results from your activity — and what to do next.', emptyTitle: 'No data yet',
    emptyText: 'Read, review flashcards or take a quiz — your progress will appear here.', studyTime7d: 'Study time (7 days)', of30d: '{value} in 30 days',
    streak: 'Current streak', longest: 'Longest: {n} days', quizAccuracy: 'Quiz accuracy (30 days)', quizzesN: '{n} quizzes', retention: 'Card retention',
    reviewsN: '{n} reviews', chapters: 'Chapters', completed: 'completed', studyChart: 'Study time', last14: 'Last 14 days', quizTrend: 'Quiz scores',
    lastAttempts: 'Recent attempts', notEnoughQuizzes: 'Take at least two quizzes to see a trend.', recommendations: 'Recommendations',
    subjectProgress: 'Progress by subject', progressLegend: 'Bar: pages read · first %: reading progress · second %: quiz accuracy.',
    mastery: 'Topic mastery', concepts: 'Concepts', chaptersTab: 'Chapters',
    masteryNote: '“Measured” is your real score on questions. “Estimated mastery” combines quiz answers and flashcard reviews, weighting recent evidence — it is an estimate, not a probability of passing.',
    measured: '{c}/{n} correct', estimated: 'est. {p}%', noMastery: 'Take quizzes to see topic mastery.',
    bands: { weak: 'Weak', developing: 'Developing', strong: 'Strong', insufficient: 'Not enough evidence' }
  },
  notes: {
    pageTitle: 'Your notebook', subtitle: 'Notes, saved explanations and highlights — linked to your books.', new: 'New note', search: 'Search notes…',
    allBooks: 'All books', fromTutor: 'Tutor', empty: 'No notes here yet.', title: 'Title', titlePlaceholder: 'Title', content: 'Content',
    contentPlaceholder: 'Write your note… (Markdown and $math$ supported)', noSubject: 'No subject', noBook: 'No book', page: 'Page', tags: 'Tags (comma separated)',
    pin: 'Pin', preview: 'Preview', saved: 'Note saved', toFlashcards: 'Make flashcards', toQuiz: 'Quiz on this page', selectTitle: 'Select a note',
    selectText: 'Pick a note on the left or start a new one.', deleteTitle: 'Delete this note?', deleteMessage: 'This cannot be undone.',
    add: 'Add note', linkedToPage: 'Linked to page {page}', emptyBook: 'No notes for this book yet.', openNotebook: 'Open notebook'
  },
  search: {
    title: 'Search your books', subtitle: 'Find passages by keyword or meaning across the books you choose.', placeholder: 'What are you looking for?',
    scope: 'Search in:', allBooks: 'All my books', recent: 'Recent:', clear: 'Clear', resultsIn: '{n} results in {books} book(s)',
    noResults: 'No results', noResultsText: 'Try different words or widen the scope.',
    match: { keyword: 'Keyword match', semantic: 'Similar meaning', hybrid: 'Keyword + meaning', page: 'Page match' }
  },
  subject: {
    workspace: 'Subject workspace', read: 'Read', accuracy: 'Quiz accuracy', estimatedMastery: 'Est. mastery',
    emptyTitle: 'No {subject} books yet', emptyText: 'Upload your textbook to start studying this subject with your tutor.', nextLesson: 'Recommended next',
    startReading: 'Start reading', testFirst: 'Test me first', books: 'Books', addBook: 'Add book', chaptersDone: '{done}/{total} chapters',
    showChapters: 'Show chapters', recentQuizzes: 'Recent quizzes', noQuizzes: 'No quizzes in this subject yet.', flashcards: 'Flashcards',
    reviewCards: 'Review cards', weakTopics: 'Weak topics', noWeakTopics: 'No weak topics detected yet.', tools: 'Tools', askTutor: 'Ask the tutor', mockExam: 'Mock exam',
    focus: {
      arabic: 'Grammar, rhetoric, literature, reading comprehension and writing.', english: 'Vocabulary, grammar, reading and writing.',
      french: 'Vocabulary, grammar, conjugation, reading and writing.', physics: 'Laws, equations, units and numerical problems.',
      chemistry: 'Reactions, equations, calculations and periodic trends.', biology: 'Processes, structures, terminology and comparisons.',
      geography: 'Places, processes, definitions and cause-and-effect.'
    }
  },
  settings: {
    title: 'Settings',
    tabs: { profile: 'Profile', preferences: 'Preferences', academic: 'Academic profile', exams: 'Exams', sessions: 'Devices', privacy: 'Privacy & data' },
    changePassword: 'Change password', currentPassword: 'Current password', passwordChanged: 'Password changed',
    interfaceLanguage: 'Interface language', theme: 'Theme', themes: { system: 'Match my device', light: 'Light', dark: 'Dark' },
    teachingStyle: 'Teaching style', styles: { guided: 'Guided (step by step)', concise: 'Concise' }, weeklyGoal: 'Weekly goal (minutes)',
    bilingualTerms: 'Show original terms next to translations', motivation: 'Show streaks and milestones', notifications: 'In-app notifications',
    targetScore: 'Target score (%)', inDays: 'in {n} days', past: 'Past', sessionsLead: 'Devices currently signed in to your account.',
    thisDevice: 'This device', lastActive: 'Active {when}', signOut: 'Sign out', unknownDevice: 'Unknown device',
    privacy1: 'Your uploaded books are stored privately and are only accessible to your account. Other students can never search or see them.',
    privacy2: 'To answer questions, relevant passages from your book are sent to the configured AI provider together with your question. Your books and conversations are not used by Riwaq to train AI models.',
    privacy3: 'Your study data (progress, cards, notes, quiz results) is used only to personalise your plan and recommendations.',
    privacy4: 'You can export everything you created, and deleting your account permanently removes your books, files and study data.',
    aiProvider: 'AI provider on this server', aiModel: 'model {model}',
    aiProviders: { 'claude-account': 'Claude — the server owner’s Claude account', anthropic: 'Claude API', 'openai-compatible': 'OpenAI-compatible service', none: 'Not configured' },
    export: 'Export my data (JSON)', deleteTitle: 'Delete account', deleteLead: 'This permanently deletes your account, books, files and all study data. It cannot be undone.',
    deleteMessage: 'Everything will be permanently deleted. Are you sure?', deleteConfirm: 'Delete my account'
  },
  notifications: { empty: 'No notifications', book_ready: '“{title}” is ready to study' },
  achievements: {
    first_book: { title: 'First book', text: 'You uploaded your first textbook.' },
    first_chapter: { title: 'First chapter', text: 'You completed your first chapter.' },
    chapters_10: { title: '10 chapters', text: 'You completed ten chapters.' },
    streak_3: { title: '3-day streak', text: 'You studied three days in a row.' },
    streak_7: { title: 'Week streak', text: 'You studied seven days in a row.' },
    streak_30: { title: 'Month streak', text: 'Thirty days in a row — remarkable consistency.' },
    reviews_100: { title: '100 reviews', text: 'You completed 100 flashcard reviews.' },
    reviews_1000: { title: '1000 reviews', text: 'You completed 1000 flashcard reviews.' },
    first_quiz: { title: 'First quiz', text: 'You completed your first quiz.' },
    quiz_perfect: { title: 'Perfect score', text: 'You scored 100% on a quiz.' },
    focus_10h: { title: '10 focus hours', text: 'Ten hours in focus mode.' },
    notes_10: { title: 'Note taker', text: 'You wrote ten notes.' }
  },
  recommendations: {
    weak_topic: 'You have answered {correct} of {total} questions on “{topic}” correctly. Review page {page}, then try a few targeted questions.',
    weak_topicNoPage: 'You have answered {correct} of {total} questions on “{topic}” correctly. Revisit this topic, then try a few targeted questions.',
    weak_concept_in_strong_chapter: 'You are doing well in “{chapter}”, but you keep missing questions on “{topic}” ({correct}/{total}). Review page {page}, then practise this concept.',
    weak_concept_in_strong_chapterNoPage: 'You are doing well in “{chapter}”, but you keep missing questions on “{topic}” ({correct}/{total}). Practise this concept.',
    cards_due: '{count} flashcards are due — about {minutes} minutes of review keeps them fresh.',
    exam_upcoming: '“{examTitle}” is in {days} days ({subject}). Your plan now prioritises it.',
    quiz_read_chapter: 'You finished reading “{chapter}”. A short quiz will show what stuck.',
    upload_first_book: 'Upload your first textbook to get personalised recommendations.',
    continue_reading: 'Continue with your next chapter in {subject}.',
    keep_reviewing: 'Great work — keep reviewing to lock it in.',
    actions: { review: 'Review', reviewCards: 'Review cards', quiz: 'Quiz me', plan: 'See plan', open: 'Open' }
  },
  errors: {
    generic: 'Something went wrong. Please try again.', loadFailed: 'This page could not be loaded.',
    not_found: 'Not found — it may have been deleted.', validation_failed: 'Please check the highlighted fields.', invalid_credentials: 'Email or password is incorrect.',
    email_taken: 'An account with this email already exists.', weak_password: 'The password must be at least 8 characters and include a number.',
    account_locked: 'Too many failed attempts. Try again in a few minutes.', wrong_password: 'The password is incorrect.', invalid_reset_token: 'The reset link is invalid or has expired.',
    file_not_pdf: 'Only PDF files can be uploaded.', file_too_large: 'The file is too large.', file_empty: 'The file is empty.',
    duplicate_book: 'You have already uploaded this book.', pdf_invalid_header: 'This file is not a valid PDF.', pdf_unreadable: 'The PDF could not be read.',
    pdf_encrypted: 'The PDF is password-protected.', pdf_no_pages: 'The PDF has no pages.', pdf_too_many_pages: 'The PDF has too many pages.',
    invalid_subject: 'Choose a valid subject.', book_not_ready: 'This book is still being processed.', no_text_in_scope: 'No readable text was found in the selected pages.',
    selection_required: 'Select some text first.', selection_too_long: 'The selected text is too long.', page_required: 'Choose a page.',
    invalid_page_range: 'Choose a valid page range.', processing_in_progress: 'This book is already being processed.',
    ai_not_configured: 'The AI tutor is not configured on this server.', ai_unavailable: 'The AI service is temporarily unavailable. Please retry.',
    ai_rate_limited: 'The AI service is busy. Please wait a moment and retry.', ai_refused: 'The AI could not help with this request. Try rephrasing it.',
    ai_quota_exceeded: 'You have reached today’s AI limit. It resets at midnight (UTC).', ai_invalid_output: 'The AI response could not be used. Please retry.',
    rate_limited: 'Too many requests. Please slow down a little.', network_error: 'Network error — check your connection.', server_error: 'Server error. Please try again.',
    request_failed: 'The request failed.', unknown_error: 'Something went wrong.', attempt_submitted: 'This attempt was already submitted.',
    time_expired: 'Time is up — your answers were submitted.', answer_locked: 'This answer was already checked.', empty_question: 'Type a question first.',
    question_too_long: 'The question is too long.', empty_note: 'A note needs a title or some content.', invalid_query: 'Search for at least 2 characters.',
    unauthorized: 'Please sign in again.', missing_client_header: 'Please refresh the page.', cancelled: 'Cancelled.'
  }
};
