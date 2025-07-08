import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ExchangeRateService, ExchangeRate } from '../services/exchange-rate.service';
import { Subscription, interval } from 'rxjs';
import { startWith, switchMap } from 'rxjs/operators';

@Component({
  selector: 'app-currency-selector',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="currency-container">
      <!-- Para Birimi Seçici -->
      <div class="currency-selector-card">
        <h3>Display Currency</h3>
        <select class="currency-select" [(ngModel)]="selectedCurrency" (change)="onCurrencyChange($event)">
          <option *ngFor="let currency of supportedCurrencies" [value]="currency">
            {{ getCurrencyIcon(currency) }} {{ currency }}
          </option>
        </select>
      </div>

      <!-- Döviz Kurları -->
      <div class="exchange-rates-card">
        <div class="card-header">
          <h3>📈 Exchange Rates</h3>
          <small>Last updated: {{ lastUpdateTime | date : 'HH:mm:ss' }}</small>
        </div>
        <div class="exchange-rates-grid">
          <div
            *ngFor="let rate of displayedRates"
            class="exchange-rate-item"
            [class.positive]="rate.changeDirection === 'up'"
            [class.negative]="rate.changeDirection === 'down'"
          >
            <div class="rate-pair">
              <span class="from-currency">{{ rate.fromCurrency }}</span>
              <span class="arrow">→</span>
              <span class="to-currency">{{ rate.toCurrency }}</span>
            </div>
            <div class="rate-value">
              <span class="rate">{{ rate.rate | number : '1.4-4' }}</span>
              <span class="trend-icon" *ngIf="rate.changeDirection">
                {{ rate.changeDirection === 'up' ? '📈' : '📉' }}
              </span>
            </div>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [
    `
      .currency-container {
        display: flex;
        gap: 1rem;
        margin-bottom: 1rem;
        flex-wrap: wrap;
      }

      .currency-selector-card {
        min-width: 200px;
        flex: 0 0 auto;
        padding: 1rem;
        border: 1px solid #e0e0e0;
        border-radius: 8px;
        background: white;
        box-shadow: 0 2px 4px rgba(0, 0, 0, 0.1);
      }

      .currency-selector-card h3 {
        margin: 0 0 0.5rem 0;
        font-size: 1rem;
        color: #333;
      }

      .currency-select {
        width: 100%;
        padding: 0.5rem;
        border: 1px solid #ddd;
        border-radius: 4px;
        font-size: 1rem;
        background: white;
      }

      .exchange-rates-card {
        flex: 1;
        min-width: 300px;
        padding: 1rem;
        border: 1px solid #e0e0e0;
        border-radius: 8px;
        background: white;
        box-shadow: 0 2px 4px rgba(0, 0, 0, 0.1);
      }

      .card-header {
        margin-bottom: 1rem;
      }

      .card-header h3 {
        margin: 0 0 0.25rem 0;
        font-size: 1.1rem;
        color: #333;
      }

      .card-header small {
        color: #666;
        font-size: 0.85rem;
      }

      .exchange-rates-grid {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
        gap: 0.75rem;
      }

      .exchange-rate-item {
        display: flex;
        justify-content: space-between;
        align-items: center;
        padding: 12px;
        border-radius: 6px;
        background: #f8f9fa;
        transition: all 0.3s ease;
        border-left: 4px solid #e0e0e0;
      }

      .exchange-rate-item.positive {
        background: rgba(76, 175, 80, 0.1);
        border-left-color: #4caf50;
      }

      .exchange-rate-item.negative {
        background: rgba(244, 67, 54, 0.1);
        border-left-color: #f44336;
      }

      .rate-pair {
        display: flex;
        align-items: center;
        font-weight: 500;
        color: #333;
        gap: 0.25rem;
      }

      .from-currency {
        font-size: 0.9rem;
      }

      .arrow {
        color: #666;
        margin: 0 2px;
      }

      .to-currency {
        font-size: 0.9rem;
        color: #1976d2;
      }

      .rate-value {
        display: flex;
        align-items: center;
        gap: 0.25rem;
      }

      .rate {
        font-weight: 600;
        font-size: 1rem;
        color: #333;
      }

      .trend-icon {
        font-size: 1rem;
      }

      @media (max-width: 768px) {
        .currency-container {
          flex-direction: column;
        }

        .exchange-rates-grid {
          grid-template-columns: 1fr;
        }
      }
    `,
  ],
})
export class CurrencySelectorComponent implements OnInit, OnDestroy {
  selectedCurrency: string = 'TRY';
  supportedCurrencies: string[] = ['TRY', 'USD', 'EUR', 'GBP'];
  exchangeRates: ExchangeRate[] = [];
  displayedRates: any[] = [];
  lastUpdateTime: Date = new Date();

  private subscriptions: Subscription = new Subscription();
  private previousRates: Map<string, number> = new Map();

  constructor(private exchangeRateService: ExchangeRateService) {}

  ngOnInit(): void {
    // Kullanıcının seçili para birimini al
    this.subscriptions.add(
      this.exchangeRateService.selectedCurrency$.subscribe((currency) => {
        this.selectedCurrency = currency;
        this.updateDisplayedRates();
      })
    );

    // Döviz kurlarını periyodik olarak güncelle (her 30 saniyede)
    this.subscriptions.add(
      interval(30000)
        .pipe(
          startWith(0),
          switchMap(() => this.exchangeRateService.getAllExchangeRates())
        )
        .subscribe({
          next: (rates) => {
            this.updateRatesWithTrend(rates);
            this.lastUpdateTime = new Date();
          },
          error: (error) => {
            console.error('Error fetching exchange rates:', error);
          },
        })
    );
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
  }

  onCurrencyChange(event: any): void {
    const currency = event.target.value;
    this.exchangeRateService.setSelectedCurrency(currency);
  }

  getCurrencyIcon(currency: string): string {
    const icons: { [key: string]: string } = {
      TRY: '₺',
      USD: '$',
      EUR: '€',
      GBP: '£',
    };
    return icons[currency] || currency;
  }

  private updateRatesWithTrend(newRates: ExchangeRate[]): void {
    // Trend hesaplama
    newRates.forEach((rate) => {
      const key = `${rate.fromCurrency}-${rate.toCurrency}`;
      const previousRate = this.previousRates.get(key);

      if (previousRate !== undefined) {
        if (rate.rate > previousRate) {
          (rate as any).changeDirection = 'up';
        } else if (rate.rate < previousRate) {
          (rate as any).changeDirection = 'down';
        }
      }

      this.previousRates.set(key, rate.rate);
    });

    this.exchangeRates = newRates;
    this.updateDisplayedRates();
  }

  private updateDisplayedRates(): void {
    // Kullanıcının seçtiği para birimine göre önemli döviz çiftlerini göster
    const importantPairs = ['USD-TRY', 'EUR-TRY', 'GBP-TRY', 'EUR-USD', 'GBP-USD'];

    this.displayedRates = this.exchangeRates
      .filter((rate) => {
        const pair = `${rate.fromCurrency}-${rate.toCurrency}`;
        return importantPairs.includes(pair);
      })
      .map((rate) => ({
        ...rate,
        changeDirection: (rate as any).changeDirection,
      }));
  }
}
