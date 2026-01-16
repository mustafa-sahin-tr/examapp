import { Injectable } from '@angular/core';
import { Observable, combineLatest, map, catchError, of } from 'rxjs';
import { Portfolio, DashboardSummary, AssetType, TransactionType } from '../models/asset.model';
import { AssetService } from './asset.service';
import { TransactionService } from './transaction.service';
import { ApiService } from './api.service';
import { ExchangeRateService } from './exchange-rate.service';
import { FundTaxRateService } from './fund-tax-rate.service';

export interface HistoricalInvestment {
  assetId: string;
  assetSymbol: string;
  assetName: string;
  asset?: any;
  totalBought: number;
  totalSold: number;
  averageBuyPrice: number;
  averageSellPrice: number;
  realizedProfitLoss: number;
  realizedProfitLossPercentage: number;
  transactions: any[];
}

export interface EmailSummaryRequest {
  recipientEmail: string;
  subject?: string;
  message?: string;
  userId?: string;
}

@Injectable({
  providedIn: 'root',
})
export class PortfolioService {
  constructor(
    private assetService: AssetService,
    private transactionService: TransactionService,
    private apiService: ApiService,
    private exchangeRateService: ExchangeRateService,
    private fundTaxRateService: FundTaxRateService
  ) {}

  // Backend'de portfolio endpoint'i hazır olduğunda bu metodu kullanacağız
  private getPortfolioFromApi(): Observable<Portfolio[]> {
    return this.apiService.get<Portfolio[]>('/portfolio').pipe(
      catchError((error) => {
        console.error('Portfolio API call failed:', error);
        return of([]);
      })
    );
  }

  // Backend'de dashboard endpoint'i hazır olduğunda bu metodu kullanacağız
  private getDashboardFromApi(): Observable<DashboardSummary> {
    return this.apiService.get<DashboardSummary>('/dashboard').pipe(
      catchError((error) => {
        console.error('Dashboard API call failed:', error);
        return this.getDashboardSummary();
      })
    );
  }

  emailDashboardSummary(request: EmailSummaryRequest): Observable<void> {
    const body = {
      recipientEmail: request.recipientEmail,
      subject: request.subject,
      message: request.message,
      userId: request.userId ?? 'default-user',
    };

    return this.apiService.post<void>('/portfolio/email-summary', body);
  }

