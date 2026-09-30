import {
  parseCommentVisibility,
  parseStudentCommentsSummary,
  parseWorksheetCommentNotificationData,
  parseWorksheetCommentRef,
  toQuestionOrder,
  worksheetCommentLink,
} from './worksheet-comment.model';

describe('worksheet-comment.model (issue #309)', () => {
  it('toQuestionOrder_AcceptsOnlyPositiveSafeIntegers', () => {
    expect(toQuestionOrder(1)).toBe(1);
    expect(toQuestionOrder(42)).toBe(42);
    for (const bad of [0, -1, 1.5, '3', null, undefined, NaN, Infinity, Number.MAX_SAFE_INTEGER + 2, {}, true]) {
      expect(toQuestionOrder(bad)).withContext(String(bad)).toBeNull();
    }
  });

  it('parseWorksheetCommentRef_ReadsQuestionOrder', () => {
    expect(parseWorksheetCommentRef({ worksheetId: 1, commentId: 2, questionId: 3, questionOrder: 4 })).toEqual({
      worksheetId: 1,
      questionId: 3,
      commentId: 2,
      rootCommentId: null,
      questionOrder: 4,
    });
  });

  it('parseWorksheetCommentRef_InvalidOrMissingQuestionOrder_Null', () => {
    for (const questionOrder of [0, -3, 2.5, '5', '<b>', null]) {
      const ref = parseWorksheetCommentRef({ worksheetId: 1, commentId: 2, questionId: 3, questionOrder });
      expect(ref?.questionOrder).withContext(String(questionOrder)).toBeNull();
    }
    expect(parseWorksheetCommentNotificationData(JSON.stringify({ worksheetId: 1, commentId: 2 }))?.questionOrder).toBeNull();
  });

  it('worksheetCommentLink_DoesNotCarryQuestionOrder', () => {
    const link = worksheetCommentLink({ worksheetId: 1, commentId: 2, questionId: 3, rootCommentId: null, questionOrder: 4 });
    expect(link).toEqual({ commands: ['/test', 1], queryParams: { commentId: 2, questionId: 3 } });
  });

  it('parseStudentCommentsSummary_ValidShape', () => {
    expect(
      parseStudentCommentsSummary({ worksheetDefault: false, assignmentOverrides: { enabled: 2, disabled: 0 }, extra: 'x' })
    ).toEqual({ worksheetDefault: false, assignmentOverrides: { enabled: 2, disabled: 0 } });
  });

  it('parseStudentCommentsSummary_MalformedOrNull_ReturnsNull', () => {
    const bad: unknown[] = [
      null,
      undefined,
      'x',
      {},
      { worksheetDefault: 'true', assignmentOverrides: { enabled: 0, disabled: 0 } },
      { worksheetDefault: true },
      { worksheetDefault: true, assignmentOverrides: null },
      { worksheetDefault: true, assignmentOverrides: { enabled: -1, disabled: 0 } },
      { worksheetDefault: true, assignmentOverrides: { enabled: 1.5, disabled: 0 } },
      { worksheetDefault: true, assignmentOverrides: { enabled: 1, disabled: '2' } },
    ];
    for (const value of bad) {
      expect(parseStudentCommentsSummary(value)).withContext(JSON.stringify(value) ?? 'undefined').toBeNull();
    }
  });
});

describe('worksheet-comment.model (issue #326)', () => {
  it('parseCommentVisibility_AcceptsKnownValues', () => {
    for (const value of ['teacher', 'school', 'self-and-teacher', 'self']) {
      expect(parseCommentVisibility(value)).toBe(value as ReturnType<typeof parseCommentVisibility>);
    }
  });

  it('parseCommentVisibility_UnknownOrMalformed_ReturnsNull', () => {
    for (const bad of [null, undefined, '', 'Teacher', 'SELF', 'everyone', ' self', 1, true, {}, ['self']]) {
      expect(parseCommentVisibility(bad)).withContext(JSON.stringify(bad) ?? 'undefined').toBeNull();
    }
  });
});
