import { ComponentFixture, TestBed } from '@angular/core/testing';

import { PassageCardComponent } from './passage-card.component';

describe('PassageCardComponent', () => {
    let component: PassageCardComponent;
    let fixture: ComponentFixture<PassageCardComponent>;

    beforeEach(async () => {
        await TestBed.configureTestingModule({
            imports: [PassageCardComponent]
        })
            .compileComponents();

        fixture = TestBed.createComponent(PassageCardComponent);
        component = fixture.componentInstance;
        fixture.detectChanges();
    });

    it('should create', () => {
        expect(component).toBeTruthy();
    });

    // #552: pasaj HTML'i Angular sanitizer'ından geçer; betik ve olay öznitelikleri DOM'a düşmez.
    it('strips script and event handlers from passage HTML but keeps formatting', () => {
        component.passage = {
            id: 1,
            title: '<b>Başlık</b><img src="x" onerror="alert(1)">',
            text: '<p class="ql-align-center"><strong>Metin</strong></p><script>alert(2)</script><a href="javascript:alert(3)">x</a>',
            imageUrl: '',
            x: 0, y: 0, width: 0, height: 0,
            isCanvasQuestion: false,
        };
        fixture.detectChanges();

        const root: HTMLElement = fixture.nativeElement;
        expect(root.querySelector('script')).toBeNull();
        expect(root.querySelector('[onerror]')).toBeNull();
        expect(root.querySelector('a')?.getAttribute('href')).not.toMatch(/^javascript:/);
        expect(root.querySelector('p.ql-align-center strong')?.textContent).toBe('Metin');
        expect(root.querySelector('b')?.textContent).toBe('Başlık');
    });
});
