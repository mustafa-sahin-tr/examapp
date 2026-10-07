import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { TranslocoService } from '@jsverse/transloco';
import { Observable, Subject, isObservable, of, throwError } from 'rxjs';

import { ParentTestResultDialogComponent, ParentTestResultDialogData } from './parent-test-result-dialog.component';
import { ParentDashboardService } from '../../../services/parent-dashboard.service';
import { LocaleService } from '../../../services/locale.service';
import { ParentChildTestResult } from '../../../models/parent-dashboard.model';
import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import parentDashboardTr from '../../../../../public/i18n/parent-dashboard/tr.json';
import parentDashboardEn from '../../../../../public/i18n/parent-dashboard/en.json';

/** Issue #421: veli test sonucu özeti — sayılar, süre, konu kırılımı; soru/şık içeriği yok; yükleniyor/hata/404 durumları. */
describe('ParentTestResultDialogComponent (issue #421)', () => {
  let service: jasmine.SpyObj<ParentDashboardService>;
  let fixture: ComponentFixture<ParentTestResultDialogComponent>;

  const data: ParentTestResultDialogData = { studentId: 11, testInstanceId: 501, title: 'Kesirler' };

  const result: ParentChildTestResult = {
    studentId: 11,
    testInstanceId: 501,
    worksheetId: 1,
    title: 'Kesirler testi',
    subject: 'Matematik',
    outcome: 'completed',
    startedAt: '2026-10-07T08:00:00Z',
    finishedAt: '2026-10-07T09:05:30Z',
    score: { scorePercent: 62, correctCount: 5, wrongCount: 2, blankCount: 1, totalCount: 8, durationSeconds: 3930 },
    topics: [
      { topicId: 3, name: 'Kesir işlemleri', correctCount: 4, wrongCount: 1, blankCount: 0, totalCount: 5 },
      { topicId: null, name: 'Sınıflandırılmamış', correctCount: 1, wrongCount: 1, blankCount: 1, totalCount: 3 },
    ],
  };

  async function setup(
    response: ParentChildTestResult | Observable<ParentChildTestResult> = result,
    lang = 'tr'
  ): Promise<HTMLElement> {
    service = jasmine.createSpyObj<ParentDashboardService>('ParentDashboardService', ['getChildTestResult']);
    service.getChildTestResult.and.returnValue(isObservable(response) ? response : of(response));

    TestBed.configureTestingModule({
      imports: [
        ParentTestResultDialogComponent,
        NoopAnimationsModule,
        translocoTestingModule({
          langs: { 'parent-dashboard/tr': parentDashboardTr, 'parent-dashboard/en': parentDashboardEn },
        }),
      ],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: jasmine.createSpyObj('MatDialogRef', ['close']) },
        { provide: ParentDashboardService, useValue: service },
        { provide: LocaleService, useValue: { localeDefinition: signal({ angularLocale: lang === 'tr' ? 'tr-TR' : 'en-US' }) } },
      ],
    });
    TestBed.inject(TranslocoService).setActiveLang(lang);
    fixture = TestBed.createComponent(ParentTestResultDialogComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const text = (el: Element | null) => el?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

  it('loadsTheInstanceOfTheChild_AndRendersCounts', async () => {
    const el = await setup();
    expect(service.getChildTestResult).toHaveBeenCalledOnceWith(11, 501);
    expect(text(el.querySelector('[mat-dialog-title]'))).toContain('Kesirler testi');
    expect(text(el.querySelector('[mat-dialog-title]'))).toContain('Matematik');
    expect(text(el.querySelector('[data-test="result-score"]'))).toContain('%62');
    expect(text(el.querySelector('[data-test="result-correct"]'))).toContain('5');
    expect(text(el.querySelector('[data-test="result-wrong"]'))).toContain('2');
    expect(text(el.querySelector('[data-test="result-blank"]'))).toContain('1');
    expect(text(el.querySelector('[data-test="result-duration"]'))).toBe('1 sa 5 dk');
    expect(text(el.querySelector('[data-test="result-started"]'))).toContain('7 Eki 2026');
    // Sunucu saate keser; ekranda "yaklaşık" ve tam saat.
    expect(text(el.querySelector('[data-test="result-started"]'))).toMatch(/^7 Eki 2026, yaklaşık \d{2}:00$/);
    expect(el.querySelector('[data-test="result-finished"]')).not.toBeNull();
    expect(el.querySelector('[data-test="result-timed-out"]')).toBeNull();

    const topics = Array.from(el.querySelectorAll('[data-test="result-topic"]')).map(text);
    expect(topics).toEqual([
      'Kesir işlemleri 4 doğru · 1 yanlış · 0 boş',
      'Sınıflandırılmamış 1 doğru · 1 yanlış · 1 boş',
    ]);
    expect(text(el)).toContain(parentDashboardTr.testResult.privacyNote);
  });

  it('timedOut_ShowsBadge', async () => {
    const el = await setup({ ...result, outcome: 'timedOut' });
    expect(text(el.querySelector('[data-test="result-timed-out"]'))).toContain(parentDashboardTr.testResult.timedOut);
  });

  it('noTopics_ShowsMessage', async () => {
    const el = await setup({ ...result, topics: [] });
    expect(text(el.querySelector('[data-test="result-no-topics"]'))).toBe(parentDashboardTr.testResult.noTopics);
  });

  it('whileLoading_ShowsListTitleAndSpinner', async () => {
    const pending = new Subject<ParentChildTestResult>();
    const el = await setup(pending);
    expect(el.querySelector('[data-test="result-loading"]')).not.toBeNull();
    expect(text(el.querySelector('[mat-dialog-title]'))).toBe('Kesirler');
  });

  it('notFound_ShowsNotFoundMessage', async () => {
    const el = await setup(throwError(() => new HttpErrorResponse({ status: 404 })));
    expect(text(el.querySelector('[data-test="result-error"]'))).toContain(parentDashboardTr.testResult.notFound);
  });

  it('serverError_RetryRefetches', async () => {
    const el = await setup(throwError(() => new HttpErrorResponse({ status: 500 })));
    expect(text(el.querySelector('[data-test="result-error"]'))).toContain(parentDashboardTr.testResult.loadError);

    service.getChildTestResult.and.returnValue(of(result));
    el.querySelector<HTMLButtonElement>('[data-test="result-error"] button')!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.querySelector('[data-test="result-error"]')).toBeNull();
    expect(service.getChildTestResult).toHaveBeenCalledTimes(2);
  });

  it('duration_ShowsMinutesOnly_UnderAMinuteAsShort', async () => {
    const el = await setup({ ...result, score: { ...result.score, durationSeconds: 0 } });
    expect(text(el.querySelector('[data-test="result-duration"]'))).toBe(parentDashboardTr.duration.underMinute);
  });

  it('duration_MinutesFromServerRoundedValue', async () => {
    const el = await setup({ ...result, score: { ...result.score, durationSeconds: 25 * 60 } });
    expect(text(el.querySelector('[data-test="result-duration"]'))).toBe('25 dk');
  });

  it('english_RendersEnglishTexts', async () => {
    const el = await setup(result, 'en');
    expect(text(el.querySelector('[data-test="result-score"]'))).toContain('62%');
    expect(text(el.querySelector('[data-test="result-duration"]'))).toBe('1 h 5 min');
    expect(text(el.querySelector('[data-test="result-close"]'))).toBe(parentDashboardEn.testResult.close);
  });
});
