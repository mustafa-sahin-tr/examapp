import { Component, OnInit, OnDestroy, Input, ViewChild, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { NgxChartsModule } from '@swimlane/ngx-charts';
import {
  ProfitLossService,
  ProfitLossHistoryDto,
  AssetTypeProfitLossDto,
  AssetProfitLossDto,
} from '../services/profit-loss.service';
import { ExchangeRateService } from '../services/exchange-rate.service';
import { Subject, takeUntil } from 'rxjs';

@Component({
  selector: 'app-profit-loss-chart',
  standalone: true,
  imports: [CommonModule, NgxChartsModule],
  template: `
    <div class="profit-loss-chart-container">
      <div class="chart-header">
        <h3>{{ title }}</h3>
        <div class="chart-controls">
          <select (change)="onTimeFrameChange($event)" [value]="timeFrame">
            <option value="hourly">Saatlik</option>
            <option value="daily">Günlük</option>
            <option value="weekly">Haftalık</option>
            <option value="monthly">Aylık</option>
          </select>

          <select (change)="onViewTypeChange($event)" [value]="viewType">
            <option value="total">Toplam</option>
            <option value="asset-type">Asset Tipi</option>
            <option value="asset">Asset Bazlı</option>
          </select>

          <select *ngIf="viewType === 'asset-type'" (change)="onAssetTypeChange($event)" [value]="selectedAssetType">
            <option value="">Tümü</option>
            <option value="Stock">BIST 100</option>
            <option value="USStock">US Stocks</option>
            <option value="Gold">Gold</option>
            <option value="Silver">Silver</option>
            <option value="Fund">Funds</option>
            <option value="FixedDeposit">Vadeli Mevduat</option>
          </select>
        </div>
      </div>

      <div class="chart-content">
        <div class="summary-cards" *ngIf="convertedSummary">
          <div
            class="summary-card"
            [ngClass]="{ profit: convertedSummary.totalProfitLoss > 0, loss: convertedSummary.totalProfitLoss < 0 }"
          >
            <div class="label">Toplam Kar/Zarar</div>
            <div class="value">{{ formatCurrency(convertedSummary.totalProfitLoss) }}</div>
            <div class="percentage">{{ convertedSummary.profitLossPercentage | number : '1.2-2' }}%</div>
          </div>

          <div class="summary-card">
            <div class="label">Toplam Yatırım</div>
            <div class="value">{{ formatCurrency(convertedSummary.totalInvestment) }}</div>
          </div>

          <div class="summary-card">
            <div class="label">Güncel Değer</div>
            <div class="value">{{ formatCurrency(convertedSummary.totalCurrentValue) }}</div>
          </div>
        </div>

        <div class="chart-wrapper">
          <ngx-charts-line-chart
            [view]="[800, 400]"
            [results]="chartData"
            [gradient]="false"
            [xAxis]="true"
            [yAxis]="true"
            [legend]="true"
            [showXAxisLabel]="true"
            [showYAxisLabel]="true"
            [xAxisLabel]="'Zaman'"
            [yAxisLabel]="'Kar/Zarar'"
            [autoScale]="true"
            [timeline]="true"
          >
          </ngx-charts-line-chart>
        </div>

        <div class="asset-breakdown" *ngIf="assetTypeBreakdown.length > 0">
          <h4>Asset Tipi Dağılımı</h4>
          <ngx-charts-pie-chart
            [view]="[400, 300]"
            [results]="assetTypeBreakdown"
            [legend]="true"
            [labels]="true"
            [doughnut]="true"
          >
          </ngx-charts-pie-chart>
        </div>
      </div>
    </div>
  `,
  styles: [
    `
      .profit-loss-chart-container {
        padding: 20px;
        background: white;
        border-radius: 8px;
        box-shadow: 0 2px 4px rgba(0, 0, 0, 0.1);
      }

      .chart-header {
        display: flex;
        justify-content: space-between;
        align-items: center;
        margin-bottom: 20px;
      }

      .chart-controls {
        display: flex;
        gap: 10px;
      }

      .chart-controls select {
        padding: 8px 12px;
        border: 1px solid #ddd;
        border-radius: 4px;
        background: white;
      }

      .summary-cards {
        display: flex;
        gap: 20px;
        margin-bottom: 20px;
      }

      .summary-card {
        flex: 1;
        padding: 16px;
        background: #f8f9fa;
        border-radius: 8px;
        text-align: center;
      }

      .summary-card.profit {
        background: #d4edda;
        border: 1px solid #c3e6cb;
      }

      .summary-card.loss {
        background: #f8d7da;
        border: 1px solid #f5c6cb;
      }

      .summary-card .label {
        font-size: 12px;
        color: #666;
        margin-bottom: 8px;
      }

      .summary-card .value {
        font-size: 18px;
        font-weight: bold;
        color: #333;
      }

      .summary-card .percentage {
        font-size: 14px;
        color: #666;
        margin-top: 4px;
      }

      .chart-wrapper {
        margin: 20px 0;
      }

      .asset-breakdown {
        margin-top: 30px;
        text-align: center;
      }

      .asset-breakdown h4 {
        margin-bottom: 20px;
        color: #333;
      }
    `,
  ],
})
export class ProfitLossChartComponent implements OnInit, OnDestroy {
  @Input() title: string = 'Kar/Zarar Geçmişi';
  @Input() userId: string = 'default-user';
  @Input() timeFrame: 'hourly' | 'daily' | 'weekly' | 'monthly' = 'daily';
  @Input() viewType: 'total' | 'asset-type' | 'asset' = 'total';
  @Input() selectedAssetType: string = '';
  @Input() selectedAssetId: string = '';

  private destroy$ = new Subject<void>();

  chartData: any[] = [];
  assetTypeBreakdown: any[] = [];
  currentSummary: ProfitLossHistoryDto | null = null;
  convertedSummary: any = null;

  colorScheme = {
    domain: ['#5AA454', '#A10A28', '#C7B42C', '#AAAAAA', '#1f77b4', '#ff7f0e', '#2ca02c', '#d62728'],
  };

  constructor(private profitLossService: ProfitLossService, private exchangeRateService: ExchangeRateService) {}

  ngOnInit() {
    this.loadData();

    // Subscribe to currency changes
    this.exchangeRateService.selectedCurrency$.pipe(takeUntil(this.destroy$)).subscribe(() => {
      this.loadData();
    });
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }

  formatCurrency(amount: number, currency?: string): string {
    const selectedCurrency = currency || this.exchangeRateService.getSelectedCurrency();
    return new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency: selectedCurrency,
    }).format(amount);
  }

  private async convertCurrency(amount: number): Promise<number> {
    // Cache'deki exchange rate ile dönüşüm yap, backend çağrısı yapma
    return this.exchangeRateService.convertToSelectedCurrencyWithCache(amount, 'TRY');
  }

  private convertSummaryValues(summary: ProfitLossHistoryDto): void {
    // Cache'deki exchange rate'leri kullanarak dönüşüm yap
    this.convertedSummary = {
      ...summary,
      totalProfitLoss: this.exchangeRateService.convertToSelectedCurrencyWithCache(summary.totalProfitLoss, 'TRY'),
      totalInvestment: this.exchangeRateService.convertToSelectedCurrencyWithCache(summary.totalInvestment, 'TRY'),
      totalCurrentValue: this.exchangeRateService.convertToSelectedCurrencyWithCache(summary.totalCurrentValue, 'TRY'),
    };
  }

  onTimeFrameChange(event: Event) {
    const target = event.target as HTMLSelectElement;
    this.timeFrame = target.value as any;
    this.loadData();
  }

  onViewTypeChange(event: Event) {
    const target = event.target as HTMLSelectElement;
    this.viewType = target.value as any;
    this.loadData();
  }

  onAssetTypeChange(event: Event) {
    const target = event.target as HTMLSelectElement;
    this.selectedAssetType = target.value;
    this.loadData();
  }

  private loadData() {
    const startDate = this.getStartDate();
    const endDate = new Date();

    // Güncel özeti yükle
    this.profitLossService
      .getCurrentProfitLoss(this.userId)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (summary) => {
          this.currentSummary = summary;
          this.convertSummaryValues(summary);
          this.prepareAssetTypeBreakdown(summary);
        },
        error: (error) => console.error('Error loading current summary:', error),
      });

    // Görünüm tipine göre data yükle
    switch (this.viewType) {
      case 'total':
        this.loadTotalProfitLossData(startDate, endDate);
        break;
      case 'asset-type':
        if (this.selectedAssetType) {
          this.loadAssetTypeProfitLossData(this.selectedAssetType, startDate, endDate);
        } else {
          this.loadTotalProfitLossData(startDate, endDate);
        }
        break;
      case 'asset':
        if (this.selectedAssetId) {
          this.loadAssetProfitLossData(this.selectedAssetId, startDate, endDate);
        } else {
          this.loadTotalProfitLossData(startDate, endDate);
        }
        break;
    }
  }

  private loadTotalProfitLossData(startDate: Date, endDate: Date) {
    this.profitLossService
      .getProfitLossHistory(this.userId, startDate, endDate, this.timeFrame)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (data) => {
          // Cache'deki exchange rate'leri kullanarak dönüşüm yap, backend çağrısı yapma
          this.chartData = [
            {
              name: 'Toplam Kar/Zarar',
              series: data.map((item) => ({
                name: new Date(item.timestamp),
                value: this.exchangeRateService.convertToSelectedCurrencyWithCache(item.totalProfitLoss, 'TRY'),
              })),
            },
          ];
        },
        error: (error) => console.error('Error loading total profit/loss data:', error),
      });
  }

  private loadAssetTypeProfitLossData(assetType: string, startDate: Date, endDate: Date) {
    this.profitLossService
      .getAssetTypeProfitLossHistory(assetType, this.userId, startDate, endDate, this.timeFrame)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (data) => {
          // Cache'deki exchange rate'leri kullanarak dönüşüm yap, backend çağrısı yapma
          this.chartData = [
            {
              name: `${assetType} Kar/Zarar`,
              series: data.map((item) => ({
                name: new Date(item.timestamp),
                value: this.exchangeRateService.convertToSelectedCurrencyWithCache(item.profitLoss, 'TRY'),
              })),
            },
          ];
        },
        error: (error) => console.error('Error loading asset type profit/loss data:', error),
      });
  }

  private loadAssetProfitLossData(assetId: string, startDate: Date, endDate: Date) {
    this.profitLossService
      .getAssetProfitLossHistory(assetId, this.userId, startDate, endDate, this.timeFrame)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (data) => {
          if (data.length > 0) {
            // Cache'deki exchange rate'leri kullanarak dönüşüm yap, backend çağrısı yapma
            this.chartData = [
              {
                name: `${data[0].assetSymbol} Kar/Zarar`,
                series: data.map((item) => ({
                  name: new Date(item.timestamp),
                  value: this.exchangeRateService.convertToSelectedCurrencyWithCache(item.profitLoss, 'TRY'),
                })),
              },
            ];
          }
        },
        error: (error) => console.error('Error loading asset profit/loss data:', error),
      });
  }

  private prepareAssetTypeBreakdown(summary: ProfitLossHistoryDto) {
    this.assetTypeBreakdown = summary.assetTypeBreakdown.map((item) => ({
      name: this.getAssetTypeLabel(item.assetType),
      value: Math.abs(this.exchangeRateService.convertToSelectedCurrencyWithCache(item.profitLoss, 'TRY')),
    }));
  }

  private getAssetTypeLabel(assetType: string): string {
    const labels: { [key: string]: string } = {
      Stock: 'BIST 100',
      USStock: 'US Stocks',
      Gold: 'Altın',
      Silver: 'Gümüş',
      Fund: 'Fonlar',
      FixedDeposit: 'Vadeli Mevduat',
    };
    return labels[assetType] || assetType;
  }

  private getStartDate(): Date {
    const now = new Date();
    switch (this.timeFrame) {
      case 'hourly':
        return new Date(now.getTime() - 24 * 60 * 60 * 1000); // 24 saat
      case 'daily':
        return new Date(now.getTime() - 7 * 24 * 60 * 60 * 1000); // 7 gün
      case 'weekly':
        return new Date(now.getTime() - 30 * 24 * 60 * 60 * 1000); // 30 gün
      case 'monthly':
        return new Date(now.getTime() - 365 * 24 * 60 * 60 * 1000); // 365 gün
      default:
        return new Date(now.getTime() - 7 * 24 * 60 * 60 * 1000);
    }
  }
}
