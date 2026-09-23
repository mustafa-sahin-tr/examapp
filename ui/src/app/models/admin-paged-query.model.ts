/**
 * Okul filtreli admin listelerinin (öğretmen #152, öğrenci #153) ortak sorgu parametreleri.
 * `schoolId` ile `unassigned=true` birlikte gönderilemez (backend 400 döner).
 * `page` 1-tabanlı; `pageSize` backend'de 1..100 aralığına kırpılır.
 */
export interface AdminSchoolPagedQuery {
  page: number;
  pageSize: number;
  schoolId?: number | null;
  unassigned?: boolean;
}
