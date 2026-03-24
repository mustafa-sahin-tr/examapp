import { ComponentFixture, TestBed } from '@angular/core/testing';

import { EduBlogComponent } from './edu-blog.component';

describe('EduBlogComponent', () => {
  let component: EduBlogComponent;
  let fixture: ComponentFixture<EduBlogComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [EduBlogComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(EduBlogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
