import { ComponentFixture, TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { QuestionRegion } from '../../../models/draws';
import { translocoTestingModule } from '../../testing/transloco-testing';
import { PREVIEW_DEVICES, QuestionDevicePreviewComponent } from './question-device-preview.component';

const region = {
  id: 1,
  name: 'q',
  x: 0,
  y: 0,
  width: 1000,
  height: 400,
  passageId: '',
  imageId: '',
  imageUrl: 'q.jpg',
  exampleAnswer: null,
  isExample: false,
  answers: ['A', 'B', 'C', 'D'].map((label, id) => ({ id, label, x: 0, y: 0, width: 200, height: 60, imageUrl: `${label}.jpg` })),
} as QuestionRegion;

describe('QuestionDevicePreviewComponent', () => {
  let fixture: ComponentFixture<QuestionDevicePreviewComponent>;
  let component: QuestionDevicePreviewComponent;

  function create(): void {
    fixture = TestBed.createComponent(QuestionDevicePreviewComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('questionRegion', region);
    (fixture.nativeElement as HTMLElement).style.display = 'block';
    (fixture.nativeElement as HTMLElement).style.width = '600px';
    fixture.detectChanges();
  }

  beforeEach(async () => {
    localStorage.removeItem('questionPreview.device');
    await TestBed.configureTestingModule({ imports: [translocoTestingModule(), QuestionDevicePreviewComponent] }).compileComponents();
  });

  afterEach(() => localStorage.removeItem('questionPreview.device'));

  it('offersPhoneTabletAndDesktopAtAgreedWidths', () => {
    expect(Object.values(PREVIEW_DEVICES).map((device) => device.viewport)).toEqual([390, 768, 1440]);
  });

  it('canvasGetsTheStudentAreaSizeEvenWhenColumnIsNarrower', () => {
    create();
    component.selectDevice('desktop');
    fixture.detectChanges();

    const canvas = fixture.nativeElement.querySelector('.device-preview__canvas') as HTMLElement;
    expect(canvas.offsetWidth).toBe(PREVIEW_DEVICES.desktop.width);
    expect(canvas.offsetHeight).toBe(PREVIEW_DEVICES.desktop.height);
    expect(component.scale()).toBeLessThan(1);
  });

  it('phone_FitsColumnWithoutScaling_AndUsesPhoneLayout', async () => {
    create();
    component.selectDevice('phone');
    fixture.detectChanges();
    await new Promise((resolve) => setTimeout(resolve, 50));
    fixture.detectChanges();

    expect(component.scale()).toBe(1);
    const image = fixture.nativeElement.querySelector('.qcv4-question-image') as HTMLElement;
    expect(image.getBoundingClientRect().width).toBeLessThanOrEqual(PREVIEW_DEVICES.phone.width);
    expect(fixture.nativeElement.querySelector('.layout-planned-side')).toBeNull();
  });

  it('choosingAChoice_IsPassedToTheOwner', () => {
    create();
    let chosen: number | undefined;
    component.choiceSelected.subscribe((answer) => (chosen = answer.id));

    (fixture.nativeElement.querySelectorAll('.qcv4-answer-card')[2] as HTMLElement).click();

    expect(chosen).toBe(2);
  });

  it('selectedDevice_IsRememberedForNextPreview', () => {
    create();
    component.selectDevice('tablet');
    fixture.destroy();

    create();

    expect(component.device()).toBe('tablet');
  });

  it('corruptStoredDevice_FallsBackToDesktop', () => {
    localStorage.setItem('questionPreview.device', 'watch');

    create();

    expect(component.device()).toBe('desktop');
  });
});
