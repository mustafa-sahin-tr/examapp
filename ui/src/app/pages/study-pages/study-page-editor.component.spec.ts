import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';

import { StudyPageEditorComponent } from './study-page-editor.component';
import { GradesService } from '../../services/grades.service';
import { SubjectService } from '../../services/subject.service';
import { StudyPageService } from '../../services/study-page.service';
import { BookService } from '../../services/book.service';
import {
  StudyBookPageLookupResult,
  StudyPage,
  StudyPageContentType,
  StudyPageLinkPlatform,
} from '../../models/study-page';
import { TranslocoService } from '@jsverse/transloco';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';

/** Komponent scope'a göreli anahtar döner; sözlükteki karşılığı gerçek `tr.json`'dan okunur. */
function translate(relativeKey: string): string {
  return TestBed.inject(TranslocoService).translate<string>(`study-pages.${relativeKey}`) ?? '';
}
import studyPagesTr from '../../../../public/i18n/study-pages/tr.json';

/**
 * Servisleri konfigure edip TestBed modulunu hazirlar; fixture'i HENUZ olusturmaz.
 * Boylece 'id' bazli (edit mode) testler constructor calismadan once getById donus
 * degerini ozellestirebilir (constructor icinde loadStudyPage senkron olarak cagrilir).
 */
function configureModule(paramMapId: string | null = 'new') {
  const gradesService = jasmine.createSpyObj<GradesService>('GradesService', ['getGrades']);
  gradesService.getGrades.and.returnValue(of([]));

  const subjectService = jasmine.createSpyObj<SubjectService>('SubjectService', [
    'loadCategories',
    'getSubjectsByGrade',
    'getTopicsBySubjectAndGrade',
    'getSubTopicsByTopic',
  ]);
  subjectService.loadCategories.and.returnValue(of([]));
  subjectService.getSubjectsByGrade.and.returnValue(of([]));
  subjectService.getTopicsBySubjectAndGrade.and.returnValue(of([]));
  subjectService.getSubTopicsByTopic.and.returnValue(of([]));

  const studyPageService = jasmine.createSpyObj<StudyPageService>('StudyPageService', [
    'getById',
    'create',
    'update',
    'lookupBookPages',
  ]);
  // Varsayilan: bos sayfa dondur (edit mode olmayan testler icin de guvenli)
  studyPageService.getById.and.returnValue(of({} as StudyPage));

  const bookService = jasmine.createSpyObj<BookService>('BookService', ['getAll', 'getTestsByBook']);
  bookService.getAll.and.returnValue(of([]));
  bookService.getTestsByBook.and.returnValue(of([]));

  const snackBar = jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']);
  const router = jasmine.createSpyObj<Router>('Router', ['navigate']);

  TestBed.configureTestingModule({
    imports: [StudyPageEditorComponent, translocoTestingModule({
        langs: { 'study-pages/tr': studyPagesTr },
        // app.config.ts ile aynı: tireli scope önekleri camelCase'e çevrilmez.
        translocoConfig: { scopes: { keepCasing: true } },
      })],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideNoopAnimations(),
      { provide: GradesService, useValue: gradesService },
      { provide: SubjectService, useValue: subjectService },
      { provide: StudyPageService, useValue: studyPageService },
      { provide: BookService, useValue: bookService },
      { provide: MatSnackBar, useValue: snackBar },
      { provide: Router, useValue: router },
      {
        provide: ActivatedRoute,
        useValue: { snapshot: { paramMap: convertToParamMap(paramMapId ? { id: paramMapId } : {}) } },
      },
    ],
  });

  // Component kendi imports dizisinde MatSnackBarModule'u bildiriyor; o modul kendi
  // 'providers: [MatSnackBar]' kaydini yaptigi icin yukaridaki kok seviye override'i
  // golgeliyor (component'in kendi environment injector'unde GERCEK MatSnackBar
  // orneklenir). MatSnackBarModule'u component'ten kaldirip kok seviyedeki mock'un
  // etkili olmasini sagliyoruz — template'te modulun kendisi (yonergeler) kullanilmiyor,
  // sadece servis (`inject(MatSnackBar)`) kullanildigi icin bu guvenli.
  TestBed.overrideComponent(StudyPageEditorComponent, {
    remove: { imports: [MatSnackBarModule] },
  });

  return { studyPageService, bookService, snackBar, router };
}

