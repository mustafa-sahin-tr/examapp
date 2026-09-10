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
  StudyPage,
  StudyPageContentType,
  StudyPageLinkPlatform,
} from '../../models/study-page';

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
  ]);
  // Varsayilan: bos sayfa dondur (edit mode olmayan testler icin de guvenli)
  studyPageService.getById.and.returnValue(of({} as StudyPage));

  const bookService = jasmine.createSpyObj<BookService>('BookService', ['getAll', 'getTestsByBook']);
  bookService.getAll.and.returnValue(of([]));
  bookService.getTestsByBook.and.returnValue(of([]));

  const snackBar = jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']);
  const router = jasmine.createSpyObj<Router>('Router', ['navigate']);

  TestBed.configureTestingModule({
    imports: [StudyPageEditorComponent],
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
      expect(eba?.label).toBe('EBA');
      expect(youtube?.icon).toBe('smart_display');
      expect(youtube?.label).toBe('YouTube');
      expect(other?.icon).toBe('link');
      expect(other?.label).toBe('Diğer');
    });

    it('platformChangedToYouTube_SelectedPlatformIconAndLabelSignalsUpdate', () => {
      const { component } = setupComponent();
      component.form.controls.platform.setValue(StudyPageLinkPlatform.YouTube);
      expect(component.selectedPlatformIcon()).toBe('smart_display');
      expect(component.selectedPlatformLabel()).toBe('YouTube');
    });

    it('onSave_LinkTypeWithoutUrl_ShowsValidationSnackBarAndDoesNotCallService', () => {
      const { component, studyPageService, snackBar } = setupComponent();
      component.form.patchValue({ title: 'Baslik' });
      component.form.controls.contentType.setValue(StudyPageContentType.Link);
      component.form.controls.url.setValue('');

      component.onSave();

      expect(snackBar.open).toHaveBeenCalledWith('Link tipi icin URL zorunludur.', 'Tamam', jasmine.any(Object));
      expect(studyPageService.create).not.toHaveBeenCalled();
    });

    it('onSave_LinkTypeWithInvalidUrl_ShowsValidationSnackBarAndDoesNotCallService', () => {
      const { component, studyPageService, snackBar } = setupComponent();
      component.form.patchValue({ title: 'Baslik' });
      component.form.controls.contentType.setValue(StudyPageContentType.Link);
      component.form.controls.url.setValue('not-a-valid-url');

      component.onSave();

      expect(snackBar.open).toHaveBeenCalledWith(
        'Gecerli bir http/https URL giriniz.',
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

      expect(snackBar.open).toHaveBeenCalledWith('Kitap secin veya yeni kitap adi girin.', 'Tamam', jasmine.any(Object));
      expect(studyPageService.create).not.toHaveBeenCalled();
    });

    it('onSave_BookPageRangeMissingTest_ShowsTestValidationSnackBar', () => {
      const { component, snackBar } = setupComponent();
      component.form.patchValue({ title: 'Baslik' });
      component.form.controls.contentType.setValue(StudyPageContentType.BookPageRange);
      component.form.controls.bookId.setValue(1);

      component.onSave();

      expect(snackBar.open).toHaveBeenCalledWith(
        'Kitap testi secin veya yeni test adi girin.',
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
        'Bitis sayfasi baslangictan kucuk olamaz.',
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
        'Baslangic ve bitis sayfasi zorunludur.',
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
        'Bu sayfada en az bir resim kalmali.',
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

      expect(snackBar.open).toHaveBeenCalledWith('En az bir resim secmelisiniz.', 'Tamam', jasmine.any(Object));
      expect(studyPageService.create).not.toHaveBeenCalled();
    });
  });

  describe('error handling', () => {
    it('loadStudyPage_ServiceErrors_ShowsSnackBarAndNavigatesToList', () => {
      const services = configureModule('99');
      services.studyPageService.getById.and.returnValue(throwError(() => new Error('not found')));

      TestBed.createComponent(StudyPageEditorComponent);

      expect(services.snackBar.open).toHaveBeenCalledWith(
        'Calisma etkinligi bulunamadi.',
        'Tamam',
        jasmine.any(Object)
      );
      expect(services.router.navigate).toHaveBeenCalledWith(['/study-pages']);
    });
  });
});