  getPortfolio(): Observable<Portfolio[]> {
    return combineLatest([
      this.assetService.getAssets(),
      this.transactionService.getTransactions(),
      this.fundTaxRateService.getRateMap(),
    ]).pipe(
      map(([assets, transactions, fundTaxRates]) => {
        const portfolioMap = new Map<string, Portfolio>();

        // Group transactions by asset
        transactions.forEach((transaction) => {
          let asset = null;
          let portfolioKey = '';

          if (transaction.assetId) {
            // Normal asset with ID
            asset = assets.find((a) => a.id === transaction.assetId);
            if (!asset) return;
            portfolioKey = transaction.assetId;
          } else {
            // Generic asset (Gold, FixedDeposit) without specific asset ID
            // Create a virtual asset based on transaction type
            const assetType = this.getAssetTypeFromTransaction(transaction);
            if (!assetType) return;

            portfolioKey = `generic_${assetType}`;
            asset = this.createVirtualAsset(assetType);
          }

          if (!portfolioMap.has(portfolioKey)) {
            portfolioMap.set(portfolioKey, {
              assetId: portfolioKey,
              assetSymbol: asset.symbol,
              assetName: asset.name,
              asset: asset,
              totalQuantity: 0,
              quantity: 0,
              averagePrice: 0,
              currentValue: 0,
              totalCost: 0,
              grossProfitLoss: 0,
              grossProfitLossPercentage: 0,
              withholdingTaxRatePercent: 0,
              netProfitLoss: 0,
              netProfitLossPercentage: 0,
              profitLoss: 0,
              profitLossPercentage: 0,
              currency: asset.currency,
              lastUpdated: new Date(),
              transactions: [],
            });
          }

          const portfolio = portfolioMap.get(portfolioKey)!;
          portfolio.transactions.push(transaction);

          // Calculate portfolio metrics
          if (
            transaction.type === TransactionType.BUY ||
            transaction.type === TransactionType.DEPOSIT_ADD ||
            transaction.type === TransactionType.DEPOSIT_INCOME
          ) {
            const totalCostBefore = portfolio.totalCost;
            const totalQuantityBefore = portfolio.totalQuantity;

            portfolio.totalQuantity += transaction.quantity;

            // Faiz geliri için cost'a eklemeyelim
            if (transaction.type !== TransactionType.DEPOSIT_INCOME) {
              portfolio.totalCost += transaction.price * transaction.quantity + (transaction.fees || 0);
            }

            // Recalculate average price
            if (portfolio.totalCost > 0) {
              portfolio.averagePrice = portfolio.totalCost / portfolio.totalQuantity;
            }
          } else if (
            transaction.type === TransactionType.SELL ||
            transaction.type === TransactionType.DEPOSIT_WITHDRAW
          ) {
            // SELL veya DEPOSIT_WITHDRAW
            portfolio.totalQuantity -= transaction.quantity;
            // For sells, we don't adjust total cost as it represents what we paid
          }
        });

        // Calculate current values, profit/loss
        const portfolios = Array.from(portfolioMap.values()).filter((p) => p.totalQuantity > 0);

        portfolios.forEach((portfolio) => {
          portfolio.currentValue = portfolio.totalQuantity * portfolio.asset!.currentPrice;

          const grossProfitLoss = portfolio.currentValue - portfolio.totalCost;
          const grossProfitLossPercentage = portfolio.totalCost > 0 ? (grossProfitLoss / portfolio.totalCost) * 100 : 0;

          const assetType = portfolio.asset?.type;
          const withholdingRatePercent =
            assetType === AssetType.Fund ? Number(fundTaxRates.get(portfolio.asset!.id) ?? 0) : 0;

          const netProfitLoss = this.applyWithholdingTax(grossProfitLoss, assetType, withholdingRatePercent);
          const netProfitLossPercentage = portfolio.totalCost > 0 ? (netProfitLoss / portfolio.totalCost) * 100 : 0;

          portfolio.grossProfitLoss = grossProfitLoss;
          portfolio.grossProfitLossPercentage = grossProfitLossPercentage;
          portfolio.withholdingTaxRatePercent = withholdingRatePercent;
          portfolio.netProfitLoss = netProfitLoss;
          portfolio.netProfitLossPercentage = netProfitLossPercentage;

          // UI: profitLoss alanı fonlar için net, diğerleri için brüt
          portfolio.profitLoss = assetType === AssetType.Fund ? netProfitLoss : grossProfitLoss;
          portfolio.profitLossPercentage =
            assetType === AssetType.Fund ? netProfitLossPercentage : grossProfitLossPercentage;
          portfolio.quantity = portfolio.totalQuantity; // quantity ile totalQuantity aynı olsun
        });

        return portfolios;
      })
    );
  }

  private applyWithholdingTax(grossProfitLoss: number, assetType: AssetType | undefined, ratePercent: number): number {
    if (assetType !== AssetType.Fund) {
      return grossProfitLoss;
    }

    // zarar varsa stopaj uygulanmaz
    if (grossProfitLoss <= 0) {
      return grossProfitLoss;
    }

    const rate = Math.min(100, Math.max(0, Number(ratePercent ?? 0))) / 100;
    return grossProfitLoss * (1 - rate);
  }

