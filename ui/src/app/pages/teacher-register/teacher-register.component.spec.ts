import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';

import { TeacherRegisterComponent } from './teacher-register.component';

import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import registerTr from '../../../../public/i18n/register/tr.json';

/** Sayfa cevirileri 'register' scope'undadir (issue #183); gercek sozluk verilir. */
const translocoTesting = translocoTestingModule({ langs: { 'register/tr': registerTr } });

describe('TeacherRegisterComponent', () => {
  let component: TeacherRegisterComponent;
  let fixture: ComponentFixture<TeacherRegisterComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TeacherRegisterComponent, translocoTesting],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations()],
    })
    .compileComponents();

    fixture = TestBed.createComponent(TeacherRegisterComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
