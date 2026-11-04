import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { PageHeaderComponent } from './page-header.component';

@Component({
  standalone: true,
  imports: [PageHeaderComponent],
  template: `<app-page-header title="Başlık" [description]="description"><button type="button">Eylem</button></app-page-header>`,
})
class HostComponent {
  description = 'Açıklama';
}

describe('PageHeaderComponent (#724)', () => {
  function render(description: string): HTMLElement {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.componentInstance.description = description;
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('rendersOneH1_DescriptionAndProjectedActions', () => {
    const el = render('Açıklama');

    expect(el.querySelectorAll('h1').length).toBe(1);
    expect(el.querySelector('h1')?.textContent).toContain('Başlık');
    expect(el.querySelector('.ph__description')?.textContent).toContain('Açıklama');
    expect(el.querySelector('.ph__actions button')?.textContent).toContain('Eylem');
  });

  it('emptyDescription_RendersNoParagraph', () => {
    expect(render('').querySelector('.ph__description')).toBeNull();
  });
});
