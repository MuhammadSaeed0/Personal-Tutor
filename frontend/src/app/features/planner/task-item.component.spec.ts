import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { I18nService } from '../../core/i18n.service';
import { StudyTask } from '../../core/models';
import { StoreService } from '../../core/store.service';
import { TaskItemComponent } from './task-item.component';

const task: StudyTask = {
  id: 't1', date: '2026-10-23', order: 0, type: 'read', status: 'pending', subjectId: 4, bookId: 'b1', bookTitle: 'Physics',
  chapterId: 'c1', chapterTitle: 'Forces', topic: null, estimatedMinutes: 30, reasonCode: 'exam_soon', pageStart: 5, pageEnd: 14, completedAt: null
};

describe('TaskItemComponent', () => {
  beforeEach(async () => {
    localStorage.setItem('riwaq.lang', 'en');
    await TestBed.configureTestingModule({
      imports: [TaskItemComponent],
      providers: [provideRouter([]), { provide: StoreService, useValue: { subject: () => ({ color: '#0369A1' }), subjectName: () => 'Physics' } }]
    }).compileComponents();
    TestBed.inject(I18nService).setLang('en');
  });

  afterEach(() => localStorage.clear());

  it('describes the task, its duration and why it was planned', () => {
    const fixture = TestBed.createComponent(TaskItemComponent);
    fixture.componentRef.setInput('task', task);
    fixture.detectChanges();
    // Interpolated values are wrapped in bidi isolate marks; ignore them when comparing text.
    const text = ((fixture.nativeElement as HTMLElement).textContent ?? '').replace(/[\u2068\u2069]/g, '');
    expect(text).toContain('Read Forces (pp. 5–14)');
    expect(text).toContain('30 min');
    expect(text).toContain('Exam soon');
  });

  it('starts a reading task at the planned page', () => {
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    const fixture = TestBed.createComponent(TaskItemComponent);
    fixture.componentRef.setInput('task', task);
    fixture.detectChanges();
    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.btn.soft')!.click();
    expect(navigate).toHaveBeenCalledWith(['/read', 'b1'], { queryParams: { page: 5, task: 't1' } });
  });

  it('emits completion toggles', () => {
    const fixture = TestBed.createComponent(TaskItemComponent);
    fixture.componentRef.setInput('task', task);
    const emitted: StudyTask[] = [];
    fixture.componentInstance.toggle.subscribe(t => emitted.push(t));
    fixture.detectChanges();
    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.check-btn')!.click();
    expect(emitted).toEqual([task]);
  });
});
