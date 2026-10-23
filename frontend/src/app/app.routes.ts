import { Routes } from '@angular/router';
import { authGuard, guestGuard, onboardingGuard } from './core/guards';

export const routes: Routes = [
  {
    path: 'auth',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/auth-layout.component').then(m => m.AuthLayoutComponent),
    children: [
      { path: 'login', loadComponent: () => import('./features/auth/login.component').then(m => m.LoginComponent) },
      { path: 'register', loadComponent: () => import('./features/auth/register.component').then(m => m.RegisterComponent) },
      { path: 'forgot-password', loadComponent: () => import('./features/auth/forgot-password.component').then(m => m.ForgotPasswordComponent) },
      { path: 'reset-password', loadComponent: () => import('./features/auth/reset-password.component').then(m => m.ResetPasswordComponent) },
      { path: '', pathMatch: 'full', redirectTo: 'login' }
    ]
  },
  {
    path: 'onboarding',
    canActivate: [authGuard],
    loadComponent: () => import('./features/onboarding/onboarding.component').then(m => m.OnboardingComponent)
  },
  {
    path: 'read/:bookId',
    canActivate: [authGuard],
    loadComponent: () => import('./features/reader/reader.component').then(m => m.ReaderComponent)
  },
  {
    path: 'quiz/attempt/:attemptId',
    canActivate: [authGuard],
    loadComponent: () => import('./features/quizzes/attempt.component').then(m => m.AttemptComponent)
  },
  {
    path: 'focus',
    canActivate: [authGuard],
    loadComponent: () => import('./features/planner/focus.component').then(m => m.FocusComponent)
  },
  {
    path: '',
    canActivate: [authGuard, onboardingGuard],
    loadComponent: () => import('./layout/shell.component').then(m => m.ShellComponent),
    children: [
      { path: '', pathMatch: 'full', loadComponent: () => import('./features/dashboard/dashboard.component').then(m => m.DashboardComponent) },
      { path: 'library', loadComponent: () => import('./features/library/library.component').then(m => m.LibraryComponent) },
      { path: 'library/:bookId', loadComponent: () => import('./features/library/book-detail.component').then(m => m.BookDetailComponent) },
      { path: 'subjects/:code', loadComponent: () => import('./features/subjects/subject.component').then(m => m.SubjectComponent) },
      { path: 'tutor', loadComponent: () => import('./features/tutor/tutor-page.component').then(m => m.TutorPageComponent) },
      { path: 'summaries', loadComponent: () => import('./features/summaries/summaries.component').then(m => m.SummariesComponent) },
      { path: 'summaries/:id', loadComponent: () => import('./features/summaries/summary-view.component').then(m => m.SummaryViewComponent) },
      { path: 'flashcards', loadComponent: () => import('./features/flashcards/flashcards.component').then(m => m.FlashcardsComponent) },
      { path: 'flashcards/review', loadComponent: () => import('./features/flashcards/review.component').then(m => m.ReviewComponent) },
      { path: 'quizzes', loadComponent: () => import('./features/quizzes/quizzes.component').then(m => m.QuizzesComponent) },
      { path: 'quizzes/new', loadComponent: () => import('./features/quizzes/quiz-builder.component').then(m => m.QuizBuilderComponent) },
      { path: 'quiz/result/:attemptId', loadComponent: () => import('./features/quizzes/result.component').then(m => m.ResultComponent) },
      { path: 'plan', loadComponent: () => import('./features/planner/planner.component').then(m => m.PlannerComponent) },
      { path: 'analytics', loadComponent: () => import('./features/analytics/analytics.component').then(m => m.AnalyticsComponent) },
      { path: 'notes', loadComponent: () => import('./features/notes/notes.component').then(m => m.NotesComponent) },
      { path: 'search', loadComponent: () => import('./features/search/search.component').then(m => m.SearchComponent) },
      { path: 'tools/:tool', loadComponent: () => import('./features/tools/tools.component').then(m => m.ToolsComponent) },
      { path: 'settings', loadComponent: () => import('./features/settings/settings.component').then(m => m.SettingsComponent) }
    ]
  },
  { path: '**', redirectTo: '' }
];
