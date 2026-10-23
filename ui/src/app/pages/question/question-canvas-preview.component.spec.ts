// #545 — Angular 21 control-flow migrasyonu (@if / @else başlık) ve preview yaşam döngüsü sözleşmesi.
import { Component, EventEmitter, Input, forwardRef, signal } from '@angular/core';
import { Location } from '@angular/common';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { Observable, of, throwError } from 'rxjs';
import { Test } from '../../models/test-instance';

import { QuestionCanvasPreviewComponent } from './question-canvas-preview.component';
import { ImageSelectorComponent } from '../image-selector/image-selector.component';
import { TestService } from '../../services/test.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import questionTr from '../../../../public/i18n/question/tr.json';

/** Gerçek tuval yerine; `@ViewChild(ImageSelectorComponent)` bu stub'ı DI token'ı üzerinden bulur. */
@Component({
  selector: 'app-image-selector',
  standalone: true,
  template: '',
  providers: [{ provide: ImageSelectorComponent, useExisting: forwardRef(() => ImageSelectorStub) }],
})
class ImageSelectorStub {
  @Input() previewMetaProvider: unknown;
  readonly previewMode = signal(false);
  readonly regions = signal<{ isExample?: boolean }[]>([]);
  readonly passages = signal<unknown[]>([]);
  currentClassification = () => null;
  togglePreviewMode = jasmine.createSpy('togglePreviewMode').and.callFake(() => this.previewMode.set(true));
  readonly loaded = new EventEmitter<void>();
}

const P = questionTr.preview;
const C = questionTr.common;

describe('QuestionCanvasPreviewComponent (#545)', () => {
  let fixture: ComponentFixture<QuestionCanvasPreviewComponent>;
  let router: Router;
  let location: { getState: jasmine.Spy; back: jasmine.Spy };
  let testService: jasmine.SpyObj<TestService>;
  let snack: jasmine.Spy;
  let selector: ImageSelectorStub;

  const el = () => fixture.nativeElement as HTMLElement;

  function setup(opts: {
    paramTestId?: string | null;
    queryTestId?: string | null;
    returnUrl?: string | null;
    testResult?: () => Observable<Test>;
  } = {}): void {
    testService = jasmine.createSpyObj<TestService>('TestService', ['get']);
    testService.get.and.callFake(opts.testResult ?? (() => of({ id: 5, name: 'Matematik Testi' } as Test)));
    location = { getState: jasmine.createSpy('getState').and.returnValue(null), back: jasmine.createSpy('back') };

    const route = {
      snapshot: {
        paramMap: convertToParamMap(opts.paramTestId == null ? {} : { testId: opts.paramTestId }),
        queryParamMap: convertToParamMap({
          ...(opts.queryTestId == null ? {} : { testId: opts.queryTestId }),
          ...(opts.returnUrl == null ? {} : { returnUrl: opts.returnUrl }),
        }),
      },
    };

    TestBed.configureTestingModule({
      imports: [QuestionCanvasPreviewComponent, NoopAnimationsModule, translocoTestingModule({ langs: { 'question/tr': questionTr } })],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: route },
        { provide: Location, useValue: location },
        { provide: TestService, useValue: testService },
      ],
    });
    // Gerçek tuval yerine stub (ImageSelectorComponent doğrudan import ediliyor).
    TestBed.overrideComponent(QuestionCanvasPreviewComponent, {
      remove: { imports: [ImageSelectorComponent] },
      add: { imports: [ImageSelectorStub] },
    });
    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);
    spyOn(router, 'navigateByUrl').and.resolveTo(true);
    fixture = TestBed.createComponent(QuestionCanvasPreviewComponent);
    snack = spyOn(fixture.debugElement.injector.get(MatSnackBar), 'open');
    fixture.detectChanges();
    selector = fixture.debugElement.query((de) => de.componentInstance instanceof ImageSelectorStub)
      .componentInstance as ImageSelectorStub;
  }

  it('template_TestNameLoaded_ShowsHeadingWithTestName', () => {
    setup({ paramTestId: '5' });

    expect(el().querySelector('.pv-heading')?.textContent).toContain(P.headingWithTest.replace('{{testName}}', 'Matematik Testi'));
  });

  it('template_TestNameMissing_ShowsGenericPreviewTitle', () => {
    setup({ paramTestId: '5', testResult: () => throwError(() => new Error('not found')) });

    expect(el().querySelector('.pv-heading')?.textContent).toContain(C.previewTitle);
    expect(el().querySelector('.pv-heading')?.textContent).not.toContain('Matematik Testi');
  });

  it('ngAfterViewInit_RouteTestIdValid_EntersPreviewModeForThatTest', () => {
    setup({ paramTestId: '5' });

    expect(testService.get).toHaveBeenCalledWith(5);
    expect(selector.togglePreviewMode).toHaveBeenCalledWith(5);
    expect(fixture.componentInstance.testId()).toBe(5);
  });

  it('ngAfterViewInit_NoTestIdAnywhere_ShowsSnackAndLeavesPreview', () => {
    setup({});

    expect(snack).toHaveBeenCalledWith(P.noTestSelected, C.snackbarAction, jasmine.any(Object));
    expect(testService.get).not.toHaveBeenCalled();
    expect(selector.togglePreviewMode).not.toHaveBeenCalled();
  });

  it('ngAfterViewInit_TestIdOnlyInQuery_UsesQueryParam', () => {
    setup({ queryTestId: '9' });

    expect(testService.get).toHaveBeenCalledWith(9);
  });

  it('closePreview_SameOriginReturnUrl_NavigatesToReturnUrl', () => {
    setup({ paramTestId: '5', returnUrl: '/worksheets/1' });

    fixture.componentInstance.closePreview();

    expect(router.navigateByUrl).toHaveBeenCalledWith('/worksheets/1', undefined);
  });

  it('closePreview_ProtocolRelativeReturnUrl_DoesNotRedirectOffApp', () => {
    setup({ paramTestId: '5', returnUrl: '//evil.example.com' });

    fixture.componentInstance.closePreview();

    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('exitPreview_Clicked_ClosesPreview', () => {
    setup({ paramTestId: '5', returnUrl: '/worksheets/1' });

    (el().querySelector('.pv-btn--primary') as HTMLButtonElement).click();

    expect(router.navigateByUrl).toHaveBeenCalledWith('/worksheets/1', undefined);
  });
});
