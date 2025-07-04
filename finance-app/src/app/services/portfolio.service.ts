import { Injectable } from '@angular/core';
import { Observable, combineLatest, map, catchError, of } from 'rxjs';
import { Portfolio, DashboardSummary, AssetType, TransactionType } from '../models/asset.model';
import { AssetService } from './asset.service';
import { TransactionService } from './transaction.service';
import { ApiService } from './api.service';

export interface HistoricalInvestment {
  assetId: number;
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

@Injectable({
  providedIn: 'root',
})
export class PortfolioService {
  constructor(
    private assetService: AssetService,
    private transactionService: TransactionService,
    private apiService: ApiService
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

  getPortfolio(): Observable<Portfolio[]> {
    return combineLatest([this.assetService.getAssets(), this.transactionService.getTransactions()]).pipe(
      map(([assets, transactions]) => {
        const portfolioMap = new Map<string, Portfolio>();

        // Group transactions by asset
        transactions.forEach((transaction) => {
          const asset = assets.find((a) => a.id === transaction.assetId);
          if (!asset) return;

          if (!portfolioMap.has(transaction.assetId)) {
            portfolioMap.set(transaction.assetId, {
              assetId: transaction.assetId,
              asset: asset,
              totalQuantity: 0,
              averagePrice: 0,
              currentValue: 0,
              totalCost: 0,
              profitLoss: 0,
              profitLossPercentage: 0,
              transactions: [],
            });
          }

          const portfolio = portfolioMap.get(transaction.assetId)!;
          portfolio.transactions.push(transaction);

          // Calculate portfolio metrics
          if (transaction.type === TransactionType.BUY) {
            const totalCostBefore = portfolio.totalCost;
            const totalQuantityBefore = portfolio.totalQuantity;

            portfolio.totalQuantity += transaction.quantity;
            portfolio.totalCost += transaction.price * transaction.quantity + (transaction.fees || 0);

            // Recalculate average price
            portfolio.averagePrice = portfolio.totalCost / portfolio.totalQuantity;
          } else {
            // SELL
            portfolio.totalQuantity -= transaction.quantity;
            // For sells, we don't adjust total cost as it represents what we paid
          }
        });

        // Calculate current values, profit/loss
        const portfolios = Array.from(portfolioMap.values()).filter((p) => p.totalQuantity > 0);

        portfolios.forEach((portfolio) => {
          portfolio.currentValue = portfolio.totalQuantity * portfolio.asset!.currentPrice;
          portfolio.profitLoss = portfolio.currentValue - portfolio.averagePrice * portfolio.totalQuantity;
          portfolio.profitLossPercentage =
            (portfolio.currentValue / (portfolio.averagePrice * portfolio.totalQuantity) - 1) * 100;
        });

        return portfolios;
      })
    );
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
    return this.getPortfolio().pipe(map((portfolios) => portfolios.filter((p) => p.asset?.type === type)));
  }

  getHistoricalInvestmentsByType(type: AssetType): Observable<HistoricalInvestment[]> {
    return this.getHistoricalInvestments().pipe(
      map((historicals) => historicals.filter((h) => h.asset?.type === type))
    );
  }

  getDashboardSummary(): Observable<DashboardSummary> {
    return this.getPortfolio().pipe(
      map((portfolios) => {
        const totalValue = portfolios.reduce((sum, p) => sum + p.currentValue, 0);
        const totalCost = portfolios.reduce((sum, p) => sum + p.averagePrice * p.totalQuantity, 0);
        const totalProfitLoss = totalValue - totalCost;
        const totalProfitLossPercentage = totalCost > 0 ? (totalProfitLoss / totalCost) * 100 : 0;

        // Group by asset type
        const portfoliosByType = new Map<AssetType, Portfolio[]>();
        portfolios.forEach((portfolio) => {
          if (portfolio.asset) {
            const type = portfolio.asset.type;
            if (!portfoliosByType.has(type)) {
              portfoliosByType.set(type, []);
            }
            portfoliosByType.get(type)!.push(portfolio);
          }
        });

        return {
          totalValue,
          totalCost,
          totalProfitLoss,
          totalProfitLossPercentage,
          portfoliosByType,
        };
      })
    );
  }
}
