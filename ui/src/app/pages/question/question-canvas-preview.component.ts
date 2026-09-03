import { AfterViewInit, Component, OnInit, ViewChild, inject, signal } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { ImageSelectorComponent } from '../image-selector/image-selector.component';
import { TestService } from '../../services/test.service';

@Component({
  selector: 'app-question-canvas-preview',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatIconModule, MatSnackBarModule, ImageSelectorComponent],
  templateUrl: './question-canvas-preview.component.html',
  styleUrls: ['./question-canvas-preview.component.scss'],
})
export class QuestionCanvasPreviewComponent implements AfterViewInit {
  @ViewChild(ImageSelectorComponent) imageSelector!: ImageSelectorComponent;

  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly location = inject(Location);
  private readonly snackBar = inject(MatSnackBar);
  private readonly testService = inject(TestService);

  readonly returnUrl = signal<string | null>(null);
  readonly returnState = signal<any | null>(null);
  readonly testId = signal<number | null>(null);
  readonly testName = signal<string>('');

  get questionCount(): number {
    return this.imageSelector?.regions()?.length ?? 0;
  }

  get exampleCount(): number {
    return this.imageSelector?.regions()?.filter((r) => r.isExample)?.length ?? 0;
  }

  get passageCount(): number {
    return this.imageSelector?.passages()?.length ?? 0;
  }

  approveAndSave(): void {
    this.snackBar.open('Sorular onaylandı.', 'Tamam', { duration: 2000 });
    this.closePreview();
  }

  readonly previewMetaProvider = () => {
    const selection = this.imageSelector?.currentClassification?.() ?? null;

    const subtopicId = selection?.subtopicIds?.length ? selection.subtopicIds[0] : null;

    return {
      subjectId: selection?.subjectId ?? null,
      topicId: selection?.topicId ?? null,
      subtopicId,
    };
  };

  ngOnInit(): void {
    // Prefer navigation extras state (works when coming from inside the app)
    const navState = (this.router.getCurrentNavigation()?.extras?.state as any) ?? null;
    const historyState = (this.location.getState?.() as any) ?? (globalThis as any)?.history?.state ?? null;
    const state = navState ?? historyState ?? {};

    this.returnUrl.set(state?.returnUrl ?? this.route.snapshot.queryParamMap.get('returnUrl'));
    this.returnState.set(state?.returnState ?? null);
  }

  ngAfterViewInit(): void {
    const paramTestId = Number(this.route.snapshot.paramMap.get('testId'));
    const queryTestId = Number(this.route.snapshot.queryParamMap.get('testId'));
    const resolvedTestId = Number.isFinite(paramTestId) && paramTestId > 0 ? paramTestId : queryTestId;

    if (!Number.isFinite(resolvedTestId) || resolvedTestId <= 0) {
      this.snackBar.open('Ön izleme için test seçilmedi.', 'Tamam', { duration: 2500 });
      this.closePreview();
      return;
    }

    this.testId.set(resolvedTestId);

    this.testService.get(resolvedTestId).subscribe({
      next: (test) => this.testName.set(test?.name ?? test?.subtitle ?? ''),
      error: () => this.testName.set(''),
    });

    // Ensure ViewChild is ready, then enter preview mode.
    if (!this.imageSelector.previewMode()) {
      this.imageSelector.togglePreviewMode(resolvedTestId);
    }
  }

  closePreview(): void {
    const url = this.returnUrl();
    const state = this.returnState();

    // Prevent open-redirects: only allow in-app absolute paths.
    if (url && url.startsWith('/') && !url.startsWith('//')) {
      this.router.navigateByUrl(url, state ? { state } : undefined);
      return;
    }

    // If user came here directly, try to go back; otherwise go to default.
    if (window.history.length > 1) {
      this.location.back();
      return;
    }

    this.router.navigate(['/questioncanvas']);
  }
}
