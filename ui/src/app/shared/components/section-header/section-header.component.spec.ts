import { ComponentFixture, TestBed } from '@angular/core/testing';

import { SectionHeaderComponent } from './section-header.component';

describe('SectionHeaderComponent', () => {
    let component: SectionHeaderComponent;
    let fixture: ComponentFixture<SectionHeaderComponent>;

    beforeEach(async () => {
        await TestBed.configureTestingModule({
            imports: [SectionHeaderComponent]
        })
            .compileComponents();

        fixture = TestBed.createComponent(SectionHeaderComponent);
        component = fixture.componentInstance;
        fixture.detectChanges();
    });

    it('should create', () => {
        expect(component).toBeTruthy();
    });

    it('i724_DefaultsToH2_HeadingLevelOneRendersH1', () => {
        const el = fixture.nativeElement as HTMLElement;
        fixture.componentRef.setInput('title', 'Başlık');
        fixture.detectChanges();
        expect(el.querySelector('h2.section-title')?.textContent).toContain('Başlık');
        expect(el.querySelector('h1')).toBeNull();

        fixture.componentRef.setInput('headingLevel', 1);
        fixture.detectChanges();
        expect(el.querySelector('h1.section-title')?.textContent).toContain('Başlık');
        expect(el.querySelector('h2')).toBeNull();
    });
});
