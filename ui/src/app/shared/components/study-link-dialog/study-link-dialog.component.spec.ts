import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { FormControl } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';

import {
  StudyLinkDialogComponent,
  StudyLinkDialogData,
  detectSourceType,
  httpUrlValidator,
} from './study-link-dialog.component';
import { StudyLinkService } from '../../../services/study-link.service';
import { StudyLink } from '../../../models/study-link';
import { translocoTestingModule } from '../../testing/transloco-testing';
import studyLinksTr from '../../../../../public/i18n/study-links/tr.json';

const link: StudyLink = {
  id: 5,
  topicId: 1,
  subTopicId: 2,
  title: 'Kesirler',
  url: 'https://www.youtube.com/watch?v=abc',
  sourceType: 'YouTube',
  sortOrder: 0,
  isActive: false,
  createdByUserId: 1,
  createdByName: 'Admin',
  createdByRole: 'Admin',
  createTime: '2026-01-01T00:00:00Z',
  updateTime: null,
};

describe('StudyLinkDialogComponent (issue #61)', () => {
  const texts = studyLinksTr.dialog;
  let fixture: ComponentFixture<StudyLinkDialogComponent>;
  let component: StudyLinkDialogComponent;
  let service: jasmine.SpyObj<StudyLinkService>;
  let dialogRef: jasmine.SpyObj<MatDialogRef<StudyLinkDialogComponent, StudyLink>>;

  function configure(data: Partial<StudyLinkDialogData> = {}): void {
    service = jasmine.createSpyObj<StudyLinkService>('StudyLinkService', ['create', 'update']);
    service.create.and.returnValue(of(link));
    service.update.and.returnValue(of(link));
    dialogRef = jasmine.createSpyObj<MatDialogRef<StudyLinkDialogComponent, StudyLink>>('MatDialogRef', ['close']);

    TestBed.configureTestingModule({
      imports: [StudyLinkDialogComponent, translocoTestingModule({ langs: { 'study-links/tr': studyLinksTr } })],
      providers: [
        provideNoopAnimations(),
        { provide: StudyLinkService, useValue: service },
        { provide: MatDialogRef, useValue: dialogRef },
        {
          provide: MAT_DIALOG_DATA,
          useValue: { scope: { subTopicId: 2 }, activeLimitReached: false, maxActiveLinks: 7, ...data },
        },
      ],
    });
    fixture = TestBed.createComponent(StudyLinkDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  describe('httpUrlValidator', () => {
    const check = (value: string) => httpUrlValidator(new FormControl(value));

    it('accepts http and https URLs', () => {
      expect(check('https://www.youtube.com/watch?v=1')).toBeNull();
      expect(check('http://example.com/a')).toBeNull();
      expect(check('')).toBeNull();
    });

    it('rejects javascript:, data:, ftp: and relative URLs', () => {
      expect(check('javascript:alert(1)')).toEqual({ url: true });
      expect(check('JavaScript:alert(document.cookie)')).toEqual({ url: true });
      expect(check('data:text/html,<script>alert(1)</script>')).toEqual({ url: true });
      expect(check('ftp://example.com/file')).toEqual({ url: true });
      expect(check('www.example.com')).toEqual({ url: true });
      expect(check('/relative/path')).toEqual({ url: true });
    });
  });

  it('detectSourceType_YouTubeHosts', () => {
    expect(detectSourceType('https://youtu.be/abc')).toBe('YouTube');
    expect(detectSourceType('https://m.youtube.com/watch?v=1')).toBe('YouTube');
    expect(detectSourceType('https://notyoutube.com/x')).toBe('Other');
    expect(detectSourceType('not a url')).toBe('Other');
  });

  it('save_JavascriptUrl_ShowsErrorAndSendsNoRequest', () => {
    configure();
    component.form.setValue({ title: 'X', url: 'javascript:alert(1)', sourceType: 'Other', isActive: true });
    component.save();
    fixture.detectChanges();

    expect(service.create).not.toHaveBeenCalled();
    expect(el().querySelector('[data-testid="url-error"]')?.textContent).toContain(texts.errors.urlInvalid);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('save_Create_SendsScopeTrimmedValuesAndClosesWithLink', () => {
    configure();
    component.form.controls.title.setValue('  Kesirler  ');
    component.form.controls.url.setValue(' https://youtu.be/abc ');
    component.save();

    expect(service.create).toHaveBeenCalledWith({
      subTopicId: 2,
      title: 'Kesirler',
      url: 'https://youtu.be/abc',
      sourceType: 'YouTube',
      isActive: true,
    });
    expect(dialogRef.close).toHaveBeenCalledWith(link);
  });

  it('create_LimitReached_ActiveDefaultsOffAndDisabled', () => {
    configure({ activeLimitReached: true });
    expect(component.form.controls.isActive.value).toBeFalse();
    expect(component.form.controls.isActive.disabled).toBeTrue();
    expect(el().textContent).toContain('Aktif link sınırı (7) dolu');

    component.form.controls.title.setValue('A');
    component.form.controls.url.setValue('https://a.com');
    component.save();
    expect(service.create).toHaveBeenCalledWith(jasmine.objectContaining({ isActive: false }));
  });

  it('save_Edit_PutsToLinkId', () => {
    configure({ link });
    expect(el().textContent).toContain(texts.editTitle);
    component.form.controls.title.setValue('Yeni');
    component.save();

    expect(service.update).toHaveBeenCalledWith(5, {
      title: 'Yeni',
      url: link.url,
      sourceType: 'YouTube',
      isActive: false,
    });
    expect(service.create).not.toHaveBeenCalled();
  });

  it('save_409ActiveLimit_ShowsServerMessageKeepsDialogOpenAndTurnsActiveOff', () => {
    configure();
    service.create.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { success: false, conflict: true, errorCode: 'ActiveLimitReached', message: 'En fazla 7 aktif link olabilir.' },
          })
      )
    );
    component.form.controls.title.setValue('A');
    component.form.controls.url.setValue('https://a.com');
    component.save();
    fixture.detectChanges();

    expect(el().querySelector('[data-testid="dialog-error"]')?.textContent).toContain('En fazla 7 aktif link olabilir.');
    expect(component.form.controls.isActive.value).toBeFalse();
    expect(dialogRef.close).not.toHaveBeenCalled();
    expect(component.submitting()).toBeFalse();
  });
});
