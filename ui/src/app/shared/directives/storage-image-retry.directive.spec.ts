import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject, of } from 'rxjs';
import { StorageImageRetryDirective } from './storage-image-retry.directive';
import { StorageImageResolver } from '../utils/storage-image-refresh.util';

const OLD = '/img/exam-questions/questions/q1/question.jpg?X-Amz-Signature=old';
const FRESH = '/img/exam-questions/questions/q1/question.jpg?X-Amz-Signature=fresh';
const OTHER = '/img/exam-questions/questions/q2/question.jpg?X-Amz-Signature=other';

@Component({
  standalone: true,
  imports: [StorageImageRetryDirective],
  template: `<img [src]="src()" [appStorageImageRetry]="resolver()" alt="" />`,
})
class HostComponent {
  readonly src = signal(OLD);
  readonly resolver = signal<StorageImageResolver | null>(null);
}

/**
 * issue #365 (S3): süresi dolmuş imzalı görsel bir kez taze URL ile yeniden denenir; döngü yok. Testler senkron
 * (gerçek ağ hatası olayı test bitmeden araya giremez); `error`/`load` olayları elle tetiklenir.
 */
describe('StorageImageRetryDirective', () => {
  function setup(resolverImpl: (failed: string) => Observable<string | null | undefined>) {
    TestBed.configureTestingModule({ imports: [HostComponent] });
    const fixture = TestBed.createComponent(HostComponent);
    const resolver = jasmine.createSpy('resolver').and.callFake(resolverImpl);
    fixture.componentInstance.resolver.set(resolver);
    fixture.detectChanges();
    const img = fixture.nativeElement.querySelector('img') as HTMLImageElement;
    return { fixture, img, resolver };
  }

  const fail = (img: HTMLImageElement) => img.dispatchEvent(new Event('error'));
  const load = (img: HTMLImageElement) => img.dispatchEvent(new Event('load'));

  it('retries a failed storage image once with the fresh URL from the resolver', () => {
    const { img, resolver } = setup(() => of(FRESH));

    fail(img);

    expect(resolver).toHaveBeenCalledOnceWith(OLD);
    expect(img.getAttribute('src')).toBe(FRESH);
  });

  it('does not loop when the fresh URL fails too', () => {
    const { img, resolver } = setup(() => of(FRESH));

    fail(img);
    fail(img); // taze URL de 403
    fail(img);

    expect(resolver).toHaveBeenCalledTimes(1);
    expect(img.getAttribute('src')).toBe(FRESH);
  });

  it('retries again only after a successful load in between (signature expired hours later)', () => {
    let n = 0;
    const { img, resolver } = setup(() => of(`${FRESH}${++n}`));

    fail(img);
    load(img);
    fail(img);

    expect(resolver).toHaveBeenCalledTimes(2);
    expect(img.getAttribute('src')).toBe(`${FRESH}2`);
  });

  it('keeps the src when the resolver returns nothing or the same URL', () => {
    const empty = setup(() => of(null));
    fail(empty.img);
    expect(empty.img.getAttribute('src')).toBe(OLD);

    TestBed.resetTestingModule();
    const same = setup((failed) => of(failed));
    fail(same.img);
    fail(same.img);
    expect(same.img.getAttribute('src')).toBe(OLD);
    expect(same.resolver).toHaveBeenCalledTimes(1);
  });

  it('ignores non-storage images (local previews, external URLs)', () => {
    const { fixture, img, resolver } = setup(() => of(FRESH));
    for (const url of ['data:image/png;base64,AAAA', 'blob:http://localhost/abc', 'https://cdn.example.com/a.png']) {
      fixture.componentInstance.src.set(url);
      fixture.detectChanges();
      fail(img);
    }

    expect(resolver).not.toHaveBeenCalled();
  });

  it('does nothing without a resolver', () => {
    TestBed.configureTestingModule({ imports: [HostComponent] });
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    const img = fixture.nativeElement.querySelector('img') as HTMLImageElement;

    fail(img);

    expect(img.getAttribute('src')).toBe(OLD);
  });

  it('drops a late answer when the binding already moved to another image', () => {
    const answer = new Subject<string>();
    const { fixture, img } = setup(() => answer);

    fail(img);
    fixture.componentInstance.src.set(OTHER);
    fixture.detectChanges();
    answer.next(FRESH);

    expect(img.getAttribute('src')).toBe(OTHER);
  });

  it('a different image on the same element gets its own retry', () => {
    const { fixture, img, resolver } = setup((failed) => of(failed.replace('old', 'fresh').replace('other', 'other-fresh')));

    fail(img);
    fail(img);
    fixture.componentInstance.src.set(OTHER);
    fixture.detectChanges();
    fail(img);

    expect(resolver).toHaveBeenCalledTimes(2);
    expect(resolver.calls.argsFor(1)).toEqual([OTHER]);
    expect(img.getAttribute('src')).toBe(OTHER.replace('other', 'other-fresh'));
  });
});
