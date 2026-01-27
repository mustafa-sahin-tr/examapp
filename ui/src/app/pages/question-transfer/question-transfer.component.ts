import { CommonModule } from '@angular/common';
import { Component, OnDestroy, signal } from '@angular/core';
import { FormControl, FormsModule, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatDividerModule } from '@angular/material/divider';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { interval, Subscription, switchMap } from 'rxjs';
import {
  QuestionTransferExportBundle,
  QuestionTransferImportPreview,
  QuestionTransferJob,
  QuestionTransferService,
} from '../../services/question-transfer.service';

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
    MatIconModule,
    MatTooltipModule,
    MatChipsModule,
    MatDividerModule,
    MatAutocompleteModule,
  ],
  templateUrl: './question-transfer.component.html',
  styleUrls: ['./question-transfer.component.scss'],
})
export class QuestionTransferComponent implements OnDestroy {
  private readonly sourceKeyStorageKey = 'questionTransfer.sourceKeys';

  public exportSourceKey = new FormControl<string>('default', { nonNullable: true });
  public exportQuestionIds = new FormControl<string>('', { nonNullable: true });

  public sourceKeyOptions = signal<string[]>([]);

  public importFile = signal<File | null>(null);
  public importPreview = signal<QuestionTransferImportPreview | null>(null);

  public bundles = signal<QuestionTransferExportBundle[]>([]);

  public jobs = signal<QuestionTransferJob[]>([]);
  public isBusy = signal<boolean>(false);
  private pollSub?: Subscription;
  private sourceKeySub?: Subscription;

  constructor(private transfer: QuestionTransferService) {
    this.startPolling();
    this.refreshBundles();

    this.loadSourceKeys();

    this.sourceKeySub = this.exportSourceKey.valueChanges.subscribe(() => {
      this.rememberSourceKey(this.exportSourceKey.value);
      this.refreshBundles();
    });
  }

  ngOnDestroy(): void {
    this.pollSub?.unsubscribe();
    this.sourceKeySub?.unsubscribe();
  }

  onFileSelected(event: Event) {
    const input = event.target as HTMLInputElement;
    const file = input.files?.item(0) ?? null;
    this.importFile.set(file);

    if (!file) {
      this.importPreview.set(null);
      return;
    }

    this.transfer.previewImport(file).subscribe({
      next: (p) => this.importPreview.set(p),
      error: () => this.importPreview.set(null),
    });
  }

  startExport() {
    const ids = this.parseIds(this.exportQuestionIds.value);

    this.isBusy.set(true);
    this.rememberSourceKey(this.exportSourceKey.value);
    this.transfer.startExport(ids, this.exportSourceKey.value).subscribe({
      next: () => {
        this.isBusy.set(false);
        this.refreshJobs();
        this.refreshBundles();
      },
      error: () => this.isBusy.set(false),
    });
  }

  exportIdCount(): number {
    try {
      return this.parseIds(this.exportQuestionIds.value).length;
    } catch {
      return 0;
    }
  }

  exportAllSelected(): boolean {
    return this.exportIdCount() === 0;
  }

  startImport() {
    const file = this.importFile();
    if (!file) return;

    this.isBusy.set(true);
    // Do not send sourceKey: backend will infer from manifest.json
    this.transfer.startImport(file, null).subscribe({
      next: () => {
        this.isBusy.set(false);
        this.refreshJobs();
      },
      error: () => this.isBusy.set(false),
    });
  }

  refreshBundles() {
    const sourceKey = this.exportSourceKey.value;
    this.transfer.listExportBundles(sourceKey).subscribe({
      next: (b) => this.bundles.set(b ?? []),
      error: () => this.bundles.set([]),
    });
  }

  filteredSourceKeys(): string[] {
    const options = this.sourceKeyOptions() ?? [];
    const q = (this.exportSourceKey.value ?? '').trim().toLowerCase();
    if (!q) return options;
    return options.filter((x) => x.toLowerCase().includes(q));
  }

  private loadSourceKeys() {
    const fromStorage = this.readSourceKeysFromStorage();
    if (fromStorage.length) {
      this.sourceKeyOptions.set(fromStorage);
    }

    this.transfer.listSourceKeys().subscribe({
      next: (keys) => {
        const merged = this.mergeSourceKeys(fromStorage, keys ?? []);
        this.sourceKeyOptions.set(merged);
        this.writeSourceKeysToStorage(merged);
      },
      error: () => {
        // keep local options
      },
    });
  }

  private rememberSourceKey(value: string) {
    const key = (value ?? '').trim();
    if (!key) return;

    const existing = this.readSourceKeysFromStorage();
    const merged = this.mergeSourceKeys([key], existing);
    this.sourceKeyOptions.set(this.mergeSourceKeys(merged, this.sourceKeyOptions()));
    this.writeSourceKeysToStorage(merged);
  }

