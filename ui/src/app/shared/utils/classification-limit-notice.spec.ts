import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router, provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { translocoTestingModule } from '../testing/transloco-testing';
import { ClassificationLimitNotice } from './classification-limit-notice';

describe('ClassificationLimitNotice', () => {
  const onAction = new Subject<void>();
  const open = vi.fn().mockReturnValue({ onAction: () => onAction });

  beforeEach(() => {
    open.mockClear();
    TestBed.configureTestingModule({
      imports: [translocoTestingModule()],
      providers: [provideRouter([]), { provide: MatSnackBar, useValue: { open } }],
    });
  });

  it('staysQuietWhenEveryQuestionIsClassified', () => {
    const notice = TestBed.inject(ClassificationLimitNotice);

    notice.showIfSkipped({ classificationSkipped: 0 });
    notice.showIfSkipped(null);

    expect(open).not.toHaveBeenCalled();
  });

  it('upgradeAction_GoesToThePlanChoice', () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    TestBed.inject(ClassificationLimitNotice).showIfSkipped({ classificationSkipped: 2 });
    onAction.next();

    expect(open).toHaveBeenCalledTimes(1);
    expect(navigate).toHaveBeenCalledWith('/plans');
  });
});
