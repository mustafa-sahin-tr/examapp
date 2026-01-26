import { CommonModule } from '@angular/common';
import { Component, OnDestroy, signal } from '@angular/core';
import { FormControl, FormsModule, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { interval, Subscription, switchMap } from 'rxjs';
import { QuestionTransferJob, QuestionTransferService } from '../../services/question-transfer.service';

@Component({
  selector: 'app-question-transfer',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatProgressBarModule,
  ],
  templateUrl: './question-transfer.component.html',
  styleUrls: ['./question-transfer.component.scss'],
})
export class QuestionTransferComponent implements OnDestroy {
  public exportSourceKey = new FormControl<string>('default', { nonNullable: true });
  public exportQuestionIds = new FormControl<string>('', { nonNullable: true });

  public importSourceKey = new FormControl<string>('default', { nonNullable: true });
  public importFile = signal<File | null>(null);

  public jobs = signal<QuestionTransferJob[]>([]);
  public isBusy = signal<boolean>(false);
  private pollSub?: Subscription;

  constructor(private transfer: QuestionTransferService) {
    this.startPolling();
  }

  ngOnDestroy(): void {
    this.pollSub?.unsubscribe();
  }

  onFileSelected(event: Event) {
    const input = event.target as HTMLInputElement;
    const file = input.files?.item(0) ?? null;
    this.importFile.set(file);
  }

  startExport() {
    const ids = this.parseIds(this.exportQuestionIds.value);
    if (!ids.length) return;

    this.isBusy.set(true);
    this.transfer.startExport(ids, this.exportSourceKey.value).subscribe({
      next: () => {
        this.isBusy.set(false);
        this.refreshJobs();
      },
      error: () => this.isBusy.set(false),
    });
  }

  startImport() {
    const file = this.importFile();
    if (!file) return;

    this.isBusy.set(true);
    this.transfer.startImport(file, this.importSourceKey.value).subscribe({
      next: () => {
        this.isBusy.set(false);
        this.refreshJobs();
      },
      error: () => this.isBusy.set(false),
    });
  }

  openHangfire() {
    this.isBusy.set(true);
    this.transfer.hangfireLogin().subscribe({
      next: () => {
        this.isBusy.set(false);
        window.open(this.transfer.hangfireUrl(), '_blank');
      },
      error: () => this.isBusy.set(false),
    });
  }

  download(job: QuestionTransferJob) {
    if (!job?.id) return;

    this.isBusy.set(true);
    this.transfer.download(job.id).subscribe({
      next: (resp) => {
        const blob = resp.body;
        if (!blob) {
          this.isBusy.set(false);
          return;
        }

        const contentDisposition = resp.headers.get('content-disposition');
        const fallbackName = `question-transfer-${job.id}.zip`;
        const filename = this.extractFilename(contentDisposition) ?? fallbackName;

        const url = window.URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        a.click();
        window.URL.revokeObjectURL(url);
        this.isBusy.set(false);
      },
      error: () => this.isBusy.set(false),
    });
  }

  progress(job: QuestionTransferJob): number {
    if (!job.totalItems) return 0;
    return Math.round((job.processedItems / job.totalItems) * 100);
  }

  private startPolling() {
    this.refreshJobs();
    this.pollSub = interval(2500)
      .pipe(switchMap(() => this.transfer.listJobs(50)))
      .subscribe({
        next: (jobs) => this.jobs.set(jobs ?? []),
      });
  }

  private refreshJobs() {
    this.transfer.listJobs(50).subscribe({
      next: (jobs) => this.jobs.set(jobs ?? []),
    });
  }

  private parseIds(value: string): number[] {
    const text = (value ?? '').trim();
    if (!text) return [];

    // Safety cap to avoid accidentally expanding huge ranges.
    const maxIds = 5000;

    const result: number[] = [];
    const push = (n: number) => {
      if (!Number.isFinite(n) || n <= 0) return;
      if (result.length >= maxIds) return;
      result.push(n);
    };

    // Split by common delimiters but keep ranges like 100-200 as one token.
    const tokens = text
      .split(/[\s,;\n\r\t]+/g)
      .map((t) => t.trim())
      .filter(Boolean);

    for (const token of tokens) {
      if (result.length >= maxIds) break;

      const rangeMatch = /^\s*(\d+)\s*-\s*(\d+)\s*$/.exec(token);
      if (rangeMatch) {
        const a = Number(rangeMatch[1]);
        const b = Number(rangeMatch[2]);
        if (!Number.isFinite(a) || !Number.isFinite(b) || a <= 0 || b <= 0) continue;

        const start = Math.min(a, b);
        const end = Math.max(a, b);
        for (let i = start; i <= end && result.length < maxIds; i++) {
          push(i);
        }
        continue;
      }

      // If token contains non-digits (like "12|13"), fall back to extracting numbers.
      const nums = token.match(/\d+/g);
      if (!nums) continue;
      for (const n of nums) {
        if (result.length >= maxIds) break;
        push(Number(n));
      }
    }

    return Array.from(new Set(result));
  }

  private extractFilename(contentDisposition: string | null): string | null {
    if (!contentDisposition) return null;

    // Handles: attachment; filename="foo.zip" or filename=foo.zip
    const match = /filename\*=UTF-8''([^;]+)|filename="?([^";]+)"?/i.exec(contentDisposition);
    const raw = (match?.[1] ?? match?.[2] ?? '').trim();
    if (!raw) return null;

    try {
      return decodeURIComponent(raw);
    } catch {
      return raw;
    }
  }
}
