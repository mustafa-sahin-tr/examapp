import { HttpErrorResponse } from '@angular/common/http';
import { BadgeRuleTypeSchema } from '../../../models/badge-definition-admin.model';
import {
  buildRuleConfigJson,
  findRuleTypeSchema,
  normalizeErrorKey,
  ruleFieldValues,
  readTarget,
  ruleSummary,
  serverFieldErrors,
} from './badge-rule.util';

const target = { name: 'target', type: 'integer', required: true, min: 1, max: 1_000_000, allowedValues: null };
const simple: BadgeRuleTypeSchema = { ruleType: 'AnswerCount', description: '', fields: [target] };
const subject: BadgeRuleTypeSchema = {
  ruleType: 'SubjectAnswerCount',
  description: '',
  fields: [
    target,
    { name: 'subjectId', type: 'integer', required: false, min: 1, max: null, allowedValues: null },
    { name: 'subjectName', type: 'string', required: false, min: null, max: null, allowedValues: null },
  ],
};

describe('badge-rule.util', () => {
  it('findRuleTypeSchema_IsCaseInsensitive', () => {
    expect(findRuleTypeSchema([simple, subject], 'answercount')).toBe(simple);
    expect(findRuleTypeSchema([simple], 'StudyStreak')).toBeNull();
    expect(findRuleTypeSchema([simple], '')).toBeNull();
  });

  it('buildRuleConfigJson_OnlySchemaFields_NumbersAsNumbers_SkipsEmpty', () => {
    expect(buildRuleConfigJson(simple, { target: '25', extra: 'x', subjectName: 'Matematik' })).toBe('{"target":25}');
    expect(buildRuleConfigJson(subject, { target: 100, subjectId: 3, subjectName: '  Matematik ' })).toBe(
      '{"target":100,"subjectId":3,"subjectName":"Matematik"}'
    );
    expect(buildRuleConfigJson(subject, { target: 5, subjectId: null, subjectName: 'Türkçe' })).toBe(
      '{"target":5,"subjectName":"Türkçe"}'
    );
  });

  it('ruleFieldValues_ReadsCaseInsensitively_DropsUnknown', () => {
    expect(ruleFieldValues(subject, { Target: 60, subjectName: 'Fen Bilimleri', foo: 1 })).toEqual({
      target: 60,
      subjectId: null,
      subjectName: 'Fen Bilimleri',
    });
  });

  it('ruleSummary_KnownAndUnknownTypes', () => {
    expect(ruleSummary({ ruleType: 'SubjectAnswerCount', ruleConfigJson: '{"subjectName":"Matematik","target":100}' })).toEqual(
      jasmine.objectContaining({ key: 'SubjectAnswerCount', params: { target: '100', subject: 'Matematik' } })
    );
    expect(ruleSummary({ ruleType: 'SubjectAnswerCount', ruleConfigJson: '{"subjectId":4,"target":1}' }).params.subject).toBe('#4');
    const legacy = ruleSummary({ ruleType: 'StudyStreak', ruleConfigJson: '{"target":3}' });
    expect(legacy.key).toBeNull();
    expect(legacy.raw).toBe('StudyStreak {"target":3}');
    expect(ruleSummary({ ruleType: 'AnswerCount', ruleConfigJson: 'not json' }).params.target).toBe('?');
  });

  it('readTarget_FallsBackToEvaluatorLegacyAliasesPerRuleType', () => {
    expect(readTarget('AnswerCount', { count: 50 })).toBe(50);
    expect(readTarget('CorrectStreak', { streak: 5 })).toBe(5);
    expect(readTarget('TotalStudyTimeMinutes', { targetMinutes: 90 })).toBe(90);
    expect(readTarget('TotalCorrectAnswers', { correct: 30 })).toBe(30);
    expect(readTarget('ActiveDays', { days: 7 })).toBe(7);
    expect(readTarget('StudyStreak', { days: 3 })).toBe(3);
    expect(readTarget('AnswerCount', { target: 10, count: 50 })).withContext('target önceliklidir').toBe(10);
    expect(readTarget('AnswerCount', { days: 7 })).withContext('başka tipin alias\'ı okunmaz').toBeUndefined();
  });

  it('ruleSummary_LegacyAliasRow_ShowsTarget', () => {
    const summary = ruleSummary({ ruleType: 'SubjectStudyTimeMinutes', ruleConfigJson: '{"subjectName":"Türkçe","minutes":300}' });
    expect(summary.key).toBe('SubjectStudyTimeMinutes');
    expect(summary.params).toEqual({ target: '300', subject: 'Türkçe' });
  });

  it('ruleFieldValues_LegacyAlias_PrefillsTarget', () => {
    expect(ruleFieldValues(subject, { count: 40, subjectName: 'Matematik' })).toEqual({
      target: 40,
      subjectId: null,
      subjectName: 'Matematik',
    });
  });

  it('serverFieldErrors_NormalizesKeys', () => {
    const err = new HttpErrorResponse({
      status: 400,
      error: { errors: { Target: ['t'], '$.pathOrder': ['p'], ruleConfigJson: ['r'], empty: [] } },
    });
    expect(serverFieldErrors(err)).toEqual({ target: ['t'], pathOrder: ['p'], ruleConfigJson: ['r'] });
    expect(serverFieldErrors(new HttpErrorResponse({ status: 500, error: 'x' }))).toBeNull();
    expect(normalizeErrorKey('request.Code')).toBe('code');
  });
});