/** configureModule + fixture olusturma (constructor bu noktada calisir). */
function setupComponent(paramMapId: string | null = 'new') {
  const services = configureModule(paramMapId);
  const fixture = TestBed.createComponent(StudyPageEditorComponent);
  const component = fixture.componentInstance;
  return { fixture, component, ...services };
}

describe('StudyPageEditorComponent', () => {
  it('should create', () => {
    const { component } = setupComponent();
    expect(component).toBeTruthy();
  });

  // Kriter 1: tip secici var; secilen tipe gore farkli alanlar gosteriliyor (computed sinyaller)
  describe('content type selector', () => {
    it('default_ContentTypeIsImage_IsImageTypeSignalTrue', () => {
      const { component } = setupComponent();
      expect(component.isImageType()).toBeTrue();
      expect(component.isLinkType()).toBeFalse();
      expect(component.isBookPageRangeType()).toBeFalse();
    });

    it('contentTypeChangedToLink_IsLinkTypeSignalTrueAndOthersFalse', () => {
      const { component } = setupComponent();
      component.form.controls.contentType.setValue(StudyPageContentType.Link);
      expect(component.isLinkType()).toBeTrue();
      expect(component.isImageType()).toBeFalse();
      expect(component.isBookPageRangeType()).toBeFalse();
    });

    it('contentTypeChangedToBookPageRange_IsBookPageRangeTypeSignalTrue', () => {
      const { component } = setupComponent();
      component.form.controls.contentType.setValue(StudyPageContentType.BookPageRange);
      expect(component.isBookPageRangeType()).toBeTrue();
    });

    it('contentTypeOptions_ContainsAllThreeTypesWithLabelsAndIcons', () => {
      const { component } = setupComponent();
      expect(component.contentTypeOptions.length).toBe(3);
      expect(component.contentTypeOptions.map((o) => o.value)).toEqual([
        StudyPageContentType.Image,
        StudyPageContentType.Link,
        StudyPageContentType.BookPageRange,
      ]);
    });
  });

  // Kriter 2: Link tipi - platform ikon/etiket eslemesi
  describe('link platform', () => {
    it('platformOptions_ContainsEbaYoutubeOtherWithCorrectIcons', () => {
      const { component } = setupComponent();
      const eba = component.platformOptions.find((o) => o.value === StudyPageLinkPlatform.Eba);
      const youtube = component.platformOptions.find((o) => o.value === StudyPageLinkPlatform.YouTube);
      const other = component.platformOptions.find((o) => o.value === StudyPageLinkPlatform.Other);

      expect(eba?.icon).toBe('school');
      expect(translate(eba?.labelKey ?? '')).toBe('EBA');
      expect(youtube?.icon).toBe('smart_display');
      expect(translate(youtube?.labelKey ?? '')).toBe('YouTube');
      expect(other?.icon).toBe('link');
      expect(translate(other?.labelKey ?? '')).toBe('Diğer');
    });

    it('platformChangedToYouTube_SelectedPlatformIconAndLabelSignalsUpdate', () => {
      const { component } = setupComponent();
      component.form.controls.platform.setValue(StudyPageLinkPlatform.YouTube);
      expect(component.selectedPlatformIcon()).toBe('smart_display');
      expect(translate(component.selectedPlatformLabelKey())).toBe('YouTube');
    });

    it('onSave_LinkTypeWithoutUrl_ShowsValidationSnackBarAndDoesNotCallService', () => {
      const { component, studyPageService, snackBar } = setupComponent();
      component.form.patchValue({ title: 'Baslik' });
      component.form.controls.contentType.setValue(StudyPageContentType.Link);
      component.form.controls.url.setValue('');

      component.onSave();

      expect(snackBar.open).toHaveBeenCalledWith('Link tipi için URL zorunludur.', 'Tamam', jasmine.any(Object));
      expect(studyPageService.create).not.toHaveBeenCalled();
    });

    it('onSave_LinkTypeWithInvalidUrl_ShowsValidationSnackBarAndDoesNotCallService', () => {
      const { component, studyPageService, snackBar } = setupComponent();
      component.form.patchValue({ title: 'Baslik' });
      component.form.controls.contentType.setValue(StudyPageContentType.Link);
      component.form.controls.url.setValue('not-a-valid-url');

      component.onSave();

      expect(snackBar.open).toHaveBeenCalledWith(
        'Geçerli bir http/https URL giriniz.',
        'Tamam',
        jasmine.any(Object)
      );
      expect(studyPageService.create).not.toHaveBeenCalled();
    });

    it('onSave_LinkTypeWithValidUrl_CallsCreateWithUrlAndPlatform', () => {
      const { component, studyPageService } = setupComponent();
      studyPageService.create.and.returnValue(of({} as StudyPage));
      component.form.patchValue({ title: 'Baslik' });
      component.form.controls.contentType.setValue(StudyPageContentType.Link);
      component.form.controls.url.setValue('https://www.youtube.com/watch?v=abc');
      component.form.controls.platform.setValue(StudyPageLinkPlatform.YouTube);

      component.onSave();

      expect(studyPageService.create).toHaveBeenCalled();
      const [payload] = studyPageService.create.calls.mostRecent().args;
      expect(payload.contentType).toBe(StudyPageContentType.Link);
      expect(payload.url).toBe('https://www.youtube.com/watch?v=abc');
      expect(payload.platform).toBe(StudyPageLinkPlatform.YouTube);
    });
  });

  // Kriter 3: kitap sayfa araligi - mevcut/yeni kitap, sayfa validasyonu
  describe('book page range', () => {
    it('onSave_BookPageRangeWithoutBookOrTest_ShowsValidationSnackBar', () => {
      const { component, snackBar, studyPageService } = setupComponent();
      component.form.patchValue({ title: 'Baslik' });
      component.form.controls.contentType.setValue(StudyPageContentType.BookPageRange);

      component.onSave();

      expect(snackBar.open).toHaveBeenCalledWith('Kitap seçin veya yeni kitap adı girin.', 'Tamam', jasmine.any(Object));
      expect(studyPageService.create).not.toHaveBeenCalled();
    });

    it('onSave_BookPageRangeMissingTest_ShowsTestValidationSnackBar', () => {
      const { component, snackBar } = setupComponent();
      component.form.patchValue({ title: 'Baslik' });
      component.form.controls.contentType.setValue(StudyPageContentType.BookPageRange);
      component.form.controls.bookId.setValue(1);

      component.onSave();

      expect(snackBar.open).toHaveBeenCalledWith(
        'Kitap testi seçin veya yeni test adı girin.',
        'Tamam',
        jasmine.any(Object)
      );
    });

    it('onSave_EndPageLessThanStartPage_ShowsValidationSnackBar', () => {
      const { component, snackBar } = setupComponent();
      component.form.patchValue({ title: 'Baslik' });
      component.form.controls.contentType.setValue(StudyPageContentType.BookPageRange);
      component.form.patchValue({ bookId: 1, bookTestId: 2, startPage: 10, endPage: 5 });

      component.onSave();

      expect(snackBar.open).toHaveBeenCalledWith(
        'Bitiş sayfası başlangıçtan küçük olamaz.',
        'Tamam',
        jasmine.any(Object)
      );
    });

    it('onSave_MissingPageNumbers_ShowsRequiredValidationSnackBar', () => {
      const { component, snackBar } = setupComponent();
      component.form.patchValue({ title: 'Baslik' });
      component.form.controls.contentType.setValue(StudyPageContentType.BookPageRange);
      component.form.patchValue({ bookId: 1, bookTestId: 2 });

      component.onSave();

      expect(snackBar.open).toHaveBeenCalledWith(
        'Başlangıç ve bitiş sayfası zorunludur.',
        'Tamam',
        jasmine.any(Object)
      );
    });

    it('onSave_ValidExistingBookAndTest_CallsCreateWithBookIdsAndPageRange', () => {
      const { component, studyPageService } = setupComponent();
      studyPageService.create.and.returnValue(of({} as StudyPage));
      component.form.patchValue({ title: 'Baslik' });
      component.form.controls.contentType.setValue(StudyPageContentType.BookPageRange);
      component.form.patchValue({ bookId: 1, bookTestId: 2, startPage: 3, endPage: 6 });

      component.onSave();

      expect(studyPageService.create).toHaveBeenCalled();
      const [payload] = studyPageService.create.calls.mostRecent().args;
      expect(payload.bookId).toBe(1);
      expect(payload.bookTestId).toBe(2);
      expect(payload.startPage).toBe(3);
      expect(payload.endPage).toBe(6);
      expect(payload.newBookName).toBeNull();
      expect(payload.newBookTestName).toBeNull();
    });

    it('onSave_InlineNewBookAndTest_CallsCreateWithNewNamesAndNullIds', () => {
      const { component, studyPageService } = setupComponent();
      studyPageService.create.and.returnValue(of({} as StudyPage));
      component.form.patchValue({ title: 'Baslik' });
      component.form.controls.contentType.setValue(StudyPageContentType.BookPageRange);
      component.openNewBookAdd();
      component.form.patchValue({
        newBookName: 'Yeni Kitap',
        newBookTestName: 'Yeni Test',
        startPage: 1,
        endPage: 2,
      });

      component.onSave();

      expect(studyPageService.create).toHaveBeenCalled();
      const [payload] = studyPageService.create.calls.mostRecent().args;
      expect(payload.newBookName).toBe('Yeni Kitap');
      expect(payload.newBookTestName).toBe('Yeni Test');
      expect(payload.bookId).toBeNull();
      expect(payload.bookTestId).toBeNull();
    });

    it('contentTypeSetToBookPageRange_LoadsBooksLazily', () => {
      const { component, bookService } = setupComponent();
      expect(bookService.getAll).not.toHaveBeenCalled();

      component.form.controls.contentType.setValue(StudyPageContentType.BookPageRange);

      expect(bookService.getAll).toHaveBeenCalled();
    });
  });

  // Kriter 4: mevcut resim tabanli etkinlikler edit ekraninda regresyonsuz calisiyor
  describe('edit mode - Image content regression', () => {
    it('loadStudyPage_ExistingImagePageWithoutContentType_PatchesFormAsImageAndPopulatesExistingImages', () => {
      const services = configureModule('42');
      const page: Partial<StudyPage> = {
        id: 42,
        title: 'Eski Sayfa',
        description: 'aciklama',
        isPublished: true,
        // contentType kasten undefined birakildi (eski kayit senaryosu)
        contentType: undefined as unknown as StudyPageContentType,
        images: [
          { id: 1, imageUrl: '/img/1.jpg', sortOrder: 0, fileName: 'a.jpg' },
          { id: 2, imageUrl: '/img/2.jpg', sortOrder: 1, fileName: 'b.jpg' },
        ],
        imageCount: 2,
      };
      services.studyPageService.getById.and.returnValue(of(page as StudyPage));

      // fixture olusturma constructor'i tetikler -> loadStudyPage senkron RxJS 'of' ile hemen calisir
      const fixture = TestBed.createComponent(StudyPageEditorComponent);
      const component = fixture.componentInstance;

      expect(component.isEditMode()).toBeTrue();
      expect(component.form.controls.contentType.value).toBe(StudyPageContentType.Image);
      expect(component.isImageType()).toBeTrue();
      expect(component.existingImages().length).toBe(2);
    });

    it('onSave_EditModeImageTypeWithNoRemainingImages_ShowsValidationAndDoesNotCallUpdate', () => {
      const services = configureModule('42');
      services.studyPageService.getById.and.returnValue(
        of({
          id: 42,
          title: 'Eski Sayfa',
          description: '',
          isPublished: true,
          contentType: StudyPageContentType.Image,
          images: [{ id: 1, imageUrl: '/img/1.jpg', sortOrder: 0, fileName: 'a.jpg' }],
          imageCount: 1,
        } as StudyPage)
      );

      const component = TestBed.createComponent(StudyPageEditorComponent).componentInstance;

      // Tek resmi kaldir
      component.toggleRemoveExisting({ id: 1, imageUrl: '/img/1.jpg', sortOrder: 0, fileName: 'a.jpg' });

      component.onSave();

      expect(services.snackBar.open).toHaveBeenCalledWith(
        'Bu sayfada en az bir resim kalmalı.',
        'Tamam',
        jasmine.any(Object)
      );
      expect(services.studyPageService.update).not.toHaveBeenCalled();
    });

    it('onSave_NewImageTypeWithoutAnyImageSelected_ShowsValidationSnackBar', () => {
      const { component, snackBar, studyPageService } = setupComponent('new');
      component.form.patchValue({ title: 'Baslik' });
      // contentType default Image, hic resim secilmedi

      component.onSave();

      expect(snackBar.open).toHaveBeenCalledWith('En az bir resim seçmelisiniz.', 'Tamam', jasmine.any(Object));
      expect(studyPageService.create).not.toHaveBeenCalled();
    });
  });

  // issue #365 (S3): bucket'lar özel — kitap sayfaları tarayıcıdan yoklanmaz, sunucuya sorulur.
  describe('JSON-guided book pages (server lookup)', () => {
    const signed = (book: string, page: number) =>
      `/img/study-pages/books/${encodeURIComponent(book)}/page_${page}.webp?X-Amz-Signature=s${page}`;
    const stored = (book: string, page: number) => `/img/study-pages/books/${book}/page_${page}.webp`;

    function jsonEvent(config: unknown): Event {
      const file = new File([JSON.stringify(config)], 'kilavuz.json', { type: 'application/json' });
      return { target: { files: [file], value: '' } } as unknown as Event;
    }

    function found(book: string, page: number): StudyBookPageLookupResult {
      return { book, pageNumber: page, exists: true, minioUrl: stored(book, page), previewUrl: signed(book, page) };
    }

    function missing(book: string, page: number): StudyBookPageLookupResult {
      return { book, pageNumber: page, exists: false, minioUrl: null, previewUrl: null };
    }

    it('onJsonSelected_AsksTheServerAndAddsOnlyExistingPagesWithSignedPreview_NoBrowserProbe', async () => {
      const { component, studyPageService } = setupComponent();
      const fetchSpy = spyOn(window, 'fetch').and.callThrough();
      studyPageService.lookupBookPages.and.returnValue(of([found('Fen Kitabı', 3), missing('Fen Kitabı', 4)]));

      await component.onJsonSelected(jsonEvent([{ book: 'Fen Kitabı', pages: [3, 4] }]));

      expect(studyPageService.lookupBookPages).toHaveBeenCalledOnceWith([
        { book: 'Fen Kitabı', pageNumber: 3 },
        { book: 'Fen Kitabı', pageNumber: 4 },
      ]);
      expect(fetchSpy).not.toHaveBeenCalled();
      const images = component.newImages();
      expect(images.length).toBe(1);
      expect(images[0].previewUrl).toBe(signed('Fen Kitabı', 3));
      expect(images[0].minioUrl).toBe(stored('Fen Kitabı', 3));
      expect(images[0].isFromMinio).toBeTrue();
      expect(component.guidanceStats).toEqual({ found: 1, total: 2, missing: 1 });
    });

    it('onSave_SendsTheServerStoredPathNotTheSignedPreview', async () => {
      const { component, studyPageService } = setupComponent();
      studyPageService.lookupBookPages.and.returnValue(of([found('Mat 5', 1)]));
      studyPageService.create.and.returnValue(of({} as StudyPage));
      await component.onJsonSelected(jsonEvent([{ book: 'Mat 5', pages: [1] }]));
      component.form.patchValue({ title: 'Baslik' });

      component.onSave();

      const [payload] = studyPageService.create.calls.mostRecent().args;
      expect(payload.minioImages).toEqual([{ bookName: 'Mat 5', pageNumber: 1, minioUrl: stored('Mat 5', 1) }]);
    });

    it('onJsonSelected_LargeGuide_IsLookedUpInChunksOf200', async () => {
      const { component, studyPageService } = setupComponent();
      studyPageService.lookupBookPages.and.returnValue(of([]));
      const pages = Array.from({ length: 250 }, (_, i) => i + 1);

      await component.onJsonSelected(jsonEvent([{ book: 'Mat 5', pages }]));

      expect(studyPageService.lookupBookPages).toHaveBeenCalledTimes(2);
      expect(studyPageService.lookupBookPages.calls.argsFor(0)[0].length).toBe(200);
      expect(studyPageService.lookupBookPages.calls.argsFor(1)[0].length).toBe(50);
    });

    it('onJsonSelected_LookupRejected_ShowsServerMessageAndKeepsTheGuide', async () => {
      const { component, studyPageService, snackBar } = setupComponent();
      studyPageService.lookupBookPages.and.returnValue(
        throwError(() => ({ status: 400, error: { message: 'Geçersiz kitap klasörü adı.' } }))
      );

      await component.onJsonSelected(jsonEvent([{ book: '../x', pages: [1] }]));

      // Hata mesajı "JSON yüklendi: 0 resim" ile ezilmemeli: SON snackbar hatadır.
      expect(snackBar.open.calls.mostRecent().args[0]).toBe('Geçersiz kitap klasörü adı.');
      expect(component.newImages().length).toBe(0);
      expect(component.hasJsonConfig).toBeTrue();
    });

    it('onJsonSelected_ModelValidationProblemDetails_ShowsFirstFieldErrorLast', async () => {
      const { component, studyPageService, snackBar } = setupComponent();
      studyPageService.lookupBookPages.and.returnValue(
        throwError(() => ({
          status: 400,
          error: {
            title: 'One or more validation errors occurred.',
            errors: { 'Pages[0].PageNumber': ['The field PageNumber must be between 1 and 9999.'] },
          },
        }))
      );

      await component.onJsonSelected(jsonEvent([{ book: 'Mat 5', pages: [0] }]));

      expect(snackBar.open.calls.mostRecent().args[0]).toBe('The field PageNumber must be between 1 and 9999.');
    });

    it('onJsonSelected_LookupFailsWithoutBody_ShowsGenericLookupErrorLast', async () => {
      const { component, studyPageService, snackBar } = setupComponent();
      studyPageService.lookupBookPages.and.returnValue(throwError(() => ({ status: 503, error: null })));

      await component.onJsonSelected(jsonEvent([{ book: 'Mat 5', pages: [1] }]));

      expect(snackBar.open.calls.mostRecent().args[0]).toBe(translate('editor.messages.bookLookupFailed'));
    });

    it('onJsonSelected_Success_ShowsJsonLoadedWithCount', async () => {
      const { component, studyPageService, snackBar } = setupComponent();
      studyPageService.lookupBookPages.and.returnValue(of([found('Mat 5', 1)]));

      await component.onJsonSelected(jsonEvent([{ book: 'Mat 5', pages: [1] }]));

      expect(snackBar.open.calls.mostRecent().args[0]).toBe(
        TestBed.inject(TranslocoService).translate('study-pages.editor.messages.jsonLoaded', { count: 1 })
      );
    });

    it('imageRefresh_BookPagePreview_LooksUpThatSinglePageForAFreshSignedUrl', async () => {
      const { component, studyPageService } = setupComponent();
      studyPageService.lookupBookPages.and.returnValue(of([found('Mat 5', 7)]));
      await component.onJsonSelected(jsonEvent([{ book: 'Mat 5', pages: [7] }]));
      const fresh = signed('Mat 5', 7).replace('s7', 'fresh');
      studyPageService.lookupBookPages.and.returnValue(of([{ ...found('Mat 5', 7), previewUrl: fresh }]));

      let result: string | null | undefined;
      component.imageRefresh(signed('Mat 5', 7)).subscribe((u) => (result = u));

      expect(studyPageService.lookupBookPages.calls.mostRecent().args[0]).toEqual([{ book: 'Mat 5', pageNumber: 7 }]);
      expect(result).toBe(fresh);
    });

    it('imageRefresh_ExistingImageInEditMode_RefetchesTheStudyItem', () => {
      const services = configureModule('42');
      const old = '/img/study-pages/pages/42/a.jpg?X-Amz-Signature=old';
      const fresh = '/img/study-pages/pages/42/a.jpg?X-Amz-Signature=new';
      services.studyPageService.getById.and.returnValue(
        of({ id: 42, title: 'T', images: [{ id: 1, imageUrl: old, sortOrder: 0 }] } as unknown as StudyPage)
      );
      const component = TestBed.createComponent(StudyPageEditorComponent).componentInstance;
      services.studyPageService.getById.and.returnValue(
        of({ id: 42, title: 'T', images: [{ id: 1, imageUrl: fresh, sortOrder: 0 }] } as unknown as StudyPage)
      );

      let result: string | null | undefined;
      component.imageRefresh(old).subscribe((u) => (result = u));

      expect(services.studyPageService.getById).toHaveBeenCalledWith(42);
      expect(result).toBe(fresh);
    });
  });

  describe('error handling', () => {
    it('loadStudyPage_ServiceErrors_ShowsSnackBarAndNavigatesToList', () => {
      const services = configureModule('99');
      services.studyPageService.getById.and.returnValue(throwError(() => new Error('not found')));

      TestBed.createComponent(StudyPageEditorComponent);

      expect(services.snackBar.open).toHaveBeenCalledWith(
        'Çalışma etkinliği bulunamadı.',
        'Tamam',
        jasmine.any(Object)
      );
      expect(services.router.navigate).toHaveBeenCalledWith(['/study-pages']);
    });
  });
});
