import { Injectable } from '@angular/core';
import { Observable, BehaviorSubject } from 'rxjs';
import { ApiService } from './api.service';

export interface ExchangeRate {
  id: string;
  fromCurrency: string;
  toCurrency: string;
  rate: number;
  lastUpdated: Date;
}

export interface CurrencyConversionRequest {
  amount: number;
  fromCurrency: string;
  toCurrency: string;
}

export interface CurrencyConversionResponse {
  originalAmount: number;
  convertedAmount: number;
  fromCurrency: string;
  toCurrency: string;
  rate: number;
}

@Injectable({
  providedIn: 'root',
})
export class ExchangeRateService {
  private selectedCurrencySubject = new BehaviorSubject<string>('TRY');
  public selectedCurrency$ = this.selectedCurrencySubject.asObservable();

  private exchangeRatesSubject = new BehaviorSubject<ExchangeRate[]>([]);
  public exchangeRates$ = this.exchangeRatesSubject.asObservable();

  constructor(private apiService: ApiService) {
    // LocalStorage'dan kullanıcının tercih ettiği para birimini al
    const savedCurrency = localStorage.getItem('selectedCurrency');
    if (savedCurrency) {
      this.selectedCurrencySubject.next(savedCurrency);
    }

    // Başlangıçta döviz kurlarını yükle
    this.loadExchangeRates();
  }

  /**
   * Kullanıcının seçtiği para birimini ayarlar
   */
  setSelectedCurrency(currency: string): void {
    this.selectedCurrencySubject.next(currency);
    localStorage.setItem('selectedCurrency', currency);
  }

  /**
   * Kullanıcının seçtiği para birimini döndürür
   */
  getSelectedCurrency(): string {
    return this.selectedCurrencySubject.value;
  }

  /**
   * Tüm döviz kurlarını getirir
   */
  getAllExchangeRates(): Observable<ExchangeRate[]> {
    return this.apiService.get<ExchangeRate[]>('/exchangerate');
  }

  /**
   * Belirli döviz çifti için kur getirir
   */
  getExchangeRate(fromCurrency: string, toCurrency: string): Observable<ExchangeRate> {
    return this.apiService.get<ExchangeRate>(`/exchangerate/${fromCurrency}/${toCurrency}`);
  }

  /**
   * Para birimi dönüştürme
   */
  convertCurrency(request: CurrencyConversionRequest): Observable<CurrencyConversionResponse> {
    return this.apiService.post<CurrencyConversionResponse>('/exchangerate/convert', request);
  }

  /**
   * Döviz kurlarını manuel günceller
   */
  updateExchangeRates(): Observable<string> {
    return this.apiService.post<string>('/exchangerate/update', {});
  }

  /**
   * Belirli miktarı kullanıcının seçtiği para birimine dönüştürür
   */
  convertToSelectedCurrency(amount: number, fromCurrency: string): Observable<number> {
    const selectedCurrency = this.getSelectedCurrency();

    if (fromCurrency === selectedCurrency) {
      return new Observable((observer) => {
        observer.next(amount);
        observer.complete();
      });
    }

    return new Observable((observer) => {
      this.convertCurrency({
        amount,
        fromCurrency,
        toCurrency: selectedCurrency,
      }).subscribe({
        next: (response) => {
          observer.next(response.convertedAmount);
          observer.complete();
        },
        error: (error) => {
          console.error('Currency conversion error:', error);
          observer.next(amount); // Hata durumunda orijinal değeri döndür
          observer.complete();
        },
      });
    });
  }

  /**
   * Döviz kurlarını yükler ve cache'ler
   */
  private loadExchangeRates(): void {
    this.getAllExchangeRates().subscribe({
      next: (rates) => {
        this.exchangeRatesSubject.next(rates);
      },
      error: (error) => {
        console.error('Error loading exchange rates:', error);
      },
    });
  }

  /**
   * Cache'deki döviz kurlarını döndürür
   */
  getCachedExchangeRates(): ExchangeRate[] {
    return this.exchangeRatesSubject.value;
  }

  /**
   * Belirli döviz çifti için cache'den kur bulur
   */
  getCachedExchangeRate(fromCurrency: string, toCurrency: string): ExchangeRate | null {
    const rates = this.getCachedExchangeRates();
    return rates.find((r) => r.fromCurrency === fromCurrency && r.toCurrency === toCurrency) || null;
  }

  /**
   * Desteklenen para birimlerini döndürür
   */
  getSupportedCurrencies(): string[] {
    return ['TRY', 'USD', 'EUR', 'GBP'];
  }
}
