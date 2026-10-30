// Issue #575: pay/payda bileşeni — biçimleme, ara hal korunması, dış değerin geri yüklenmesi.
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { FractionInputComponent } from './fraction-input.component';
import { composeFraction, splitFraction } from '../../../models/slot-fill';

describe('FractionInputComponent (#575)', () => {
    let fixture: ComponentFixture<FractionInputComponent>;
    const inputs = () => Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('input'));

    function setup(mixed: boolean, value = '') {
        TestBed.configureTestingModule({ imports: [FractionInputComponent] });
        fixture = TestBed.createComponent(FractionInputComponent);
        fixture.componentRef.setInput('mixed', mixed);
        fixture.componentRef.setInput('value', value);
        fixture.componentRef.setInput('ariaLabel', 'Kutu 1');
        fixture.componentRef.setInput('numeratorLabel', 'pay');
        fixture.detectChanges();
    }

    it('composeAndSplit_RoundTrip', () => {
        expect(composeFraction({ whole: '', numerator: '3', denominator: '4' })).toBe('3/4');
        expect(composeFraction({ whole: '2', numerator: '3', denominator: '5' })).toBe('2 3/5');
        expect(composeFraction({ whole: '2', numerator: '', denominator: '' })).toBe('2');
        expect(composeFraction({ whole: '', numerator: '3', denominator: '' })).toBe('3/');
        expect(composeFraction({ whole: ' ', numerator: '', denominator: '' })).toBe('');
        expect(splitFraction('2 tam 3/5')).toEqual({ whole: '2', numerator: '3', denominator: '5' });
        expect(splitFraction('3/4')).toEqual({ whole: '', numerator: '3', denominator: '4' });
    });

    it('typing_EmitsComposedValue_CommitOnChange', () => {
        setup(true);
        const typed: string[] = [];
        const committed: string[] = [];
        fixture.componentInstance.valueInput.subscribe((v) => typed.push(v));
        fixture.componentInstance.valueCommit.subscribe((v) => committed.push(v));
        const [whole, num, den] = inputs();

        whole.value = '1';
        whole.dispatchEvent(new Event('input'));
        num.value = '1';
        num.dispatchEvent(new Event('input'));
        den.value = '2';
        den.dispatchEvent(new Event('input'));
        den.dispatchEvent(new Event('change'));

        expect(typed).toEqual(['1', '1 1/', '1 1/2']);
        expect(committed).toEqual(['1 1/2']);
        expect(num.getAttribute('aria-label')).toBe('Kutu 1, pay');
        expect((fixture.nativeElement as HTMLElement).getAttribute('role')).toBe('group');
    });

    it('lettersAreDropped_MinusKeptOnlyAtStartOfWholeAndNumerator', () => {
        setup(true);
        const typed: string[] = [];
        fixture.componentInstance.valueInput.subscribe((v) => typed.push(v));
        const [whole, num, den] = inputs();

        whole.value = '-2a';
        whole.dispatchEvent(new Event('input'));
        num.value = 'üç3';
        num.dispatchEvent(new Event('input'));
        den.value = '-5x';
        den.dispatchEvent(new Event('input'));

        expect([whole.value, num.value, den.value]).toEqual(['-2', '3', '5']);
        expect(typed.at(-1)).toBe('-2 3/5');
    });

    it('externalValue_RestoresParts_PartialStateKeptWhenUnchanged', () => {
        setup(false, '5/8');
        expect(inputs().map((i) => i.value)).toEqual(['5', '8']);

        fixture.componentRef.setInput('value', '');
        fixture.detectChanges();
        expect(inputs().map((i) => i.value)).toEqual(['', '']);
    });

    it('composesFromAllThreeDomFields_EvenIfStateLagsBehind (#575 E2E)', () => {
        setup(true);
        const committed: string[] = [];
        fixture.componentInstance.valueCommit.subscribe((v) => committed.push(v));
        const [whole, num, den] = inputs();
        whole.value = '5';
        num.value = '8';
        den.value = '12';
        // Yalnız son alanın olayı: önceki alanların durumu geride kalmış olsa da değer ekrandakinden kurulur.
        den.dispatchEvent(new Event('change'));

        expect(committed).toEqual(['5 8/12']);
    });

    it('externalReseed_WritesDomEvenWhenBindingValueUnchanged', () => {
        setup(false, '3/4');
        const [num, den] = inputs();
        den.value = '9';
        fixture.componentRef.setInput('value', '1/2');
        fixture.detectChanges();

        expect([num.value, den.value]).toEqual(['1', '2']);
    });
});
