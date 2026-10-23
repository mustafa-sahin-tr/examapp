import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';

import { TestCreateComponent } from './test-create.component';
import { buildXlsxFile } from '../../shared/testing/xlsx-testing';
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

  describe('Excel okuma (readExcelFile, SheetJS 0.20.3)', () => {
    // Özel erişim: readExcelFile private; ham JSON sözleşmesini doğrudan denetliyoruz.
    const readExcel = (file: File): Promise<Record<string, unknown>[]> =>
      component['readExcelFile'](file);

    it('readExcelFile_HeaderAndRows_MapsRowsByHeaderNames', async () => {
      const file = buildXlsxFile([
        ['Ad', 'Sınıf', 'Süre', 'Açıklama'],
        ['Matematik', '5', 30, 'Konu tekrarı'],
        ['Fen', '6', 45, 'Deney'],
      ]);

      const rows = await readExcel(file);

      expect(rows.length).toBe(2);
      expect(rows[0]['Ad']).toBe('Matematik');
      expect(rows[0]['Sınıf']).toBe('5');
      expect(rows[0]['Süre']).toBe(30);
      expect(rows[1]['Ad']).toBe('Fen');
      expect(rows[1]['Açıklama']).toBe('Deney');
    });

    it('readExcelFile_EmptyCell_UsesEmptyStringDefault', async () => {
      const file = buildXlsxFile([
        ['Ad', 'Sınıf', 'Açıklama'],
        ['Matematik', '5', null],
        ['Fen', '6', 'Deney'],
      ]);

      const rows = await readExcel(file);

      expect(rows[0]).toEqual({ Ad: 'Matematik', Sınıf: '5', Açıklama: '' });
      expect(rows[1]).toEqual({ Ad: 'Fen', Sınıf: '6', Açıklama: 'Deney' });
    });

    it('onBulkImport_ValidWorkbook_FillsBulkImportDataWithMappedRows', async () => {
      const file = buildXlsxFile([
        ['Ad', 'Sınıf', 'Süre', 'Açıklama'],
        ['Matematik', '5', 30, null],
      ]);
      component.onBulkFileSelected({ target: { files: [file] } });

      await component.onBulkImport();

      expect(component.bulkImportData.length).toBe(1);
      const item = component.bulkImportData[0];
      expect(item.name).toBe('Matematik');
      expect(item.gradeId).toBe('5');
      expect(item.maxDurationMinutes).toBe(30);
      expect(item.description).toBe('');
      expect(item.__original).toEqual(jasmine.objectContaining({ name: 'Matematik' }));
    });

    // Regresyon ağı: __proto__ başlıklı bir sütun içeren dosyanın diğer sütunlarını okur ve
    // Object.prototype'u değiştirmediğini doğrular. CVE-2023-30533 kötü niyetli dosya yapısının
    // XLSX.read sırasındaki tetiklenmesini bu test SINAMAZ; yalnız bu girdiyle okumanın güvenli kaldığını gösterir.
    it('readExcelFile_ProtoHeader_ReadsOtherColumnsAndDoesNotPollutePrototype', async () => {
      const file = buildXlsxFile([
        ['Ad', '__proto__'],
        ['Matematik', 'polluted-value'],
      ]);

      const rows = await readExcel(file);

      expect(rows.length).toBe(1);
      expect(rows[0]['Ad']).toBe('Matematik');
      const probe: Record<string, unknown> = {};
      expect(probe['polluted']).toBeUndefined();
      expect(probe['polluted-value']).toBeUndefined();
      expect(Object.keys(Object.prototype)).toEqual([]);
    });
  });
});
