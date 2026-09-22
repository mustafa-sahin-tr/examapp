/**
 * Müsaitlik taslağı testleri için saat dilimine bağımsız yardımcılar (issue #176).
 * Taslak UTC gün sınırını aşamaz ve bu sınır dilime göre yerel saatin farklı yerine düşer; testler sabit
 * yerel saat yerine buradaki güvenli saati kullanır ki Karma'nın koştuğu makinenin dilimi sonucu etkilemesin.
 */

/** [H, H+5) yerel penceresi tek bir UTC gününde kalacak şekilde H (9 ya da 15) döner. */
export function safeLocalHour(day: Date): number {
  const offsetMinutes = -day.getTimezoneOffset();
  const utcMidnightLocalHour = (((offsetMinutes % 1440) + 1440) % 1440) / 60;
  return utcMidnightLocalHour > 9 && utcMidnightLocalHour <= 14 ? 15 : 9;
}

/** "09:30" — onay çubuğundaki 2 haneli saat biçimi. */
export function hhmm(hour: number, minute = 0): string {
  return `${`${hour}`.padStart(2, '0')}:${`${minute}`.padStart(2, '0')}`;
}

/** Beklenen `POST /booking/slots` gövdesi: taslak anlarının UTC bileşenleri (backend date+time'ı UTC sayar). */
export function utcRequest(start: Date, end: Date): { date: string; startTime: string; endTime: string } {
  return {
    date: start.toISOString().slice(0, 10),
    startTime: start.toISOString().slice(11, 19),
    endTime: end.toISOString().slice(11, 19),
  };
}
