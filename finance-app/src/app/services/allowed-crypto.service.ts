import { Injectable } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiService } from './api.service';

export interface AllowedCryptoDto {
  id: string;
  symbol: string;
  name: string;
  coinGeckoId: string;
  yahooSymbol: string;
  isEnabled: boolean;
}

@Injectable({
  providedIn: 'root',
})
export class AllowedCryptoService {
  constructor(private apiService: ApiService) {}

  getAll(): Observable<AllowedCryptoDto[]> {
    return this.apiService.get<AllowedCryptoDto[]>('/allowedcryptos');
  }

  getEnabled(): Observable<AllowedCryptoDto[]> {
    return this.getAll().pipe(map((items) => items.filter((x) => x.isEnabled)));
  }
}
