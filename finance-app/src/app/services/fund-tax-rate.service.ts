import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable, catchError, map, of, tap } from 'rxjs';
import { ApiService } from './api.service';

export interface FundTaxRateDto {
  id: string;
  assetId: string;
  assetSymbol: string;
  ratePercent: number;
  updatedAt: string | Date;
}

export interface UpsertFundTaxRateDto {
  ratePercent: number;
}

@Injectable({
  providedIn: 'root',
})
export class FundTaxRateService {
  private readonly rateMapSubject = new BehaviorSubject<Map<string, number>>(new Map());
  readonly rateMap$ = this.rateMapSubject.asObservable();

  private loaded = false;

  constructor(private apiService: ApiService) {}

  getRateMap(): Observable<Map<string, number>> {
    if (!this.loaded) {
      this.loaded = true;
      this.refresh().subscribe();
    }

    return this.rateMap$;
  }

  refresh(): Observable<Map<string, number>> {
    return this.apiService.get<FundTaxRateDto[]>('/fundtaxrates').pipe(
      map((items) => {
        const mapResult = new Map<string, number>();
        (items ?? []).forEach((x) => {
          mapResult.set(x.assetId, Number(x.ratePercent ?? 0));
        });
        return mapResult;
      }),
      tap((mapResult) => this.rateMapSubject.next(mapResult)),
      catchError((error) => {
        console.error('FundTaxRate API call failed:', error);
        const empty = new Map<string, number>();
        this.rateMapSubject.next(empty);
        return of(empty);
      })
    );
  }

  upsert(assetId: string, ratePercent: number): Observable<FundTaxRateDto> {
    const payload: UpsertFundTaxRateDto = {
      ratePercent: Number(ratePercent ?? 0),
    };

    return this.apiService.put<FundTaxRateDto>(`/fundtaxrates/${assetId}`, payload).pipe(
      tap(() => {
        // optimistic update of local cache
        const current = new Map(this.rateMapSubject.value);
        current.set(assetId, payload.ratePercent);
        this.rateMapSubject.next(current);
      })
    );
  }
}
