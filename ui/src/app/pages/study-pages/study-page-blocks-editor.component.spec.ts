import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';
import { StudyPageBlockType, StudyPageLinkPlatform } from '../../models/study-page';
import { StudyPageService } from '../../services/study-page.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import { StudyPageBlockDraft, StudyPageBlocksEditorComponent, toBlockDrafts, toBlockRequests } from './study-page-blocks-editor.component';
import studyPagesTr from '../../../../public/i18n/study-pages/tr.json';

function setup() {
  const studyPages = { uploadBlockImage: vi.fn() };
  const dialog = { open: vi.fn() };
  const snackBar = { open: vi.fn() };
  TestBed.configureTestingModule({
    imports: [
      StudyPageBlocksEditorComponent,
      translocoTestingModule({ langs: { 'study-pages/tr': studyPagesTr }, translocoConfig: { scopes: { keepCasing: true } } }),
    ],
    providers: [
      provideNoopAnimations(),
      { provide: StudyPageService, useValue: studyPages },
      { provide: MatDialog, useValue: dialog },
      { provide: MatSnackBar, useValue: snackBar },
    ],
  });
  const fixture = TestBed.createComponent(StudyPageBlocksEditorComponent);
  fixture.detectChanges();
  return { fixture, component: fixture.componentInstance, studyPages, dialog, snackBar };
}

const text = (key: string, value: string): StudyPageBlockDraft => ({ key, id: 0, sortOrder: 0, type: StudyPageBlockType.Text, text: value });

describe('StudyPageBlocksEditorComponent (#933)', () => {
  it('add_InsertsAtTheGivenPosition_MoveSwaps_RemoveDrops', () => {
    const { component } = setup();
    component.blocks.set([text('a', 'A'), text('b', 'B')]);

    component.add(StudyPageBlockType.Flashcard, 1);
    expect(component.blocks().map((b) => b.type)).toEqual([StudyPageBlockType.Text, StudyPageBlockType.Flashcard, StudyPageBlockType.Text]);

    component.move(0, 1);
    expect(component.blocks()[1].key).toBe('a');
    component.move(0, -1); // en üstte: değişmez
    expect(component.blocks()[1].key).toBe('a');

    component.remove('a');
    expect(component.blocks().map((b) => b.key)).not.toContain('a');
  });

  it('addQuestion_PickerCancelled_RemovesTheEmptyBlock_PickedQuestionIsKept', () => {
    const { component, dialog } = setup();
    dialog.open.mockReturnValueOnce({ afterClosed: () => of(undefined) });
    component.add(StudyPageBlockType.Question, 0);
    expect(component.blocks()).toEqual([]);

    dialog.open.mockReturnValueOnce({ afterClosed: () => of({ id: 5, imageUrl: '/img/q.png', text: null }) });
    component.add(StudyPageBlockType.Question, 0);
    expect(component.blocks()[0]).toMatchObject({ type: StudyPageBlockType.Question, questionId: 5, questionImageUrl: '/img/q.png' });
  });

  it('imageUpload_SetsTheReturnedUrl_FailureShowsTheServerMessage', () => {
    const { component, studyPages, snackBar } = setup();
    component.add(StudyPageBlockType.Image, 0);
    const key = component.blocks()[0].key;
    const file = new File([new Uint8Array([1])], 'a.png', { type: 'image/png' });
    const input = document.createElement('input');
    input.type = 'file';
    Object.defineProperty(input, 'files', { value: [file] });

    studyPages.uploadBlockImage.mockReturnValueOnce(of({ imageUrl: '/img/study-pages/pages/blocks/1/a.png?sig' }));
    component.onImageSelected(key, { target: input } as unknown as Event);
    expect(component.blocks()[0]).toMatchObject({ imageUrl: '/img/study-pages/pages/blocks/1/a.png?sig', uploading: false });

    studyPages.uploadBlockImage.mockReturnValueOnce(throwError(() => ({ error: { message: 'Çok büyük' } })));
    component.onImageSelected(key, { target: input } as unknown as Event);
    expect(snackBar.open).toHaveBeenCalledWith('Çok büyük', undefined, expect.any(Object));
  });

  it('previewMode_RendersTheStudentView', () => {
    const { fixture, component } = setup();
    component.blocks.set([text('a', 'Merhaba')]);
    component.mode.set('preview');
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="blocks-preview"] [data-testid="block-text"]')?.textContent).toContain('Merhaba');
  });

  it('toBlockRequests_ClearsForeignFieldsAndSkipsUnfinishedBlocks', () => {
    const drafts = toBlockDrafts([
      { id: 3, sortOrder: 1, type: StudyPageBlockType.Link, url: ' https://eba.gov.tr ', text: 'EBA', backText: 'x', platform: null },
      { id: 4, sortOrder: 2, type: StudyPageBlockType.Question, questionId: null },
      { id: 0, sortOrder: 3, type: StudyPageBlockType.Image, imageUrl: null },
    ]);

    expect(toBlockRequests(drafts)).toEqual([
      { id: 3, type: StudyPageBlockType.Link, text: 'EBA', backText: null, imageUrl: null, url: 'https://eba.gov.tr', platform: StudyPageLinkPlatform.Other, questionId: null },
    ]);
  });
});