  getHistoricalInvestments(): Observable<HistoricalInvestment[]> {
    return combineLatest([this.assetService.getAssets(), this.transactionService.getTransactions()]).pipe(
      map(([assets, transactions]) => {
        const historicalMap = new Map<string, HistoricalInvestment>();

        // Group transactions by asset
        transactions.forEach((transaction) => {
          const asset = assets.find((a) => a.id === transaction.assetId);
          if (!asset) return;

          if (!historicalMap.has(transaction.assetId)) {
            historicalMap.set(transaction.assetId, {
              assetId: transaction.assetId,
              assetSymbol: asset.symbol,
              assetName: asset.name,
              asset: asset,
              totalBought: 0,
              totalSold: 0,
              averageBuyPrice: 0,
              averageSellPrice: 0,
              realizedProfitLoss: 0,
              realizedProfitLossPercentage: 0,
              transactions: [],
            });
          }

          const historical = historicalMap.get(transaction.assetId)!;
          historical.transactions.push(transaction);
        });

        // Calculate historical metrics
        const historicalInvestments = Array.from(historicalMap.values());

        historicalInvestments.forEach((historical) => {
          const buyTransactions = historical.transactions.filter((t) => t.type === TransactionType.BUY);
          const sellTransactions = historical.transactions.filter((t) => t.type === TransactionType.SELL);

          // Calculate totals
          historical.totalBought = buyTransactions.reduce((sum, t) => sum + t.quantity, 0);
          historical.totalSold = sellTransactions.reduce((sum, t) => sum + t.quantity, 0);

          // Calculate average prices
          if (buyTransactions.length > 0) {
            const totalBuyValue = buyTransactions.reduce((sum, t) => sum + t.quantity * t.price, 0);
            historical.averageBuyPrice = totalBuyValue / historical.totalBought;
          }

          if (sellTransactions.length > 0) {
            const totalSellValue = sellTransactions.reduce((sum, t) => sum + t.quantity * t.price, 0);
            historical.averageSellPrice = totalSellValue / historical.totalSold;
          }

          // Calculate realized profit/loss
          const soldQuantity = Math.min(historical.totalBought, historical.totalSold);
          if (soldQuantity > 0 && historical.averageBuyPrice > 0 && historical.averageSellPrice > 0) {
            const costBasis = soldQuantity * historical.averageBuyPrice;
            const saleValue = soldQuantity * historical.averageSellPrice;
            historical.realizedProfitLoss = saleValue - costBasis;
            historical.realizedProfitLossPercentage = (historical.realizedProfitLoss / costBasis) * 100;
          }
        });

        // Only return assets that have been completely sold (totalSold >= totalBought)
        return historicalInvestments.filter((h) => h.totalSold >= h.totalBought && h.totalSold > 0);
      })
    );
  }

  getTotalProfitLoss(): Observable<{ totalPL: number; realizedPL: number; unrealizedPL: number }> {
    return combineLatest([this.getPortfolio(), this.getHistoricalInvestments()]).pipe(
      map(([currentPortfolio, historicalInvestments]) => {
        const unrealizedPL = currentPortfolio.reduce((sum, p) => sum + p.profitLoss, 0);
        const realizedPL = historicalInvestments.reduce((sum, h) => sum + h.realizedProfitLoss, 0);
        const totalPL = unrealizedPL + realizedPL;

        return {
          totalPL,
          realizedPL,
          unrealizedPL,
        };
      })
    );
  }

  getPortfolioByType(type: AssetType): Observable<Portfolio[]> {
    return this.getPortfolio().pipe(
      map((portfolios) => {
        return portfolios.filter((p) => {
          // Normal asset'ler için type kontrolü
          if (p.asset?.type === type) {
            return true;
          }

          // Virtual asset'ler için özel kontrol
          if (type === AssetType.FixedDeposit && p.assetId === 'generic_5') {
            return true;
          }

          if (type === AssetType.Gold && p.assetId === 'generic_2') {
            return true;
          }

          return false;
        });
      })
    );
  }

  getHistoricalInvestmentsByType(type: AssetType): Observable<HistoricalInvestment[]> {
    return this.getHistoricalInvestments().pipe(
      map((historicals) => historicals.filter((h) => h.asset?.type === type))
    );
  }