  private mergeSourceKeys(a: string[], b: string[]): string[] {
    const result: string[] = [];
    const seen = new Set<string>();

    const push = (x: string) => {
      const v = (x ?? '').trim();
      if (!v) return;
      const k = v.toLowerCase();
      if (seen.has(k)) return;
      seen.add(k);
      result.push(v);
    };

    (a ?? []).forEach(push);
    (b ?? []).forEach(push);

    // Keep it reasonably small.
    return result.slice(0, 50);
  }

  private readSourceKeysFromStorage(): string[] {
    try {
      const raw = window.localStorage.getItem(this.sourceKeyStorageKey);
      if (!raw) return [];
      const parsed = JSON.parse(raw);
      return Array.isArray(parsed)
        ? parsed
            .map(String)
            .map((x) => x.trim())
            .filter(Boolean)
        : [];
    } catch {
      return [];
    }
  }

  private writeSourceKeysToStorage(keys: string[]) {
    try {
      window.localStorage.setItem(this.sourceKeyStorageKey, JSON.stringify(keys ?? []));
    } catch {
      // ignore
    }
  }

  refreshJobsNow() {
    this.refreshJobs();
  }

  downloadIndex() {
    const sourceKey = this.exportSourceKey.value;
    this.isBusy.set(true);
    this.transfer.downloadSourceIndex(sourceKey).subscribe({
      next: (resp) => {
        const blob = resp.body;
        if (!blob) {
          this.isBusy.set(false);
          return;
        }
        const filename = `question-transfer-${(sourceKey ?? 'default').trim() || 'default'}-index.json`;
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

  downloadBundle(bundle: QuestionTransferExportBundle) {
    this.isBusy.set(true);
    this.transfer.downloadBundle(bundle.sourceKey, bundle.bundleNo).subscribe({
      next: (resp) => {
        const blob = resp.body;
        if (!blob) {
          this.isBusy.set(false);
          return;
        }
        const filename = `question-transfer-${bundle.sourceKey}-bundle-${String(bundle.bundleNo).padStart(4, '0')}.zip`;
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

  downloadBundleMap(bundle: QuestionTransferExportBundle) {
    this.isBusy.set(true);
    this.transfer.downloadBundleMap(bundle.sourceKey, bundle.bundleNo).subscribe({
      next: (resp) => {
        const blob = resp.body;
        if (!blob) {
          this.isBusy.set(false);
          return;
        }
        const filename = `question-transfer-${bundle.sourceKey}-bundle-${String(bundle.bundleNo).padStart(4, '0')}.map.json`;
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

  downloadSourcePackage() {
    const sk = (this.exportSourceKey.value ?? '').trim() || 'default';
    this.isBusy.set(true);
    this.transfer.downloadSourcePackage(sk).subscribe({
      next: (resp) => {
        const blob = resp.body;
        if (!blob) {
          this.isBusy.set(false);
          return;
        }

        const contentDisposition = resp.headers.get('content-disposition');
        const fallbackName = `question-transfer-${sk}-package.zip`;
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

  canDownloadJob(job: QuestionTransferJob): boolean {
    return !!job?.fileUrl && job.status === 'Completed';
  }

  jobDownloadLabel(job: QuestionTransferJob): string {
    const url = (job?.fileUrl ?? '').toLowerCase();
    if (url.endsWith('.json')) return 'Index';
    if (url.endsWith('.zip')) return 'ZIP';
    return 'Dosya';
  }

  jobChipColor(job: QuestionTransferJob): 'primary' | 'accent' | 'warn' | undefined {
    const status = (job?.status ?? '').toLowerCase();
    if (status === 'completed') return 'primary';
    if (status === 'running') return 'accent';
    if (status === 'failed') return 'warn';
    return undefined;
  }

  progress(job: QuestionTransferJob): number {
    if (!job.totalItems) return 0;
    return Math.round((job.processedItems / job.totalItems) * 100);
  }

  private startPolling() {
    this.refreshJobs();
    this.pollSub = interval(60_000)
      .pipe(switchMap(() => this.transfer.listJobs(10)))
      .subscribe({
        next: (jobs) => this.jobs.set(jobs ?? []),
      });
  }

  private refreshJobs() {
    this.transfer.listJobs(10).subscribe({
      next: (jobs) => this.jobs.set(jobs ?? []),
    });
  }

  exportJobs(): QuestionTransferJob[] {
    return (this.jobs() ?? []).filter((j) => j.kind === 'Export');
  }

  importJobs(): QuestionTransferJob[] {
    return (this.jobs() ?? []).filter((j) => j.kind === 'Import');
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
