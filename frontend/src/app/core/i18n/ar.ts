import { EN } from './en';

/** Arabic (default) dictionary. Typed as `typeof EN` so both languages must define exactly the same keys. */
export const AR: typeof EN = {
  app: { name: 'رِواق' },
  common: {
    all: 'الكل', allSubjects: 'كل المواد', back: 'رجوع', cancel: 'إلغاء', close: 'إغلاق', copied: 'تم النسخ', copy: 'نسخ', date: 'التاريخ',
    delete: 'حذف', details: 'التفاصيل', edit: 'تعديل', export: 'تصدير', manage: 'إدارة', min: 'دقيقة', more: 'المزيد', next: 'التالي', open: 'فتح',
    pageN: 'ص {n}', pagesShort: 'صفحة', pagination: 'التنقل بين الصفحات', previous: 'السابق', remove: 'إزالة', retry: 'إعادة المحاولة', save: 'حفظ',
    saved: 'تم الحفظ', search: 'بحث', subject: 'المادة', tryAgain: 'حاول مجددًا',
    minutesShort: '{n} د', hoursShort: '{n} س', hoursMinutesShort: '{h} س {m} د'
  },
  nav: {
    account: 'الحساب', analytics: 'التقدّم', flashcards: 'البطاقات', focus: 'وضع التركيز', home: 'الرئيسية', library: 'المكتبة', logout: 'تسجيل الخروج',
    mainNavigation: 'التنقل الرئيسي', notes: 'الدفتر', notifications: 'الإشعارات', openMenu: 'فتح القائمة', plan: 'خطة المذاكرة',
    practice: 'التدريب', quizzes: 'الاختبارات', searchPlaceholder: 'ابحث في كتبك…', settings: 'الإعدادات', skipToContent: 'تخطَّ إلى المحتوى',
    subjects: 'المواد', summaries: 'الملخصات', switchLanguage: 'تغيير اللغة', toggleTheme: 'الوضع الداكن', tutor: 'المعلّم الذكي', uploadBook: 'رفع كتاب'
  },
  auth: {
    heroTitle: 'كل كتاب مدرسي يصبح معلّمك الخاص.',
    heroText: 'ارفع كتبك وذاكرها مع شروح وبطاقات مراجعة واختبارات وخطة مبنية حول مواعيد امتحاناتك.',
    point1: 'اسأل عن أي فقرة واحصل على إجابة موثّقة برقم الصفحة',
    point2: 'تذكّر أكثر مع بطاقات التكرار المتباعد',
    point3: 'اعرف نقاط ضعفك قبل أن يكشفها الامتحان',
    privacyNote: 'كتبك وبيانات مذاكرتك خاصة بحسابك وحدك.',
    loginTitle: 'أهلًا بعودتك', loginLead: 'سجّل الدخول لمتابعة المذاكرة.', email: 'البريد الإلكتروني', password: 'كلمة المرور', emailInvalid: 'أدخل بريدًا إلكترونيًا صحيحًا.',
    showPassword: 'إظهار كلمة المرور', hidePassword: 'إخفاء كلمة المرور', forgot: 'نسيت كلمة المرور؟', login: 'تسجيل الدخول', noAccount: 'جديد على رِواق؟',
    createAccount: 'إنشاء حساب', registerTitle: 'أنشئ حسابك', registerLead: 'لن يستغرق الأمر أكثر من دقيقة.', displayName: 'اسمك',
    passwordHint: '8 أحرف على الأقل، منها رقم واحد.', haveAccount: 'لديك حساب بالفعل؟',
    forgotTitle: 'استعادة كلمة المرور', forgotLead: 'أدخل بريدك وسنرسل لك رابطًا لتعيين كلمة مرور جديدة.',
    forgotSent: 'إذا كان هناك حساب بهذا البريد فسيصلك رابط الاستعادة قريبًا.', sendResetLink: 'إرسال الرابط', backToLogin: 'العودة لتسجيل الدخول',
    resetTitle: 'اختر كلمة مرور جديدة', newPassword: 'كلمة المرور الجديدة', resetPassword: 'تعيين كلمة المرور', resetDone: 'تم تغيير كلمة المرور. يمكنك تسجيل الدخول الآن.'
  },
  onboarding: {
    skip: 'تخطَّ الآن', stepOf: 'الخطوة {n} من {total}', optional: 'اختياري — يمكنك تغييره لاحقًا من الإعدادات.',
    gradeQ: 'في أي صف أو سنة دراسية أنت؟', grade: 'الصف', gradePlaceholder: 'مثال: الصف الثالث الثانوي', academicYear: 'العام الدراسي',
    curriculumQ: 'ما المنهج الذي تدرسه؟', curriculumHint: 'رِواق يتكيّف مع الكتب التي ترفعها — هذا يساعدنا فقط في التخطيط.',
    curriculumOther: 'المنهج', curricula: { national: 'المنهج الوطني', igcse: 'IGCSE', american: 'الدبلومة الأمريكية', ib: 'البكالوريا الدولية IB', french: 'البكالوريا الفرنسية', other: 'منهج آخر' },
    examsQ: 'متى امتحاناتك القادمة؟', examsHint: 'خطتك تعطي الأولوية للمواد التي اقترب امتحانها.', examTitle: 'اسم الامتحان', allSubjects: 'عدة مواد', addExam: 'إضافة امتحان',
    timeQ: 'كم من الوقت يمكنك المذاكرة يوميًا؟', timeHint: 'لن تتجاوز الخطة هذا الوقت أبدًا.',
    difficultQ: 'ما المواد التي تجدها الأصعب؟', difficultHint: 'ستحصل على وقت أكثر قليلًا في خطتك.',
    goalQ: 'ما الدرجة التي تطمح إليها؟', explainLanguage: 'لغة الشرح', explainHint: 'يشرح المعلّم بهذه اللغة أيًّا كانت لغة الكتاب.',
    finish: 'ابدأ المذاكرة'
  },
  dashboard: {
    morning: 'صباح الخير يا {name}', afternoon: 'مساء الخير يا {name}', evening: 'مساء النور يا {name}',
    leadNoBooks: 'ارفع أول كتاب مدرسي لتحوّله إلى مساحة مذاكرة متكاملة.',
    leadPlan: 'اليوم: أنجزت {done} من {total} مهام · المخطط نحو {minutes}.',
    leadFree: 'لا شيء مجدول الآن — راجع بعض البطاقات أو تابع القراءة.',
    streakDays: 'أيام متتالية', weeklyGoal: 'الهدف الأسبوعي', thisWeek: 'هذا الأسبوع من {goal}', daysLeft: 'يومًا متبقيًا',
    aiNotConfigured: 'لم يُفعَّل المعلّم الذكي على هذا الخادم بعد. القراءة والبطاقات والملاحظات والخطة تعمل بشكل طبيعي.',
    processing: 'جارٍ تجهيز كتاب', welcome1: 'ارفع كتابًا مدرسيًا', welcome1Text: 'أي ملف PDF — بالعربية أو الإنجليزية أو الفرنسية.',
    welcome2: 'ذاكر مع معلّمك', welcome2Text: 'ظلّل النص واسأل، والإجابات موثّقة بصفحات كتابك.',
    welcome3: 'تدرّب وراجع', welcome3Text: 'بطاقات واختبارات وخطة تتكيّف معك.', uploadFirst: 'ارفع أول كتاب',
    todayPlan: 'خطة اليوم', fullPlan: 'الخطة كاملة', noTasks: 'لا مهام لليوم. أنشئ خطة أو خذ استراحة مستحقة.',
    continue: 'تابع المذاكرة', subjects: 'موادك', subjectMeta: '{books} كتاب · {chapters}/{total} فصول', cardsDueN: '{n} بطاقة مستحقة',
    noBooksYet: 'لا كتب بعد', recommendations: 'مقترحات لك', noRecommendations: 'واصل المذاكرة — ستظهر المقترحات مع تدرّبك.',
    recommendationsBasis: 'مبنية فقط على نشاطك المسجّل وكتبك.', cardsDue: 'بطاقة مستحقة للمراجعة', reviewNow: 'راجع الآن',
    weakTopics: 'تحتاج إلى اهتمام', correctOf: '{c}/{n} صحيحة', suggestedQuizzes: 'تحقّق من فهمك', testMe: 'اختبرني',
    exams: 'الامتحانات القادمة', daysShort: 'يوم', noExams: 'لا امتحانات قادمة. أضفها لتحصل على خطة أدق.', achievements: 'محطات الإنجاز'
  },
  library: {
    title: 'كتبك المدرسية', subtitle: 'ارفع كتبك ونظّمها وأدرها من مكان واحد.', allYears: 'كل الأعوام', filter: 'تصفية الكتب…',
    emptyTitle: 'مكتبتك فارغة', emptyText: 'ارفع كتابًا بصيغة PDF، وسيقرؤه رِواق ويستخرج فصوله ويجهّزه للمذاكرة.',
    failed: 'فشلت المعالجة', lastPage: 'آخر قراءة: ص {page}', warnings: 'ملاحظات', open: 'فتح', manage: 'إدارة', noMatches: 'لا توجد كتب مطابقة لهذه التصفية.',
    deleteTitle: 'حذف هذا الكتاب؟', deleteMessage: 'سيُحذف «{title}» وكل ما اشتُق منه (الصفحات والتظليلات والمحادثات والملخصات). أما البطاقات وسجل الاختبارات فتبقى.',
    deleted: 'تم حذف الكتاب'
  },
  upload: {
    title: 'رفع كتاب مدرسي', subtitle: 'ملفات PDF فقط. تستمر المعالجة في الخلفية.', drop: 'أفلت ملف PDF هنا أو انقر للاختيار',
    limits: 'PDF · حتى {mb} ميغابايت', change: 'انقر للتغيير', chooseSubject: 'اختر المادة', bookTitle: 'عنوان الكتاب', academicYear: 'العام الدراسي',
    tags: 'وسوم', tagsPlaceholder: 'مثال: الفصل الأول، مراجعة', notPdf: 'هذا الملف ليس PDF.', tooLarge: 'حجم الملف أكبر من {mb} ميغابايت.',
    start: 'رفع', uploading: 'جارٍ الرفع…', cancelUpload: 'إلغاء الرفع', processingLead: 'يجري تجهيز كتابك. يمكنك إغلاق هذه النافذة وسنواصل العمل.',
    ready: 'كتابك جاهز للمذاكرة.', readyWithWarnings: 'كتابك جاهز، لكن بعض الصفحات واجهت مشكلات — اطّلع على التفاصيل.',
    viewDetails: 'عرض التفاصيل', startStudying: 'ابدأ المذاكرة', continueInBackground: 'المتابعة في الخلفية', openExisting: 'فتح الكتاب الموجود'
  },
  processing: {
    timeline: 'مراحل المعالجة',
    stages: {
      uploading: 'تم الرفع', queued: 'في الانتظار', validating: 'فحص المستند', extractingText: 'استخراج النص',
      recognizingScannedPages: 'قراءة الصفحات الممسوحة ضوئيًا', detectingStructure: 'اكتشاف الفصول والعناوين', processingTablesAndFigures: 'فحص الجداول والأشكال',
      preparingSearch: 'تجهيز البحث والمعلّم', generatingResources: 'توليد المواد', completed: 'جاهز للمذاكرة'
    }
  },
  processingErrors: {
    pdf_invalid_header: 'هذا الملف ليس PDF صالحًا.', pdf_unreadable: 'تعذّرت قراءة ملف PDF، وقد يكون تالفًا.',
    pdf_encrypted: 'الملف محمي بكلمة مرور. أزل كلمة المرور ثم ارفعه مجددًا.', pdf_no_pages: 'الملف لا يحتوي على صفحات.',
    pdf_too_many_pages: 'عدد صفحات الملف أكبر من المسموح.', processing_error: 'حدث خطأ أثناء المعالجة. حاول مرة أخرى.',
    ai_unavailable: 'خدمة البحث لم تكن متاحة. حاول مرة أخرى.', ai_rate_limited: 'خدمة الذكاء الاصطناعي مشغولة. حاول بعد قليل.',
    ai_not_configured: 'مزوّد التضمينات غير مُعدّ بشكل صحيح.'
  },
  warnings: {
    scanned_page_no_ocr: 'هذه الصفحة ممسوحة ضوئيًا (صورة) والتعرّف الضوئي على النصوص غير متاح على الخادم، لذلك لم يُقرأ نصها.',
    ocr_low_confidence: 'النص المستخرج من هذه الصفحة الممسوحة قد يحتوي على أخطاء.', ocr_failed: 'تعذّر التعرّف على نص هذه الصفحة الممسوحة.',
    no_structure_detected: 'لم يُعثر على تقسيم موثوق للفصول. ما زال بإمكانك التنقل بالصفحات.',
    structure_estimated: 'قُدّرت الفصول من شكل العناوين — راجع العناوين وصحّحها إن لزم.',
    figures_not_interpreted: 'بعض الصفحات تحتوي على أشكال أو رسوم توضيحية. لا يُفسَّر محتواها البصري، ويمكن البحث في النص المحيط بها فقط.',
    tables_flattened: 'اكتُشفت جداول؛ حُفظ نصها لكن قد تندمج الصفوف والأعمدة.',
    embeddings_unavailable: 'تعذّر إنشاء متجهات البحث الدلالي؛ البحث بالكلمات ما زال يعمل. أعد المعالجة لاحقًا.',
    no_text_extracted: 'لم يُستخرج أي نص مقروء، لذلك لا يستطيع المعلّم والبحث استخدام هذا الكتاب بعد.'
  },
  book: {
    read: 'قراءة', quiz: 'إنشاء اختبار', processing: 'المعالجة', details: 'بيانات الكتاب', semester: 'الفصل الدراسي', collection: 'المجموعة',
    structure: 'الفصول', estimated: 'تقديري', fromOutline: 'من فهرس الملف', noStructure: 'لم تُكتشف فصول لهذا الكتاب.',
    structurePending: 'تظهر الفصول بعد انتهاء المعالجة.', chapterTitle: 'عنوان الفصل', pages: 'ص {from}–{to}', chapterStatus: 'حالة الفصل',
    renameChapter: 'إعادة تسمية الفصل', extraction: 'استخراج النص', textPages: 'صفحات بها نص', scannedPages: 'صفحات ممسوحة', ocrPages: 'قُرئت بالتعرّف الضوئي',
    language: 'اللغة المكتشفة', warnings: 'ملاحظات المعالجة', noWarnings: 'لم تُرصد أي مشكلات.', moreWarnings: '+{n} ملاحظات أخرى',
    reprocess: 'إعادة معالجة الكتاب', reprocessTitle: 'إعادة معالجة هذا الكتاب؟', reprocessMessage: 'سيُستخرج النص من جديد. تظليلاتك وملاحظاتك محفوظة، وقد تُعاد حالة تقدّم الفصول.',
    delete: 'حذف الكتاب'
  },
  chapterStatus: { notStarted: 'لم يبدأ', inProgress: 'قيد الدراسة', completed: 'مكتمل', alreadyKnown: 'أعرفه مسبقًا' },
  languages: { ar: 'العربية', en: 'الإنجليزية', fr: 'الفرنسية', mixed: 'متعدد اللغات' },
  extraction: { none: 'بلا نص', embeddedText: 'نص مضمّن', ocr: 'تعرّف ضوئي' },
  reader: {
    toolbar: 'شريط أدوات القارئ', toggleContents: 'إظهار الفهرس', previousPage: 'الصفحة السابقة', nextPage: 'الصفحة التالية', goToPage: 'انتقل إلى صفحة',
    zoomIn: 'تكبير', zoomOut: 'تصغير', fit: 'الملاءمة', fitWidth: 'العرض', fitPage: 'الصفحة', bookmark: 'إضافة إشارة مرجعية', nightMode: 'القراءة الليلية',
    focusMode: 'وضع بلا مشتتات', fullscreen: 'ملء الشاشة', tutor: 'المعلّم', notes: 'ملاحظات', tools: 'أدوات', contents: 'الفهرس',
    studyPanel: 'لوحة المذاكرة', loading: 'جارٍ فتح كتابك…', loadFailed: 'تعذّر فتح الكتاب.',
    scannedNotice: 'هذه الصفحة صورة ممسوحة بلا نص مقروء، لذا لا يمكن تحديد النص هنا.', askAboutPage: 'اسأل عن هذه الصفحة',
    selectionActions: 'إجراءات على النص المحدد', highlight: 'تظليل', annotation: 'تظليل وملاحظة', annotationPlaceholder: 'أضف ملاحظتك الشخصية…',
    actions: { explain: 'اشرح', simplify: 'بسّط', summarize: 'لخّص', translate: 'ترجم', example: 'مثال', steps: 'خطوة بخطوة', questions: 'أسئلة', flashcard: 'بطاقة' },
    toc: 'الفصول', pages: 'الصفحات', bookmarks: 'الإشارات', noToc: 'لم تُكتشف فصول.', noTocHint: 'استخدم التنقل بالصفحات أو البحث.',
    noBookmarks: 'لا إشارات مرجعية بعد. اضغط B أو زر الإشارة لحفظ صفحة.', searchBook: 'ابحث في هذا الكتاب…', noResults: 'لا نتائج مطابقة.'
  },
  tutor: {
    newConversation: 'محادثة جديدة', newChat: 'محادثة جديدة', untitled: 'محادثة بلا عنوان', rename: 'إعادة تسمية', deleteTitle: 'حذف هذه المحادثة؟',
    deleteMessage: 'ستُحذف المحادثة نهائيًا.',
    aiNotConfigured: 'المعلّم الذكي غير مُفعّل على هذا الخادم. يحتاج المسؤول إلى إضافة مفتاح مزوّد الذكاء الاصطناعي أو تسجيل دخول الخادم إلى حساب Claude.',
    introTitle: 'معلّمك لهذا الكتاب', introReader: 'ظلّل فقرة أو اسأل عن أي صفحة، وستُوثَّق الإجابات بأرقام الصفحات التي اعتمدت عليها.',
    introGeneral: 'اسأل عن أي شيء في كتب هذه المادة، وستُظهر كل إجابة الصفحات التي أُخذت منها.',
    quick: { explainPage: 'اشرح الصفحة {page}', summarizeSection: 'لخّص هذا القسم', testMe: 'اختبر فهمي', terms: 'اشرح المصطلحات الأساسية' },
    actions: {
      explain: 'اشرح', simplify: 'بسّط', summarize: 'لخّص', translate: 'ترجم', example: 'مثال', steps: 'خطوة بخطوة', questions: 'أسئلة تدريبية',
      deeper: 'تعمّق أكثر', analogy: 'تشبيه', terminology: 'المصطلحات', check: 'اختبر فهمي', explain_page: 'اشرح هذه الصفحة', summarize_section: 'لخّص القسم'
    },
    notGrounded: 'لم يُعثر على فقرة مطابقة في كتابك — قد تعتمد هذه الإجابة على معلومات عامة.',
    stopped: 'أُوقفت', sources: 'المصادر', helpful: 'مفيدة', notHelpful: 'غير مفيدة', saveToNotes: 'حفظ في الدفتر', savedToNotes: 'حُفظت في دفترك',
    regenerate: 'إعادة التوليد', thinking: 'يفكّر…', contextPage: 'السياق: الصفحة {page}', placeholder: 'اسأل معلّمك…', stop: 'إيقاف', send: 'إرسال',
    answerIn: 'لغة الإجابة', disclaimer: 'قد يخطئ الذكاء الاصطناعي — تحقّق من المعلومات المهمة في كتابك.',
    pageTitle: 'اسأل معلّمك', pageSubtitle: 'شروح مستندة إلى كتبك المدرسية أنت.', noBooksTitle: 'ارفع كتابًا أولًا',
    noBooksText: 'يجيب المعلّم من كتبك، لذا يحتاج إلى كتاب واحد على الأقل مكتمل المعالجة.', scope: 'أين يبحث المعلّم؟',
    allBooks: 'كل كتب هذه المادة', allBooksHint: 'يبحث في {n} كتاب ويوثّق كل مصدر على حدة', scopeNote: 'لا يبحث المعلّم أبدًا في كتب الطلاب الآخرين.'
  },
  tools: {
    summary: 'ملخص', flashcards: 'بطاقات', quiz: 'اختبار', pageText: 'نص الصفحة', scope: 'النطاق', style: 'النمط',
    scopes: { selection: 'النص المحدد', page: 'الصفحة {page}', chapter: 'هذا الفصل', book: 'الكتاب كاملًا' },
    generating: 'جارٍ التوليد…', generateSummary: 'توليد الملخص', noChapter: 'لا يوجد فصل يغطي هذه الصفحة.', selectFirst: 'حدّد نصًا في الكتاب أولًا.',
    summaryDisclaimer: 'مُولَّد بالذكاء الاصطناعي من كتابك، وقد لا يكون كاملًا — وليس ملخصًا رسميًا للمنهج.',
    manualCard: 'إنشاء بطاقة', front: 'السؤال / التلميح', back: 'الإجابة', saveCard: 'حفظ البطاقة', cardSaved: 'حُفظت البطاقة', aiCards: 'توليد بطاقات بالذكاء الاصطناعي',
    focus: 'التركيز', focuses: { general: 'الأفكار الرئيسية', definitions: 'التعريفات', formulas: 'القوانين', vocabulary: 'المفردات' },
    generateCards: 'توليد البطاقات', reviewDrafts: 'راجع المسودات — ألغِ تحديد ما لا تريده.', saveSelected: 'حفظ البطاقات المحددة',
    cardsSaved: 'حُفظت {n} بطاقة', noDrafts: 'تعذّر إنشاء بطاقات جديدة من هذا المحتوى.',
    quizLead: 'اختبر نفسك فيما تقرؤه. تُراجَع الأسئلة مقابل الكتاب قبل أن تراها.',
    quizChapter: 'اختبار على هذا الفصل', quizPages: 'اختبار على الصفحات {from}–{to}', noPageText: 'لم يُستخرج أي نص من هذه الصفحة.',
    periodicTable: 'الجدول الدوري', balancer: 'موازنة المعادلات', units: 'الوحدات والتحويلات', languagePractice: 'تدريب اللغة',
    titles: { 'periodic-table': 'الجدول الدوري', 'equation-balancer': 'موازنة المعادلات الكيميائية', units: 'الوحدات والتحويلات', practice: 'تدريب اللغة' },
    leads: {
      'periodic-table': 'العناصر الـ118 بكتلها الذرية، مع حاسبة للكتلة المولية.',
      'equation-balancer': 'جرّب الموازنة بنفسك أولًا ثم تحقّق — أو اعرض المعادلة الموزونة.',
      units: 'وحدات النظام الدولي وتحويلات سريعة للفيزياء والكيمياء.', practice: 'تدرّب بتمارين من كتب اللغات الخاصة بك.'
    },
    categories: {
      alkali: 'فلز قلوي', alkaline: 'فلز قلوي ترابي', transition: 'فلز انتقالي', post: 'فلز بعد انتقالي', metalloid: 'شبه فلز', nonmetal: 'لا فلز',
      halogen: 'هالوجين', noble: 'غاز نبيل', lanthanide: 'لانثانيد', actinide: 'أكتينيد', unknown: 'خواص غير معروفة'
    },
    atomicMass: 'الكتلة الذرية', period: 'الدورة', group: 'المجموعة', molarMass: 'حاسبة الكتلة المولية', invalidFormula: 'اكتب صيغة صحيحة، مثل Ca(OH)2.',
    equation: 'المعادلة', equationHint: 'استخدم -> بين الطرفين و + بين المواد، مثل C3H8 + O2 -> CO2 + H2O',
    yourCoefficients: 'معاملاتك (بالترتيب ومفصولة بفواصل)', check: 'تحقّق', attemptRight: 'صحيح — المعادلة موزونة!',
    attemptWrong: 'لم تُوزن بعد. عُدّ ذرات كل عنصر في الطرفين وحاول مجددًا.', showBalanced: 'اعرض المعادلة الموزونة',
    balanceErrors: {
      no_arrow: 'افصل المتفاعلات عن النواتج بالرمز ->.', impossible: 'لا يمكن موازنة هذه المعادلة — راجع الصيغ.',
      ambiguous: 'لهذه المعادلة أكثر من موازنة مستقلة (قد تجمع تفاعلين).', element: 'رمز عنصر غير معروف: {el}',
      unbalanced: 'أقواس غير متوازنة في إحدى الصيغ.', invalid: 'تعذّرت قراءة المعادلة.'
    },
    converter: 'المحوّل', amount: 'القيمة', from: 'من', to: 'إلى', temperature: 'درجة الحرارة', siBase: 'الوحدات الأساسية في النظام الدولي', derived: 'وحدات مشتقة شائعة',
    quantities: {
      length: 'الطول', mass: 'الكتلة', time: 'الزمن', energy: 'الطاقة', pressure: 'الضغط', volume: 'الحجم', current: 'شدة التيار', temperature: 'درجة الحرارة',
      amount: 'كمية المادة', luminous: 'شدة الإضاءة', force: 'القوة', power: 'القدرة', charge: 'الشحنة', voltage: 'فرق الجهد', resistance: 'المقاومة', frequency: 'التردد'
    },
    needLanguageBook: 'ارفع كتابًا للغة العربية أو الإنجليزية أو الفرنسية لتتدرّب هنا.', practiceExercises: 'تمارين من كتابك',
    grammarVocab: 'اختبار قواعد ومفردات', comprehension: 'فهم المقروء', translationPractice: 'تدريب الترجمة مع المعلّم',
    practiceNote: 'تُولَّد التمارين من كتابك وتُراجَع قبل أن تراها.', writingFeedback: 'ملاحظات على الكتابة',
    writingPlaceholder: 'اكتب فقرة باللغة التي تدرسها…', getFeedback: 'احصل على الملاحظات', writingFeedbackTitle: 'ملاحظات على الكتابة',
    writingPrompt: 'من فضلك صحّح كتابتي أدناه. اذكر كل خطأ مع الصواب والقاعدة، ثم اكتب نسخة محسّنة ونصيحة واحدة.',
    unknown: 'هذه الأداة غير موجودة.'
  },
  summaryStyles: {
    quick: 'ملخص سريع', simple: 'شرح مبسط', detailed: 'ملخص تفصيلي', keyPoints: 'أهم النقاط', definitions: 'التعريفات والمصطلحات',
    formulas: 'القوانين والمعادلات', comparisons: 'المقارنات', causesEffects: 'الأسباب والنتائج', examQuestions: 'أسئلة امتحانات', revisionSheet: 'ورقة مراجعة'
  },
  summaryWarnings: {
    pages_without_text: 'الصفحات {pages} لا تحتوي على نص مقروء ولم تُضمَّن.',
    ocr_pages_included: 'الصفحات {pages} قُرئت بالتعرّف الضوئي وقد تحتوي على أخطاء.',
    source_truncated: 'المحتوى أطول من الحد المسموح؛ هذا الملخص يغطي حتى الصفحة {page}.'
  },
  summaries: {
    title: 'الملخصات وأدلة المذاكرة', subtitle: 'كل ما ولّدته من كتبك — قابل للتعديل والتصدير.', edited: 'معدَّل',
    emptyTitle: 'لا ملخصات بعد', emptyText: 'افتح كتابًا واستخدم الأدوات ← ملخص لإنشاء ملخص لصفحة أو فصل أو الكتاب كاملًا.',
    print: 'طباعة / PDF', content: 'محتوى الملخص', editHint: 'يدعم Markdown. مراجع الصفحات تُكتب هكذا [p:12].', next: 'تابع',
    toCards: 'حوّله إلى بطاقات', toQuiz: 'اختبرني فيه', regenerate: 'إعادة التوليد', deleteTitle: 'حذف هذا الملخص؟', deleteMessage: 'لا يمكن التراجع عن ذلك.'
  },
  flashcards: {
    title: 'البطاقات التعليمية', subtitle: 'التكرار المتباعد يعرض كل بطاقة قبل أن تنساها مباشرة.', newCard: 'بطاقة جديدة', editCard: 'تعديل البطاقة',
    startReview: 'ابدأ المراجعة', due: 'مستحقة', new: 'جديدة', learning: 'قيد التعلّم', mastered: 'متقنة', retention: 'نسبة التذكّر (30 يومًا)', streak: 'أيام المراجعة المتتالية',
    forecast: 'المراجعات في الأيام السبعة القادمة', algorithmNote: 'تعتمد الجدولة على خوارزمية FSRS-5. لا تُعدّ البطاقة متقنة إلا بعد مراجعات ناجحة ترفع ثبات تذكّرها إلى أكثر من 21 يومًا.',
    mathHint: 'يمكنك كتابة المعادلات بين $...$.', filterState: 'التصفية حسب الحالة', allStates: 'كل البطاقات', suspended: 'موقوفة',
    emptyTitle: 'لا بطاقات بعد', emptyText: 'أنشئ بطاقات بنفسك، أو من نص مظلّل، أو دع الذكاء الاصطناعي يقترحها من فصل.',
    dueNow: 'مستحقة الآن', suspend: 'إيقاف', resume: 'استئناف', deleteTitle: 'حذف هذه البطاقة؟', deleteMessage: 'سيُحذف سجل مراجعتها أيضًا.',
    states: { new: 'جديدة', learning: 'قيد التعلّم', review: 'مراجعة', relearning: 'إعادة تعلّم' }
  },
  review: {
    nothingDue: 'أنجزت كل شيء!', nothingDueText: 'لا بطاقات مستحقة الآن. عد لاحقًا أو أضف بطاقات جديدة.', sessionDone: 'اكتملت الجلسة',
    reviewed: 'راجعت', forgotten: 'نسيت', time: 'الوقت', doneText: 'البطاقات التي نسيتها ستعود قريبًا. المراجعة المنتظمة أفضل من الحفظ المكثف.',
    again: 'مراجعة المزيد', tapToReveal: 'انقر أو اضغط مسافة لإظهار الإجابة', showAnswer: 'أظهر الإجابة', howWell: 'إلى أي حد تذكّرت؟',
    ratings: { again: 'مجددًا', hard: 'صعبة', good: 'جيدة', easy: 'سهلة' },
    minutes: '{n} د', hours: '{n} س', days: '{n} يوم', months: '{n} شهر', years: '{n} سنة'
  },
  quizzes: {
    title: 'الاختبارات والامتحانات التجريبية', subtitle: 'تدرّب بأسئلة مولّدة من كتبك ومراجَعة مقابلها.', newQuiz: 'اختبار جديد', newExam: 'امتحان تجريبي',
    myQuizzes: 'اختباراتي', history: 'السجل', emptyTitle: 'لا اختبارات بعد', emptyText: 'أنشئ اختبارًا على فصل أو نطاق صفحات أو كتاب كامل.',
    modes: { practice: 'تدريب', exam: 'امتحان' }, questionsN: '{n} سؤال', best: 'الأفضل', attemptsN: '{n} محاولات', start: 'ابدأ', retake: 'أعد المحاولة',
    inProgress: 'قيد الحل', noHistory: 'لا محاولات بعد.', deleteTitle: 'حذف هذا الاختبار؟', deleteMessage: 'ستُحذف كل محاولات هذا الاختبار.'
  },
  builder: {
    title: 'إنشاء اختبار', examTitle: 'إنشاء امتحان تجريبي', subtitle: 'اختر ما تريد التدرّب عليه. تُستبعد الأسئلة غير المدعومة بكتابك.',
    noBooks: 'لا كتب جاهزة بعد', noBooksText: 'ارفع كتابًا وانتظر حتى تكتمل معالجته.', book: 'الكتاب', wholeBook: 'الكتاب كاملًا',
    chapter: 'فصل', pages: 'نطاق صفحات', fromPage: 'من الصفحة', toPage: 'إلى الصفحة', difficulty: 'الصعوبة', count: 'عدد الأسئلة',
    types: 'أنواع الأسئلة', useBlueprint: 'استخدام مخطط امتحان (توزيع ثابت لأنواع الأسئلة)',
    blueprintHint: 'طابق هيكل امتحانك الحقيقي إن كنت تعرفه.', total: 'إجمالي الأسئلة', timeLimit: 'الزمن (بالدقائق)',
    timeLimitOptional: 'الزمن (بالدقائق، 0 = بلا حد)', revealImmediately: 'إظهار الإجابة بعد كل سؤال', hints: 'السماح بالتلميحات (−25% من الدرجة)',
    focusWeak: 'التركيز على نقاط ضعفي', language: 'لغة الأسئلة', languageAuto: 'تلقائي', customTitle: 'العنوان (اختياري)',
    disclaimer: 'أسئلة التدريب مولّدة بالذكاء الاصطناعي من كتابك، ولا تتنبأ بأسئلة امتحانك الحقيقي.',
    generate: 'توليد', generating: 'جارٍ كتابة أسئلتك ومراجعتها…', generatingText: 'يستغرق ذلك عادةً من 20 إلى 60 ثانية.',
    excluded: 'استُبعد {n} سؤال لتعذّر التحقق منه مقابل كتابك.'
  },
  questionTypes: {
    multipleChoice: 'اختيار من متعدد', trueFalse: 'صح / خطأ', fillBlank: 'أكمل الفراغ', shortAnswer: 'إجابة قصيرة', matching: 'توصيل',
    ordering: 'ترتيب', numerical: 'مسألة عددية', structured: 'سؤال مقالي'
  },
  difficulty: { easy: 'سهل', medium: 'متوسط', hard: 'صعب' },
  attempt: {
    answered: 'أجبت عن {n} من {total}', saving: 'جارٍ الحفظ…', timer: 'الوقت', sheet: 'ورقة الإجابات', submit: 'تسليم', questionN: 'السؤال {n} من {total}',
    points: '{n} درجات', markReview: 'تعليم للمراجعة', true: 'صح', false: 'خطأ', yourAnswer: 'إجابتك',
    numericHint: 'اكتب رقمًا، ويمكنك إضافة الوحدة. تُقبل الكسور العشرية بالنقطة أو الفاصلة.', choose: 'اختر…', moveUp: 'تحريك لأعلى', moveDown: 'تحريك لأسفل',
    confirmOrder: 'اعتمد هذا الترتيب', writeAnswer: 'اكتب إجابتك…', openHint: 'تُصحَّح الإجابات المكتوبة مقابل إجابة نموذجية ومعايير تصحيح.',
    showHint: 'أظهر تلميحًا', correct: 'صحيحة', incorrect: 'خاطئة', correctAnswer: 'الإجابة الصحيحة', check: 'تحقّق من الإجابة', finish: 'إنهاء',
    submitTitle: 'تسليم إجاباتك؟', submitMessage: 'ستظهر لك النتيجة مع الشروح.',
    submitUnanswered: 'يوجد {n} سؤال بلا إجابة وسيُحسب خطأ.', timeUp: 'انتهى الوقت — سُلّمت إجاباتك.',
    legendAnswered: 'تمت الإجابة', legendMarked: 'معلَّم للمراجعة'
  },
  result: {
    score: 'الدرجة', scoreLine: '{score} من {max} درجة', correctN: '{n} صحيحة', wrongN: '{n} خاطئة', pendingN: '{n} بانتظار التصحيح',
    personalBest: 'أفضل نتيجة لك', byTopic: 'حسب الموضوع', noTopics: 'لا بيانات مواضيع لهذا الاختبار.', nextSteps: 'ماذا بعد؟',
    reviewPages: 'راجع هذه الصفحات:', makeCards: 'أنشئ بطاقات من أخطائي', cardsCreated: 'أُنشئت البطاقات', practiceWeak: 'تدرّب مجددًا',
    review: 'المراجعة', mistakes: 'الأخطاء', pending: 'تحتاج تصحيحًا', yourAnswer: 'إجابتك', noAnswer: '(بلا إجابة)',
    selfAssessPrompt: 'لم يتوفر التصحيح الآلي. قارن بالإجابة النموذجية — هل كانت إجابتك صحيحة؟', iWasRight: 'كانت صحيحة', iWasWrong: 'كانت خاطئة',
    explanation: 'الشرح'
  },
  plan: {
    title: 'خطة مذاكرتك', subtitle: 'أسبوع واقعي مبني على امتحاناتك ووقتك المتاح وتقدّمك الفعلي.', regenerate: 'إعادة بناء الخطة',
    regenerated: 'أُعيد بناء خطتك.', dailyAvailability: 'وقت المذاكرة اليومي',
    howItWorks: 'تُرتَّب المهام حسب مواعيد الامتحانات والمواضيع الضعيفة والمواد الصعبة والفصول غير المقروءة. لا تتراكم المهام الفائتة — إعادة البناء توزّعها بشكل معقول.',
    breakReminder: 'خذ استراحة {minutes} دقائق كل {every} دقيقة تقريبًا.', today: 'اليوم', restDay: 'لا شيء مخطط — يوم أخف.',
    emptyTitle: 'لا خطة بعد', emptyText: 'ارفع كتابًا مدرسيًا وستُبنى خطتك تلقائيًا.', reschedule: 'نقل إلى…',
    start: 'ابدأ', skip: 'تخطَّ', markDone: 'تعليم كمنجزة', markPending: 'تعليم كغير منجزة',
    reasons: { cards_due: 'بطاقات مستحقة', weak_topic: 'نقطة ضعف', check_understanding: 'تحقّق من الفهم', exam_soon: 'امتحان قريب', next_chapter: 'التالي' },
    titles: {
      read: 'اقرأ {target}', readPages: 'اقرأ {target} (ص {from}–{to})', cards: 'راجع البطاقات المستحقة', quiz: 'اختبار: {target}',
      weak: 'راجع: {topic}', exam: 'امتحان تجريبي: {target}'
    }
  },
  focus: {
    title: 'وضع التركيز', lead: 'مهمة واحدة بلا مشتتات، والاستراحات جزء من الخطة.', pomodoro: 'بومودورو', deep: 'تركيز عميق', short: 'جلسة قصيرة',
    focusMinutes: 'التركيز (بالدقائق)', breakMinutes: 'الاستراحة (بالدقائق)', book: 'الكتاب (اختياري)', noBook: 'بلا كتاب محدد', goal: 'هدف هذه الجلسة',
    goalPlaceholder: 'مثال: إنهاء تمارين الفصل الثالث', start: 'ابدأ التركيز', history: 'الجلسات الأخيرة', session: 'جلسة تركيز', reading: 'قراءة',
    wellDone: 'أحسنت!', summary: 'ركّزت لمدة {minutes} عبر {cycles} دورة.', notes: 'ملاحظات الجلسة (اختياري)',
    notesPlaceholder: 'ماذا أنجزت؟ وما التالي؟', saveSession: 'حفظ الجلسة', saved: 'حُفظت الجلسة', focusing: 'تركيز', breakTime: 'استراحة',
    timer: 'المؤقت', pause: 'إيقاف مؤقت', resume: 'استئناف', skipBreak: 'تخطَّ الاستراحة', stop: 'إنهاء الجلسة', progress: '{minutes} من التركيز · {cycles} دورة'
  },
  analytics: {
    title: 'تقدّمك', subtitle: 'نتائج مقاسة من نشاطك — وما الخطوة التالية.', emptyTitle: 'لا بيانات بعد',
    emptyText: 'اقرأ أو راجع البطاقات أو أدِّ اختبارًا — وسيظهر تقدّمك هنا.', studyTime7d: 'وقت المذاكرة (7 أيام)', of30d: '{value} خلال 30 يومًا',
    streak: 'الأيام المتتالية', longest: 'الأطول: {n} يومًا', quizAccuracy: 'دقة الاختبارات (30 يومًا)', quizzesN: '{n} اختبار', retention: 'تذكّر البطاقات',
    reviewsN: '{n} مراجعة', chapters: 'الفصول', completed: 'مكتملة', studyChart: 'وقت المذاكرة', last14: 'آخر 14 يومًا', quizTrend: 'نتائج الاختبارات',
    lastAttempts: 'المحاولات الأخيرة', notEnoughQuizzes: 'أدِّ اختبارين على الأقل لترى الاتجاه.', recommendations: 'المقترحات',
    subjectProgress: 'التقدّم حسب المادة', progressLegend: 'الشريط: الصفحات المقروءة · النسبة الأولى: تقدّم القراءة · الثانية: دقة الاختبارات.',
    mastery: 'إتقان المواضيع', concepts: 'المفاهيم', chaptersTab: 'الفصول',
    masteryNote: '«المقاس» هو نتيجتك الفعلية في الأسئلة. أما «الإتقان المقدَّر» فيجمع إجابات الاختبارات ومراجعات البطاقات مع ترجيح الأحدث — وهو تقدير، وليس احتمال نجاح.',
    measured: '{c}/{n} صحيحة', estimated: 'تقديري {p}%', noMastery: 'أدِّ اختبارات لترى إتقانك للمواضيع.',
    bands: { weak: 'ضعيف', developing: 'متوسط', strong: 'قوي', insufficient: 'أدلة غير كافية' }
  },
  notes: {
    pageTitle: 'دفترك', subtitle: 'ملاحظات وشروح محفوظة وتظليلات — مرتبطة بكتبك.', new: 'ملاحظة جديدة', search: 'ابحث في الملاحظات…',
    allBooks: 'كل الكتب', fromTutor: 'من المعلّم', empty: 'لا ملاحظات هنا بعد.', title: 'العنوان', titlePlaceholder: 'العنوان', content: 'المحتوى',
    contentPlaceholder: 'اكتب ملاحظتك… (يدعم Markdown والمعادلات $...$)', noSubject: 'بلا مادة', noBook: 'بلا كتاب', page: 'الصفحة', tags: 'وسوم (مفصولة بفواصل)',
    pin: 'تثبيت', preview: 'معاينة', saved: 'حُفظت الملاحظة', toFlashcards: 'أنشئ بطاقات', toQuiz: 'اختبار على هذه الصفحة', selectTitle: 'اختر ملاحظة',
    selectText: 'اختر ملاحظة من القائمة أو ابدأ واحدة جديدة.', deleteTitle: 'حذف هذه الملاحظة؟', deleteMessage: 'لا يمكن التراجع عن ذلك.',
    add: 'إضافة ملاحظة', linkedToPage: 'مرتبطة بالصفحة {page}', emptyBook: 'لا ملاحظات لهذا الكتاب بعد.', openNotebook: 'فتح الدفتر'
  },
  search: {
    title: 'ابحث في كتبك', subtitle: 'اعثر على الفقرات بالكلمات أو بالمعنى عبر الكتب التي تختارها.', placeholder: 'عمّ تبحث؟',
    scope: 'البحث في:', allBooks: 'كل كتبي', recent: 'عمليات بحث سابقة:', clear: 'مسح', resultsIn: '{n} نتيجة في {books} كتاب',
    noResults: 'لا نتائج', noResultsText: 'جرّب كلمات أخرى أو وسّع نطاق البحث.',
    match: { keyword: 'تطابق بالكلمات', semantic: 'معنى مشابه', hybrid: 'كلمات ومعنى', page: 'تطابق بالصفحة' }
  },
  subject: {
    workspace: 'مساحة المادة', read: 'مقروء', accuracy: 'دقة الاختبارات', estimatedMastery: 'الإتقان المقدَّر',
    emptyTitle: 'لا كتب في {subject} بعد', emptyText: 'ارفع كتابك المدرسي لتبدأ مذاكرة هذه المادة مع معلّمك.', nextLesson: 'المقترح التالي',
    startReading: 'ابدأ القراءة', testFirst: 'اختبرني أولًا', books: 'الكتب', addBook: 'إضافة كتاب', chaptersDone: '{done}/{total} فصول',
    showChapters: 'إظهار الفصول', recentQuizzes: 'اختبارات حديثة', noQuizzes: 'لا اختبارات في هذه المادة بعد.', flashcards: 'البطاقات',
    reviewCards: 'راجع البطاقات', weakTopics: 'مواضيع ضعيفة', noWeakTopics: 'لم تُرصد مواضيع ضعيفة بعد.', tools: 'الأدوات', askTutor: 'اسأل المعلّم', mockExam: 'امتحان تجريبي',
    focus: {
      arabic: 'النحو والبلاغة والأدب وفهم المقروء والتعبير.', english: 'المفردات والقواعد والقراءة والكتابة.',
      french: 'المفردات والقواعد وتصريف الأفعال والقراءة والكتابة.', physics: 'القوانين والمعادلات والوحدات والمسائل العددية.',
      chemistry: 'التفاعلات والمعادلات والحسابات والتدرّج الدوري.', biology: 'العمليات والتراكيب والمصطلحات والمقارنات.',
      geography: 'الأماكن والعمليات والتعريفات والأسباب والنتائج.'
    }
  },
  settings: {
    title: 'الإعدادات',
    tabs: { profile: 'الملف الشخصي', preferences: 'التفضيلات', academic: 'الملف الأكاديمي', exams: 'الامتحانات', sessions: 'الأجهزة', privacy: 'الخصوصية والبيانات' },
    changePassword: 'تغيير كلمة المرور', currentPassword: 'كلمة المرور الحالية', passwordChanged: 'تم تغيير كلمة المرور',
    interfaceLanguage: 'لغة الواجهة', theme: 'المظهر', themes: { system: 'مثل جهازي', light: 'فاتح', dark: 'داكن' },
    teachingStyle: 'أسلوب الشرح', styles: { guided: 'موجَّه (خطوة بخطوة)', concise: 'مختصر' }, weeklyGoal: 'الهدف الأسبوعي (بالدقائق)',
    bilingualTerms: 'إظهار المصطلحات الأصلية بجانب الترجمة', motivation: 'إظهار الأيام المتتالية والإنجازات', notifications: 'إشعارات داخل التطبيق',
    targetScore: 'الدرجة المستهدفة (%)', inDays: 'بعد {n} يومًا', past: 'منتهٍ', sessionsLead: 'الأجهزة المسجّل دخولها إلى حسابك حاليًا.',
    thisDevice: 'هذا الجهاز', lastActive: 'نشِط {when}', signOut: 'تسجيل الخروج', unknownDevice: 'جهاز غير معروف',
    privacy1: 'تُخزَّن كتبك المرفوعة بشكل خاص ولا يصل إليها إلا حسابك. لا يمكن لطلاب آخرين البحث فيها أو رؤيتها.',
    privacy2: 'للإجابة عن أسئلتك، تُرسَل الفقرات ذات الصلة من كتابك مع سؤالك إلى مزوّد الذكاء الاصطناعي المُعدّ. لا يستخدم رِواق كتبك أو محادثاتك لتدريب نماذج الذكاء الاصطناعي.',
    privacy3: 'تُستخدم بيانات مذاكرتك (التقدّم والبطاقات والملاحظات ونتائج الاختبارات) فقط لتخصيص خطتك ومقترحاتك.',
    privacy4: 'يمكنك تصدير كل ما أنشأته، وحذف حسابك يزيل كتبك وملفاتك وبيانات مذاكرتك نهائيًا.',
    aiProvider: 'مزوّد الذكاء الاصطناعي على هذا الخادم', aiModel: 'النموذج {model}',
    aiProviders: { 'claude-account': 'Claude — حساب Claude الخاص بمالك الخادم', anthropic: 'واجهة Claude البرمجية (API)', 'openai-compatible': 'خدمة متوافقة مع OpenAI', none: 'غير مُعدّ' },
    export: 'تصدير بياناتي (JSON)', deleteTitle: 'حذف الحساب', deleteLead: 'سيحذف هذا حسابك وكتبك وملفاتك وكل بيانات مذاكرتك نهائيًا، ولا يمكن التراجع عنه.',
    deleteMessage: 'سيُحذف كل شيء نهائيًا. هل أنت متأكد؟', deleteConfirm: 'احذف حسابي'
  },
  notifications: { empty: 'لا إشعارات', book_ready: '«{title}» جاهز للمذاكرة' },
  achievements: {
    first_book: { title: 'أول كتاب', text: 'رفعت أول كتاب مدرسي.' },
    first_chapter: { title: 'أول فصل', text: 'أكملت أول فصل.' },
    chapters_10: { title: '10 فصول', text: 'أكملت عشرة فصول.' },
    streak_3: { title: '3 أيام متتالية', text: 'ذاكرت ثلاثة أيام متتالية.' },
    streak_7: { title: 'أسبوع متواصل', text: 'ذاكرت سبعة أيام متتالية.' },
    streak_30: { title: 'شهر متواصل', text: 'ثلاثون يومًا متتالية — مواظبة رائعة.' },
    reviews_100: { title: '100 مراجعة', text: 'أكملت 100 مراجعة للبطاقات.' },
    reviews_1000: { title: '1000 مراجعة', text: 'أكملت 1000 مراجعة للبطاقات.' },
    first_quiz: { title: 'أول اختبار', text: 'أكملت أول اختبار.' },
    quiz_perfect: { title: 'الدرجة الكاملة', text: 'حصلت على 100% في اختبار.' },
    focus_10h: { title: '10 ساعات تركيز', text: 'عشر ساعات في وضع التركيز.' },
    notes_10: { title: 'مدوّن نشيط', text: 'كتبت عشر ملاحظات.' }
  },
  recommendations: {
    weak_topic: 'أجبت عن {correct} من {total} أسئلة في «{topic}» إجابة صحيحة. راجع الصفحة {page} ثم حلّ بعض الأسئلة المركّزة.',
    weak_topicNoPage: 'أجبت عن {correct} من {total} أسئلة في «{topic}» إجابة صحيحة. راجع هذا الموضوع ثم حلّ بعض الأسئلة المركّزة.',
    weak_concept_in_strong_chapter: 'أداؤك جيد في «{chapter}»، لكنك تخطئ باستمرار في «{topic}» ({correct}/{total}). راجع الصفحة {page} ثم تدرّب على هذا المفهوم.',
    weak_concept_in_strong_chapterNoPage: 'أداؤك جيد في «{chapter}»، لكنك تخطئ باستمرار في «{topic}» ({correct}/{total}). تدرّب على هذا المفهوم.',
    cards_due: 'لديك {count} بطاقة مستحقة — نحو {minutes} دقيقة من المراجعة تحافظ عليها.',
    exam_upcoming: 'امتحان «{examTitle}» بعد {days} يومًا ({subject}). خطتك تعطيه الأولوية الآن.',
    quiz_read_chapter: 'أنهيت قراءة «{chapter}». اختبار قصير سيُظهر ما رسخ في ذهنك.',
    upload_first_book: 'ارفع أول كتاب لتحصل على مقترحات مخصّصة.',
    continue_reading: 'تابع الفصل التالي في {subject}.',
    keep_reviewing: 'عمل رائع — واصل المراجعة لتثبيت ما تعلّمته.',
    actions: { review: 'راجع', reviewCards: 'راجع البطاقات', quiz: 'اختبرني', plan: 'عرض الخطة', open: 'فتح' }
  },
  errors: {
    generic: 'حدث خطأ ما. حاول مرة أخرى.', loadFailed: 'تعذّر تحميل هذه الصفحة.',
    not_found: 'غير موجود — ربما حُذف.', validation_failed: 'راجع الحقول المحددة.', invalid_credentials: 'البريد الإلكتروني أو كلمة المرور غير صحيحة.',
    email_taken: 'يوجد حساب بهذا البريد الإلكتروني بالفعل.', weak_password: 'يجب أن تكون كلمة المرور 8 أحرف على الأقل وتحتوي على رقم.',
    account_locked: 'محاولات فاشلة كثيرة. حاول بعد بضع دقائق.', wrong_password: 'كلمة المرور غير صحيحة.', invalid_reset_token: 'رابط الاستعادة غير صالح أو منتهي الصلاحية.',
    file_not_pdf: 'يمكن رفع ملفات PDF فقط.', file_too_large: 'حجم الملف كبير جدًا.', file_empty: 'الملف فارغ.',
    duplicate_book: 'سبق أن رفعت هذا الكتاب.', pdf_invalid_header: 'هذا الملف ليس PDF صالحًا.', pdf_unreadable: 'تعذّرت قراءة ملف PDF.',
    pdf_encrypted: 'الملف محمي بكلمة مرور.', pdf_no_pages: 'الملف لا يحتوي على صفحات.', pdf_too_many_pages: 'عدد صفحات الملف كبير جدًا.',
    invalid_subject: 'اختر مادة صحيحة.', book_not_ready: 'ما زال هذا الكتاب قيد المعالجة.', no_text_in_scope: 'لم يُعثر على نص مقروء في الصفحات المحددة.',
    selection_required: 'حدّد نصًا أولًا.', selection_too_long: 'النص المحدد طويل جدًا.', page_required: 'اختر صفحة.',
    invalid_page_range: 'اختر نطاق صفحات صحيحًا.', processing_in_progress: 'هذا الكتاب قيد المعالجة بالفعل.',
    ai_not_configured: 'المعلّم الذكي غير مُفعّل على هذا الخادم.', ai_unavailable: 'خدمة الذكاء الاصطناعي غير متاحة مؤقتًا. حاول مرة أخرى.',
    ai_rate_limited: 'خدمة الذكاء الاصطناعي مشغولة. انتظر قليلًا ثم أعد المحاولة.', ai_refused: 'لم يستطع الذكاء الاصطناعي المساعدة في هذا الطلب. جرّب صياغة أخرى.',
    ai_quota_exceeded: 'بلغت الحد اليومي لاستخدام الذكاء الاصطناعي. يتجدد عند منتصف الليل (UTC).', ai_invalid_output: 'تعذّر استخدام رد الذكاء الاصطناعي. حاول مرة أخرى.',
    rate_limited: 'طلبات كثيرة جدًا. تمهّل قليلًا.', network_error: 'خطأ في الشبكة — تحقق من اتصالك.', server_error: 'خطأ في الخادم. حاول مرة أخرى.',
    request_failed: 'فشل الطلب.', unknown_error: 'حدث خطأ ما.', attempt_submitted: 'سُلّمت هذه المحاولة بالفعل.',
    time_expired: 'انتهى الوقت — سُلّمت إجاباتك.', answer_locked: 'تم التحقق من هذه الإجابة بالفعل.', empty_question: 'اكتب سؤالًا أولًا.',
    question_too_long: 'السؤال طويل جدًا.', empty_note: 'تحتاج الملاحظة إلى عنوان أو محتوى.', invalid_query: 'ابحث بحرفين على الأقل.',
    unauthorized: 'سجّل الدخول مجددًا.', missing_client_header: 'حدّث الصفحة من فضلك.', cancelled: 'أُلغي.'
  }
};
