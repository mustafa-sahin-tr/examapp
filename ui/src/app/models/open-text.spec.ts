// Issue #583: açık uçlu cevap modeli — sözcük sayacı (sunucu kuralı), sınır durumu, ayrıştırıcılar.
import {
  buildOpenTextPayload,
  countOpenTextWords,
  isBlankOpenTextPayload,
  isOpenTextType,
  normalizeOpenText,
  openTextRowStatus,
  openTextWordState,
  parseOpenTextAnswer,
  parseOpenTextFeedbackStatus,
  parseOpenTextPlan,
  parseOpenTextSolution,
  parseOpenTextStudentReview,
  stripOpenTextControlChars,
} from './open-text';
import { interactionForComponent, isConsistentInteraction, isDraftableInteractionType, isBlockInteractionType } from './prediction';

describe('open-text model (#583)', () => {
  describe('countOpenTextWords (sunucu OpenTextGrader.CountWords ile aynı)', () => {
    it.each([
      ['', 0],
      ['   \n\t ', 0],
      ['tek', 1],
      ['  iki   sözcük  ', 2],
      ['satır\nsonu\r\nve\tsekme', 4],
      ['noktalama, sayılır! (parantez) - tire', 5],
      ['bölünmez boşluk ayırır', 3],
      ['ideografik　boşluk', 2],
      // .NET char.IsWhiteSpace U+0085'i ayırıcı sayar, U+FEFF'i saymaz.
      ['a\u0085b', 2],
      ['a﻿b', 1],
      ['Türkçe İçerik ğüşöç', 3],
    ])('%j → %i', (text, expected) => {
      expect(countOpenTextWords(text)).toBe(expected);
    });
  });

  it('normalizeOpenText_CrlfToLf_TrimsEdges', () => {
    expect(normalizeOpenText('  a\r\nb\rc  \n')).toBe('a\nb\nc');
  });

  it('stripOpenTextControlChars_KeepsNewlineTabCr_DropsC1AndBidi', () => {
    expect(stripOpenTextControlChars('a\u0000b\u0007c\td\ne\rf\u007F')).toBe('abc\td\ne\rf');
    expect(stripOpenTextControlChars('x\u0080\u0085\u009Fy')).toBe('xy');
    expect(stripOpenTextControlChars('a\u202Ab\u202Ec\u2066d\u2069e\u200Ff')).toBe('abcde\u200Ff');
    // Türkçe ve Latin-1 harfleri (U+00A0 ve sonrası) korunur.
    expect(stripOpenTextControlChars('ğüşıöç é\u00A0ñ')).toBe('ğüşıöç é\u00A0ñ');
  });

  it('openTextWordState_UnderOverBlocked', () => {
    const plan = { minWords: 3, maxWords: 5 };
    expect(openTextWordState(0, plan)).toBe('ok');
    expect(openTextWordState(2, plan)).toBe('under');
    expect(openTextWordState(5, plan)).toBe('ok');
    expect(openTextWordState(6, plan)).toBe('over');
    expect(openTextWordState(10, plan)).toBe('over');
    expect(openTextWordState(11, plan)).toBe('blocked');
    expect(openTextWordState(5000, { minWords: null, maxWords: null })).toBe('ok');
    expect(openTextWordState(5000, null)).toBe('ok');
  });

  it('parseOpenTextPlan_TolerantRead', () => {
    expect(parseOpenTextPlan('{"version":1,"type":"openText","minWords":10,"maxWords":200,"lines":5}')).toEqual({
      version: 1,
      type: 'openText',
      minWords: 10,
      maxWords: 200,
      lines: 5,
    });
    // Tür büyük/küçük harf duyarsız; geçersiz sınırlar sınırsız sayılır; min > max ise min düşer.
    expect(parseOpenTextPlan('{"type":"OPENTEXT","minWords":0,"maxWords":2001,"lines":31}')).toEqual({
      version: 1,
      type: 'openText',
      minWords: null,
      maxWords: null,
      lines: null,
    });
    expect(parseOpenTextPlan('{"type":"openText","minWords":50,"maxWords":10}')?.minWords).toBeNull();
    expect(parseOpenTextPlan('{"type":"textSelect"}')).toBeNull();
    expect(parseOpenTextPlan('bozuk')).toBeNull();
    expect(parseOpenTextPlan(null)).toBeNull();
  });

  it('parseOpenTextSolution_SampleAndRubricOptional', () => {
    expect(parseOpenTextSolution('{"sampleAnswer":"Örnek","rubric":"Ölçüt"}')).toEqual({ sampleAnswer: 'Örnek', rubric: 'Ölçüt' });
    expect(parseOpenTextSolution('{"sampleAnswer":"Örnek"}')).toEqual({ sampleAnswer: 'Örnek', rubric: null });
    expect(parseOpenTextSolution('{"sampleAnswer":"  "}')).toEqual({ sampleAnswer: null, rubric: null });
    expect(parseOpenTextSolution('[1]')).toEqual({ sampleAnswer: null, rubric: null });
    expect(parseOpenTextSolution(null)).toEqual({ sampleAnswer: null, rubric: null });
  });

  it('payload_RoundTrip_BlankIsUnanswered', () => {
    const payload = buildOpenTextPayload('Merhaba <b>dünya</b>');
    expect(JSON.parse(payload)).toEqual({ version: 1, type: 'openText', text: 'Merhaba <b>dünya</b>' });
    expect(parseOpenTextAnswer(payload)).toBe('Merhaba <b>dünya</b>');
    expect(isBlankOpenTextPayload(payload)).toBe(false);
    expect(isBlankOpenTextPayload(buildOpenTextPayload('  \n '))).toBe(true);
    expect(isBlankOpenTextPayload(null)).toBe(true);
    // Başka tür boş sayılmaz (kendi kontrolcüsü karar verir).
    expect(isBlankOpenTextPayload('{"type":"textSelect","selected":[]}')).toBe(false);
    expect(parseOpenTextAnswer('{"type":"slotFill","text":"x"}')).toBe('');
    expect(isOpenTextType('openText')).toBe(true);
  });

  it('parseOpenTextStudentReview_AndRowStatus', () => {
    expect(parseOpenTextStudentReview(null)).toBeNull();
    expect(parseOpenTextStudentReview({ status: 'weird' })).toBeNull();
    expect(parseOpenTextStudentReview({ status: 'pending', evaluation: 'partial', feedback: '' })).toEqual({
      status: 'pending',
      evaluation: null,
      feedback: null,
      reviewedAt: null,
    });
    const reviewed = parseOpenTextStudentReview({ status: 'reviewed', evaluation: 'partial', feedback: 'İyi', reviewedAt: '2026-10-27T10:00:00Z' });
    expect(reviewed).toEqual({ status: 'reviewed', evaluation: 'partial', feedback: 'İyi', reviewedAt: '2026-10-27T10:00:00Z' });
    // Değerlendirmesi olmayan reviewed bekliyor sayılır.
    expect(parseOpenTextStudentReview({ status: 'reviewed', evaluation: null })?.status).toBe('pending');

    expect(openTextRowStatus(reviewed)).toBe('partial');
    expect(openTextRowStatus({ ...reviewed!, status: 'outdated' })).toBe('pending');
    expect(openTextRowStatus(null)).toBeNull();
  });

  it('parseOpenTextFeedbackStatus', () => {
    expect(parseOpenTextFeedbackStatus('{"rows":[{"id":"text","status":"pending"}]}')).toBe('pending');
    expect(parseOpenTextFeedbackStatus('{"rows":[{"id":"text","status":"sufficient"}]}')).toBe('sufficient');
    expect(parseOpenTextFeedbackStatus('{"rows":[{"id":"text","status":"x"}]}')).toBeNull();
    expect(parseOpenTextFeedbackStatus('{"rows":[]}')).toBeNull();
    expect(parseOpenTextFeedbackStatus(null)).toBeNull();
  });

  it('prediction_HIsOpenText_ButNotDraftable', () => {
    expect(isConsistentInteraction('H', 'openText')).toBe(true);
    expect(isConsistentInteraction('D', 'openText')).toBe(false);
    expect(interactionForComponent('H', null)).toBe('openText');
    expect(isBlockInteractionType('openText')).toBe(true);
    // activity-structure'a openText bloğu gönderilmez.
    expect(isDraftableInteractionType('openText')).toBe(false);
    expect(isDraftableInteractionType('textSelect')).toBe(true);
  });
});
