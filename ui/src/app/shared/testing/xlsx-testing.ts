import * as XLSX from 'xlsx';

export const XLSX_MIME = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';

/**
 * Testlerde kullanılmak üzere küçük bir .xlsx dosyasını gerçek SheetJS ile üretir.
 * İlk satır başlık, sonraki satırlar veri; `null` hücre boş bırakılır.
 */
export function buildXlsxFile(rows: unknown[][], fileName = 'tests.xlsx'): File {
  const wb = XLSX.utils.book_new();
  XLSX.utils.book_append_sheet(wb, XLSX.utils.aoa_to_sheet(rows), 'Sheet1');
  const bytes = XLSX.write(wb, { type: 'array', bookType: 'xlsx' }) as ArrayBuffer;
  return new File([bytes], fileName, { type: XLSX_MIME });
}
