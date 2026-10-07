import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';

import { TestCreateComponent } from './test-create.component';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';

describe('TestCreateComponent', () => {
  let component: TestCreateComponent;
  let fixture: ComponentFixture<TestCreateComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TestCreateComponent, translocoTestingModule()],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations()],
    })
    .compileComponents();

    fixture = TestBed.createComponent(TestCreateComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
