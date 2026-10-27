import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ActivityCheckPanelComponent } from './activity-check-panel.component';
import { translocoTestingModule } from '../../testing/transloco-testing';

describe('ActivityCheckPanelComponent (#573)', () => {
  let fixture: ComponentFixture<ActivityCheckPanelComponent>;
  const el = () => fixture.nativeElement as HTMLElement;
  const q = (id: string) => el().querySelector(`[data-testid="${id}"]`) as HTMLElement | null;
  const btn = (id: string) => q(id) as HTMLButtonElement | null;

  function setup(inputs: Record<string, unknown> = {}): void {
    TestBed.configureTestingModule({ imports: [ActivityCheckPanelComponent, translocoTestingModule()] });
    fixture = TestBed.createComponent(ActivityCheckPanelComponent);
    Object.entries(inputs).forEach(([key, value]) => fixture.componentRef.setInput(key, value));
    fixture.detectChanges();
  }

  it('beforeFirstCheck_CheckEnabled_RevealAndFinishDisabledWithHints', () => {
    setup();

    expect(btn('check')?.textContent).toContain('Cevapları kontrol et');
    expect(btn('check')?.disabled).toBe(false);
    expect(btn('reveal')?.disabled).toBe(true);
    expect(el().textContent).toContain('İlk kontrolden sonra açılır');
    expect(btn('finish')?.disabled).toBe(true);
    expect(el().textContent).toContain('Bitirmeden önce cevaplarını bir kez kontrol et.');
    expect(q('check-summary')).toBeNull();
  });

  it('blankItems_ShowNonBlockingWarning_CheckStaysEnabled', () => {
    setup({ blankItems: ['c', 'e'] });

    const warning = q('blank-warning');
    expect(warning?.getAttribute('role')).toBe('status');
    expect(warning?.textContent).toContain('2 maddede boş yerleştirme var (c, e)');
    expect(warning?.textContent).toContain('Boş bırakılan satırlar boş sayılır');
    expect(btn('check')?.disabled).toBe(false);
  });

  it('blankKindGeneric_UsesNeutralWording', () => {
    setup({ blankItems: ['a'], blankKind: 'generic' });

    expect(q('blank-warning')?.textContent).toContain('1 maddede boş bırakılan yer var (a)');
    expect(q('blank-warning')?.textContent).not.toContain('yerleştirme');
  });

  it('afterCheck_SummaryInAriaLiveRegion_FixWrongRecheckAndRevealEnabled', () => {
    setup({ checkCount: 1, summary: { correct: 8, total: 10 } });

    const live = q('check-live');
    expect(live?.getAttribute('aria-live')).toBe('polite');
    expect(q('check-summary')?.textContent).toContain('8 / 10 doğru');
    expect(q('check-summary')?.textContent).toContain('Yanlışları düzeltip yeniden kontrol edebilirsin.');
    expect(btn('check')?.textContent).toContain('Yeniden kontrol et');
    expect(btn('fix-wrong')).not.toBeNull();
    expect(btn('reveal')?.disabled).toBe(false);
    expect(btn('finish')?.disabled).toBe(false);
    // Bilgi satırı: puan/yüzde/XP yok.
    expect(el().textContent).not.toMatch(/%|puan|XP/i);
  });

  it('allCorrect_NoFixWrongButton', () => {
    setup({ checkCount: 2, summary: { correct: 4, total: 4 } });

    expect(q('check-summary')?.textContent).toContain('Hepsi doğru');
    expect(btn('fix-wrong')).toBeNull();
  });

  it('rows_CorrectAndWrongCarryIconAndText', () => {
    setup({
      checkCount: 1,
      rows: [
        { label: 'Hedef 1: cat', correct: true },
        { label: 'Hedef 2: bird', correct: false },
      ],
    });

    const rows = Array.from(el().querySelectorAll('[data-testid="result-row"]')) as HTMLElement[];
    expect(rows.length).toBe(2);
    expect(rows[0].classList.contains('acp__row--ok')).toBe(true);
    expect(rows[0].textContent).toContain('Doğru');
    expect(rows[0].querySelector('mat-icon')?.textContent).toBe('check');
    expect(rows[1].classList.contains('acp__row--no')).toBe(true);
    expect(rows[1].textContent).toContain('Yanlış');
    expect(rows[1].querySelector('mat-icon')?.textContent).toBe('close');
  });

  it('error429_ShowsWaitMessageWithoutCountdown', () => {
    setup({ checkCount: 1, error: 'rateLimited' });

    const error = q('check-error');
    expect(error?.getAttribute('role')).toBe('alert');
    expect(error?.textContent).toContain('Çok sık kontrol ettin.');
    expect(error?.textContent).toContain('Biraz bekleyip tekrar dene.');
    expect(error?.textContent).not.toMatch(/\d+\s*(sn|saniye)/);
    expect(btn('retry')).toBeNull();
  });

  it('networkError_SaysAnswersSaved_RetryEmitsRetryNotCheck', () => {
    setup({ error: 'network' });
    const events: string[] = [];
    fixture.componentInstance.check.subscribe(() => events.push('check'));
    fixture.componentInstance.retry.subscribe(() => events.push('retry'));

    expect(q('check-error')?.textContent).toContain('Kontrol edilemedi. Cevapların kayıtlı.');
    btn('retry')!.click();

    // W2: sahibi başarısız adımı yeniden dener; panel her zaman kontrol etmez.
    expect(events).toEqual(['retry']);
  });

  it('networkError_AnswersNotSaved_DoesNotClaimSaved', () => {
    setup({ error: 'network', answersSaved: false });

    expect(q('check-error')?.textContent).not.toContain('Cevapların kayıtlı');
    expect(q('check-error')?.textContent).toContain('henüz kaydedilmedi');
  });

  it('staleSummaryWithWrong_FixWrongStillAvailable', () => {
    setup({ checkCount: 1, summary: { correct: 1, total: 3 }, stale: true });

    expect(btn('fix-wrong')).not.toBeNull();
    expect(el().textContent).toContain('Kontrolden sonra cevabını değiştirdin');
  });

  it('error409_CheckRequired_ShowsCheckFirstWarning', () => {
    setup({ error: 'checkRequired' });

    expect(q('check-error')?.textContent).toContain('Doğru cevaplar için önce bir kez kontrol et.');
  });

  it('genericError_ShowsServerMessage', () => {
    setup({ error: 'generic', errorMessage: 'Sunucu mesajı' });

    expect(q('check-error')?.textContent).toContain('Sunucu mesajı');
  });

  it('revealed_InfoBandAndRestoreButton_NoCheckButton', () => {
    setup({ checkCount: 1, revealed: true, blankItems: ['a'] });
    const restores: void[] = [];
    fixture.componentInstance.restore.subscribe(() => restores.push(undefined));

    expect(q('revealed-info')).not.toBeNull();
    expect(btn('check')).toBeNull();
    expect(q('blank-warning')).toBeNull();
    btn('restore')!.click();
    expect(restores.length).toBe(1);
  });

  it('busy_DisablesActions', () => {
    setup({ checkCount: 1, checking: true });

    expect(btn('check')?.disabled).toBe(true);
    expect(btn('check')?.textContent).toContain('Kontrol ediliyor');
    expect(btn('reveal')?.disabled).toBe(true);
    expect(btn('finish')?.disabled).toBe(true);
  });

  it('outputs_RevealFixWrongFinishEmit', () => {
    setup({ checkCount: 1, summary: { correct: 1, total: 2 } });
    const events: string[] = [];
    fixture.componentInstance.reveal.subscribe(() => events.push('reveal'));
    fixture.componentInstance.fixWrong.subscribe(() => events.push('fix'));
    fixture.componentInstance.finish.subscribe(() => events.push('finish'));

    btn('reveal')!.click();
    btn('fix-wrong')!.click();
    btn('finish')!.click();

    expect(events).toEqual(['reveal', 'fix', 'finish']);
  });

  describe('collapsible on narrow screens (#576 E2E)', () => {
    const checked = { checkCount: 1, summary: { correct: 6, total: 15 }, rows: [{ label: 'a', correct: false }] };

    it('collapsed_ShowsOnlyCompactBar_WithSummaryRecheckAndExpand', () => {
      setup({ ...checked, collapsible: true, collapsed: true });
      const expand = vi.fn();
      const check = vi.fn();
      fixture.componentInstance.expand.subscribe(expand);
      fixture.componentInstance.check.subscribe(check);

      expect(q('check-compact')).not.toBeNull();
      expect(q('check-summary')?.textContent).toContain('6 / 15 doğru');
      expect(el().querySelectorAll('[data-testid="result-row"]').length).toBe(0);
      expect(q('finish')).toBeNull();
      expect(btn('check')?.textContent).toContain('Yeniden kontrol et');
      expect(btn('expand')?.getAttribute('aria-expanded')).toBe('false');
      btn('expand')!.click();
      btn('check')!.click();
      expect(expand).toHaveBeenCalledTimes(1);
      expect(check).toHaveBeenCalledTimes(1);
    });

    it('expandedCollapsible_HasCollapseToggle_NotCollapsibleHasNone', () => {
      setup({ ...checked, collapsible: true, collapsed: false });
      const collapse = vi.fn();
      fixture.componentInstance.collapse.subscribe(collapse);
      expect(q('check-compact')).toBeNull();
      expect(el().querySelectorAll('[data-testid="result-row"]').length).toBe(1);
      btn('collapse')!.click();
      expect(collapse).toHaveBeenCalledTimes(1);

      fixture.componentRef.setInput('collapsible', false);
      fixture.componentRef.setInput('collapsed', true);
      fixture.detectChanges();
      expect(q('collapse')).toBeNull();
      expect(q('check-compact')).toBeNull();
    });

    it('collapsedIsIgnored_BeforeFirstCheck_OnError_AndWhenRevealed', () => {
      setup({ collapsible: true, collapsed: true });
      expect(q('check-compact')).toBeNull();
      fixture.componentRef.setInput('checkCount', 1);
      fixture.componentRef.setInput('error', 'network');
      fixture.detectChanges();
      expect(q('check-compact')).toBeNull();
      expect(q('check-error')).not.toBeNull();
      fixture.componentRef.setInput('error', null);
      fixture.componentRef.setInput('revealed', true);
      fixture.detectChanges();
      expect(q('check-compact')).toBeNull();
      expect(q('restore')).not.toBeNull();
    });
  });

  it('openTextRow_ShowsReviewStatusInsteadOfCorrectWrong (#583)', () => {
    setup({
      checkCount: 1,
      rows: [
        { label: 'Cevabın: 12 sözcük', correct: null, status: 'pending' },
        { label: 'Cevabın: 30 sözcük', correct: null, status: 'insufficient' },
      ],
    });

    const rows = Array.from(el().querySelectorAll('[data-testid="result-row"]'));
    expect(rows[0].getAttribute('data-status')).toBe('pending');
    expect(rows[0].textContent).toContain('Değerlendirme bekliyor');
    expect(rows[0].textContent).toContain('Cevabın: 12 sözcük');
    expect(rows[0].textContent).not.toMatch(/Doğru|Yanlış/);
    expect(rows[0].classList).toContain('acp__row--status');
    expect(rows[1].textContent).toContain('Eksik');
    expect(rows[1].classList).not.toContain('acp__row--no');
  });
});
