import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { TranslatePipe } from '../../core/i18n.service';
import { ProcessingStage, ProcessingStatus, SourceKind } from '../../core/models';
import { IconComponent } from '../../shared/icon.component';

const STAGES: Record<SourceKind, ProcessingStage[]> = {
  pdf: ['validating', 'extractingText', 'recognizingScannedPages', 'detectingStructure', 'processingTablesAndFigures', 'preparingSearch', 'completed'],
  images: ['validating', 'recognizingScannedPages', 'detectingStructure', 'preparingSearch', 'completed'],
  youTube: ['validating', 'extractingText', 'cleaningTranscript', 'detectingStructure', 'preparingSearch', 'completed']
};

/** Stages a source kind passes through without showing them separately. */
const FOLDED: Partial<Record<ProcessingStage, ProcessingStage>> = {
  queued: 'validating', generatingResources: 'preparingSearch', extractingText: 'recognizingScannedPages', processingTablesAndFigures: 'detectingStructure'
};

/** Stage labels that differ for photos and videos. */
const LABELS: Partial<Record<SourceKind, Partial<Record<ProcessingStage, string>>>> = {
  images: { recognizingScannedPages: 'readingPhotos' },
  youTube: { extractingText: 'readingCaptions', detectingStructure: 'findingTopics' }
};

/** Vertical timeline of the ingestion pipeline stages. */
@Component({
  selector: 'rw-processing-timeline',
  imports: [TranslatePipe, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ol class="timeline" [attr.aria-label]="'processing.timeline' | t">
      <li class="done"><span class="node"><rw-icon name="check" [size]="12" /></span>{{ 'processing.stages.uploading' | t }}</li>
      @for (stage of stages(); track stage; let i = $index) {
        <li [class.done]="i < currentIndex() || isComplete()" [class.active]="i === currentIndex() && !isComplete() && !failed()" [class.failed]="failed() && i === currentIndex()">
          <span class="node">
            @if (i < currentIndex() || isComplete()) { <rw-icon name="check" [size]="12" /> }
            @else if (failed() && i === currentIndex()) { <rw-icon name="x" [size]="12" /> }
            @else if (i === currentIndex()) { <span class="spinner" style="width:10px;height:10px;border-width:2px"></span> }
          </span>
          {{ 'processing.stages.' + label(stage) | t }}
          @if (i === currentIndex() && !isComplete() && !failed() && status()?.progress) { <span class="subtle xsmall num">· {{ status()!.progress }}%</span> }
        </li>
      }
    </ol>
  `,
  styles: [`
    .timeline { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 2px; }
    li { position: relative; display: flex; align-items: center; gap: 10px; padding: 6px 0; font-size: var(--text-sm); color: var(--ink-3); }
    li:not(:last-child)::after { content: ''; position: absolute; inset-inline-start: 10px; top: 28px; height: 14px; width: 2px; background: var(--line); }
    li.done { color: var(--ink-2); }
    li.done:not(:last-child)::after { background: var(--brand); }
    li.active { color: var(--ink); font-weight: 600; }
    li.failed { color: var(--danger); }
    .node { width: 22px; height: 22px; border-radius: 50%; display: grid; place-items: center; flex: none; border: 2px solid var(--line-strong); background: var(--surface); color: var(--brand); }
    li.done .node { background: var(--brand); border-color: var(--brand); color: var(--brand-ink); }
    li.active .node { border-color: var(--brand); }
    li.failed .node { background: var(--danger); border-color: var(--danger); color: #fff; }
  `]
})
export class ProcessingTimelineComponent {
  readonly status = input<ProcessingStatus | null>(null);
  readonly kind = input<SourceKind>('pdf');
  protected readonly stages = computed(() => STAGES[this.kind()]);
  protected readonly isComplete = computed(() => ['ready', 'readyWithWarnings'].includes(this.status()?.bookStatus ?? ''));
  protected readonly failed = computed(() => this.status()?.bookStatus === 'failed');
  protected readonly currentIndex = computed(() => {
    const stages = this.stages();
    const stage = this.status()?.stage ?? 'queued';
    const shown = stages.includes(stage) ? stage : FOLDED[stage] ?? 'validating';
    return Math.max(0, stages.indexOf(stages.includes(shown) ? shown : 'validating'));
  });

  protected label(stage: ProcessingStage): string {
    return LABELS[this.kind()]?.[stage] ?? stage;
  }
}
