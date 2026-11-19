import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { of, throwError } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import solutionTr from '../../../../../public/i18n/solution-recording/tr.json';
import { SolutionRecording } from '../../../models/solution-recording';
import { SolutionRecordingService } from '../../../services/solution-recording.service';
import { SolutionRecordingDialogComponent, SolutionRecordingDialogData } from './solution-recording-dialog.component';

const RECORDING: SolutionRecording = {
  questionId: 7,
  durationMs: 4000,
  audioContentType: 'audio/webm',
  audioSizeBytes: 3,
  schemaRevision: 1,
  strokes: { v: 1, width: 1000, height: 750, strokes: [{ c: 'red', t: 0, p: [10, 10, 0, 50, 50, 100] }] },
  updatedAt: '2026-11-19T10:00:00Z',
};

describe('SolutionRecordingDialogComponent (#1226)', () => {
  let fixture: ComponentFixture<SolutionRecordingDialogComponent>;
  const close = vi.fn();
  const service = {
    get: vi.fn(),
    audio: vi.fn(),
    getForStudent: vi.fn(),
    audioForStudent: vi.fn(),
    save: vi.fn(),
    delete: vi.fn(),
  };
  const root = () => fixture.nativeElement as HTMLElement;
  const byId = (id: string) => root().querySelector<HTMLElement>(`[data-testid="${id}"]`);

  function create(data: Partial<SolutionRecordingDialogData>): void {
    TestBed.configureTestingModule({
      imports: [
        SolutionRecordingDialogComponent,
        NoopAnimationsModule,
        TranslocoTestingModule.forRoot({
          langs: { tr: { 'solution-recording': solutionTr } },
          translocoConfig: { availableLangs: ['tr'], defaultLang: 'tr' },
        }),
      ],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: { mode: 'record', id: 7, title: 'Soru', imageUrl: null, hasRecording: false, ...data } },
        { provide: MatDialogRef, useValue: { close } },
        { provide: SolutionRecordingService, useValue: service },
      ],
    });
    fixture = TestBed.createComponent(SolutionRecordingDialogComponent);
    fixture.detectChanges();
  }

  afterEach(() => {
    vi.clearAllMocks();
    vi.unstubAllGlobals();
  });

  it('a student loads the recording through the session row and gets no recording tools', () => {
    service.getForStudent.mockReturnValue(of(RECORDING));
    service.audioForStudent.mockReturnValue(of(new Blob(['abc'], { type: 'audio/webm' })));

    create({ mode: 'watch', id: 55, hasRecording: true });

    expect(service.getForStudent).toHaveBeenCalledWith(55);
    expect(service.get).not.toHaveBeenCalled();
    expect(root().querySelector('app-solution-player')).not.toBeNull();
    // Kayıt 0. saniyede başlayan tek çizgiyi taşır.
    expect(root().querySelectorAll('app-solution-player path')).toHaveLength(1);
    expect(byId('solution-rerecord')).toBeNull();
    expect(byId('solution-delete')).toBeNull();
  });

  it('shows an error when the recording cannot be loaded', () => {
    service.getForStudent.mockReturnValue(throwError(() => new Error('404')));
    service.audioForStudent.mockReturnValue(of(new Blob()));

    create({ mode: 'watch', id: 55, hasRecording: true });

    expect(byId('solution-load-error')).not.toBeNull();
  });

  it('the owner can delete a saved recording after confirming', () => {
    service.get.mockReturnValue(of(RECORDING));
    service.audio.mockReturnValue(of(new Blob(['abc'], { type: 'audio/webm' })));
    service.delete.mockReturnValue(of(undefined));
    create({ hasRecording: true });

    byId('solution-delete')!.click();
    fixture.detectChanges();
    expect(service.delete).not.toHaveBeenCalled();
    byId('solution-delete-confirm')!.click();
    fixture.detectChanges();

    expect(service.delete).toHaveBeenCalledWith(7);
    expect(byId('solution-start')).not.toBeNull();
    byId('solution-close')!.click();
    expect(close).toHaveBeenCalledWith({ hasRecording: false });
  });

  it('explains a denied microphone instead of starting', async () => {
    vi.stubGlobal('MediaRecorder', class {});
    Object.defineProperty(navigator, 'mediaDevices', {
      configurable: true,
      value: { getUserMedia: () => Promise.reject(new DOMException('denied', 'NotAllowedError')) },
    });
    create({});

    byId('solution-start')!.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(byId('solution-mic-error')!.textContent).toContain(solutionTr.micDenied);
    expect(byId('solution-finish')).toBeNull();
  });
});