  getDashboardSummary(): Observable<DashboardSummary> {
    const selectedCurrency = this.exchangeRateService.getSelectedCurrency();

    return this.getPortfolio().pipe(
      map((portfolios) => {
        // Para birimi dönüşümü UI'da cache'deki exchange rate'leri kullanarak yapılır
        const convertedPortfolios = portfolios.map((portfolio) => {
          const convertedCurrentValue = this.exchangeRateService.convertToSelectedCurrencyWithCache(
            portfolio.currentValue,
            portfolio.currency
          );
          const convertedTotalCost = this.exchangeRateService.convertToSelectedCurrencyWithCache(
            portfolio.totalCost,
            portfolio.currency
          );

          return {
            ...portfolio,
            convertedCurrentValue,
            convertedTotalCost,
          };
        });

        // Toplam hesaplamalar
        const totalValue = convertedPortfolios.reduce((sum, p) => sum + p.convertedCurrentValue, 0);
        const totalCost = convertedPortfolios.reduce((sum, p) => sum + p.convertedTotalCost, 0);
        const totalProfitLoss = totalValue - totalCost;
        const totalProfitLossPercentage = totalCost > 0 ? (totalProfitLoss / totalCost) * 100 : 0;

        // Asset type'a göre gruplama (dönüştürülmüş değerlerle)
        const portfoliosByType = new Map<AssetType, Portfolio[]>();
        convertedPortfolios.forEach((portfolio) => {
          if (portfolio.asset) {
            const type = portfolio.asset.type;
            if (!portfoliosByType.has(type)) {
              portfoliosByType.set(type, []);
            }

            // Portfolio'ya dönüştürülmüş değerleri ekle (fonlarda net kâr/zarar korunur)
            const convertedGrossProfitLoss = portfolio.convertedCurrentValue - portfolio.convertedTotalCost;
            const convertedGrossProfitLossPercentage =
              portfolio.convertedTotalCost > 0 ? (convertedGrossProfitLoss / portfolio.convertedTotalCost) * 100 : 0;

            const withholdingRatePercent = Number((portfolio as any).withholdingTaxRatePercent ?? 0);
            const convertedNetProfitLoss = this.applyWithholdingTax(
              convertedGrossProfitLoss,
              type,
              withholdingRatePercent
            );
            const convertedNetProfitLossPercentage =
              portfolio.convertedTotalCost > 0 ? (convertedNetProfitLoss / portfolio.convertedTotalCost) * 100 : 0;

            const convertedProfitLoss = type === AssetType.Fund ? convertedNetProfitLoss : convertedGrossProfitLoss;
            const convertedProfitLossPercentage =
              type === AssetType.Fund ? convertedNetProfitLossPercentage : convertedGrossProfitLossPercentage;

            const portfolioWithConversion = {
              ...portfolio,
              currentValue: portfolio.convertedCurrentValue,
              totalCost: portfolio.convertedTotalCost,
              grossProfitLoss: convertedGrossProfitLoss,
              grossProfitLossPercentage: convertedGrossProfitLossPercentage,
              netProfitLoss: convertedNetProfitLoss,
              netProfitLossPercentage: convertedNetProfitLossPercentage,
              profitLoss: convertedProfitLoss,
              profitLossPercentage: convertedProfitLossPercentage,
              currency: selectedCurrency,
            };
            portfoliosByType.get(type)!.push(portfolioWithConversion);
          }
        });

        return {
          totalValue,
          totalCost,
          totalProfitLoss,
          totalProfitLossPercentage,
          portfoliosByType,
          displayCurrency: selectedCurrency,
        };
      })
    );
  }

  // Helper fonksiyonlar
  private getAssetTypeFromTransaction(transaction: any): AssetType | null {
    // Transaction'ın type'ına göre asset type'ını belirle
    if (
      transaction.type === TransactionType.DEPOSIT_ADD ||
      transaction.type === TransactionType.DEPOSIT_WITHDRAW ||
      transaction.type === TransactionType.DEPOSIT_INCOME
    ) {
      return AssetType.FixedDeposit;
    }

    // Diğer generic asset type'lar için gelecekte eklenebilir
    // Şimdilik Gold için özel bir logic yok, çünkü Gold da normal asset olarak ekleniyor

    return null;
  }

  private createVirtualAsset(assetType: AssetType): any {
    switch (assetType) {
      case AssetType.FixedDeposit:
        return {
          id: 'virtual_fixed_deposit',
          symbol: 'DEPOSIT',
          name: 'Vadeli Mevduat',
          type: AssetType.FixedDeposit,
          currentPrice: 1, // Vadeli mevduat için fiyat 1 (TRY bazında)
          currency: 'TRY',
          lastUpdated: new Date(),
          changePercentage: 0,
          changeValue: 0,
        };
      case AssetType.Gold:
        return {
          id: 'virtual_gold',
          symbol: 'GOLD',
          name: 'Altın',
          type: AssetType.Gold,
          currentPrice: 2500, // Default gold price - gerçek fiyat API'den alınmalı
          currency: 'TRY',
          lastUpdated: new Date(),
          changePercentage: 0,
          changeValue: 0,
        };
      default:
        return null;
    }
  }
}
