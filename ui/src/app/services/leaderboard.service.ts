import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { LeaderboardErrorBody, LeaderboardResult, LeaderboardScope } from '../models/leaderboard.model';

/** Backend varsayılanı ile aynı (`LeaderboardService.DefaultTake`); üst sınır 100. */
export const LEADERBOARD_PAGE_SIZE = 20;

/**
 * Liderlik tablosu ucu (issue #193). Gateway üzerinden `/api/exam/leaderboard`.
 * Okul kimliği ASLA gönderilmez — `scope=school` verildiğinde sunucu istek sahibinin okulunu kendisi çözer.
 * 400 (`okulsuz istekçi + scope=school`, geçersiz scope/paging) `HttpErrorResponse` olarak akar;
 * gövdedeki mesaj `extractError` ile okunur.
 */
@Injectable({ providedIn: 'root' })
export class LeaderboardService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/exam/leaderboard';

  getLeaderboard(scope: LeaderboardScope, skip = 0, take = LEADERBOARD_PAGE_SIZE): Observable<LeaderboardResult> {
    const params = new HttpParams().set('scope', scope).set('skip', String(skip)).set('take', String(take));
    return this.http.get<LeaderboardResult>(this.baseUrl, { params });
  }

  /**
   * `{ message }` gövdesindeki sunucu mesajını döner; yoksa `null`. Çevrili varsayılan metin
   * şablonda (`t('error')`) çözülür — scope sözlüğü yüklenmeden ham anahtar sızmasın diye burada üretilmez.
   */
  extractError(err: HttpErrorResponse): string | null {
    const body = err.error as LeaderboardErrorBody | null;
    const message = body?.message?.trim();
    return message ? message : null;
  }
}
