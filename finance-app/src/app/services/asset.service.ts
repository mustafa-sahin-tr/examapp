import { Injectable } from '@angular/core';
import { Observable, of, BehaviorSubject, interval, map } from 'rxjs';
import { Asset, AssetType, Transaction, TransactionType, Portfolio, DashboardSummary } from '../models/asset.model';

@Injectable({
  providedIn: 'root'
})
export class AssetService {
  private assetsSubject = new BehaviorSubject<Asset[]>([]);
  public assets$ = this.assetsSubject.asObservable();

  constructor() {
    this.initializeDummyAssets();
    this.startPriceUpdates();
  }

  private initializeDummyAssets(): void {
    const dummyAssets: Asset[] = [
      // BIST100 Stocks
      {
        id: '1',
        symbol: 'TUPRS',
        name: 'Tüpraş',
        type: AssetType.BIST100,
        currentPrice: 85.50,
        currency: 'TRY',
        lastUpdated: new Date(),
        changePercentage: 2.5,
        changeValue: 2.08
      },
      {
        id: '2',
        symbol: 'AKBNK',
        name: 'Akbank',
        type: AssetType.BIST100,
        currentPrice: 42.30,
        currency: 'TRY',
        lastUpdated: new Date(),
        changePercentage: -1.2,
        changeValue: -0.51
      },
      {
        id: '3',
        symbol: 'THYAO',
        name: 'Türk Hava Yolları',
        type: AssetType.BIST100,
        currentPrice: 156.80,
        currency: 'TRY',
        lastUpdated: new Date(),
        changePercentage: 3.1,
        changeValue: 4.71
      },
      {
        id: '9',
        symbol: 'ASELS',
        name: 'Aselsan',
        type: AssetType.BIST100,
        currentPrice: 125.40,
        currency: 'TRY',
        lastUpdated: new Date(),
        changePercentage: 1.8,
        changeValue: 2.22
      },
      {
        id: '10',
        symbol: 'EREGL',
        name: 'Ereğli Demir Çelik',
        type: AssetType.BIST100,
        currentPrice: 31.85,
        currency: 'TRY',
        lastUpdated: new Date(),
        changePercentage: -0.8,
        changeValue: -0.26
      },
      {
        id: '11',
        symbol: 'KCHOL',
        name: 'Koç Holding',
        type: AssetType.BIST100,
        currentPrice: 164.20,
        currency: 'TRY',
        lastUpdated: new Date(),
        changePercentage: 2.2,
        changeValue: 3.54
      },
      {
        id: '12',
        symbol: 'SAHOL',
        name: 'Sabancı Holding',
        type: AssetType.BIST100,
        currentPrice: 49.80,
        currency: 'TRY',
        lastUpdated: new Date(),
        changePercentage: 1.5,
        changeValue: 0.74
      },
      {
        id: '13',
        symbol: 'VAKBN',
        name: 'VakıfBank',
        type: AssetType.BIST100,
        currentPrice: 17.25,
        currency: 'TRY',
        lastUpdated: new Date(),
        changePercentage: -2.1,
        changeValue: -0.37
      },
      {
        id: '14',
        symbol: 'ISCTR',
        name: 'İş Bankası',
        type: AssetType.BIST100,
        currentPrice: 11.20,
        currency: 'TRY',
        lastUpdated: new Date(),
        changePercentage: 0.9,
        changeValue: 0.10
      },
      {
        id: '15',
        symbol: 'GARAN',
        name: 'Garanti BBVA',
        type: AssetType.BIST100,
        currentPrice: 89.40,
        currency: 'TRY',
        lastUpdated: new Date(),
        changePercentage: 1.3,
        changeValue: 1.15
      },
      {
        id: '16',
        symbol: 'TCELL',
        name: 'Turkcell',
        type: AssetType.BIST100,
        currentPrice: 52.60,
        currency: 'TRY',
        lastUpdated: new Date(),
        changePercentage: 2.8,
        changeValue: 1.43
      },
      {
        id: '17',
        symbol: 'TTKOM',
        name: 'Türk Telekom',
        type: AssetType.BIST100,
        currentPrice: 15.80,
        currency: 'TRY',
        lastUpdated: new Date(),
        changePercentage: -1.5,
        changeValue: -0.24
      },
      {
        id: '18',
        symbol: 'BIMAS',
        name: 'BİM',
        type: AssetType.BIST100,
        currentPrice: 192.50,
        currency: 'TRY',
        lastUpdated: new Date(),
        changePercentage: 0.7,
        changeValue: 1.34
      },
      {
        id: '19',
        symbol: 'MGROS',
        name: 'Migros',
        type: AssetType.BIST100,
        currentPrice: 38.90,
        currency: 'TRY',
        lastUpdated: new Date(),
        changePercentage: -0.6,
        changeValue: -0.23
      },
      {
        id: '20',
        symbol: 'KOZAL',
        name: 'Koza Altın',
        type: AssetType.BIST100,
        currentPrice: 148.70,
        currency: 'TRY',
        lastUpdated: new Date(),
        changePercentage: 3.4,
        changeValue: 4.89
      },
      {
        id: '21',
        symbol: 'PETKM',
        name: 'Petkim',
        type: AssetType.BIST100,
        currentPrice: 10.55,
        currency: 'TRY',
        lastUpdated: new Date(),
        changePercentage: 1.1,
        changeValue: 0.11
      },
      {
        id: '22',
        symbol: 'TOASO',
        name: 'Tofaş',
        type: AssetType.BIST100,
        currentPrice: 78.20,
        currency: 'TRY',
        lastUpdated: new Date(),
        changePercentage: 2.6,
        changeValue: 1.98
      },
      {
        id: '23',
        symbol: 'FROTO',
        name: 'Ford Otosan',
        type: AssetType.BIST100,
        currentPrice: 145.60,
        currency: 'TRY',
        lastUpdated: new Date(),
        changePercentage: 1.7,
        changeValue: 2.43
      },
      {
        id: '9',
        symbol: 'ASELS',
        name: 'Aselsan',
        type: AssetType.BIST100,
        currentPrice: 124.60,
        currency: 'TRY',
        lastUpdated: new Date(),
        changePercentage: 1.8,
        changeValue: 2.21
      },
      {
        id: '10',
        symbol: 'EREGL',
        name: 'Ereğli Demir Çelik',
        type: AssetType.BIST100,
        currentPrice: 28.45,
        currency: 'TRY',
        lastUpdated: new Date(),
        changePercentage: -0.7,
        changeValue: -0.20
      },
      {
        id: '11',
        symbol: 'KCHOL',
        name: 'Koç Holding',
        type: AssetType.BIST100,
        currentPrice: 167.20,
        currency: 'TRY',
        lastUpdated: new Date(),
        changePercentage: 2.1,
        changeValue: 3.45
      },
      {
        id: '12',
        symbol: 'SAHOL',
        name: 'Sabancı Holding',
        type: AssetType.BIST100,
        currentPrice: 45.80,
        currency: 'TRY',
        lastUpdated: new Date(),
        changePercentage: -0.9,
        changeValue: -0.41
      },
      {
        id: '13',
        symbol: 'VAKBN',
        name: 'VakıfBank',
        type: AssetType.BIST100,
        currentPrice: 16.75,
        currency: 'TRY',
        lastUpdated: new Date(),
        changePercentage: 1.5,
        changeValue: 0.25
      },
      {
        id: '14',
        symbol: 'ISCTR',
        name: 'İş Bankası',
        type: AssetType.BIST100,
        currentPrice: 9.84,
        currency: 'TRY',
        lastUpdated: new Date(),
        changePercentage: -2.1,
        changeValue: -0.21
      },
      {
        id: '15',
        symbol: 'GARAN',
        name: 'Garanti BBVA',
        type: AssetType.BIST100,
        currentPrice: 89.75,
        currency: 'TRY',
        lastUpdated: new Date(),
        changePercentage: 0.8,
        changeValue: 0.71
      },
      {
        id: '16',
        symbol: 'TCELL',
        name: 'Turkcell',
        type: AssetType.BIST100,
        currentPrice: 52.40,
        currency: 'TRY',
        lastUpdated: new Date(),
        changePercentage: 3.2,
        changeValue: 1.63
      },
      {
        id: '17',
        symbol: 'TTKOM',
        name: 'Türk Telekom',
        type: AssetType.BIST100,
        currentPrice: 13.56,
        currency: 'TRY',
        lastUpdated: new Date(),
        changePercentage: -1.8,
        changeValue: -0.25
      },
      {
        id: '18',
        symbol: 'BIMAS',
        name: 'BİM',
        type: AssetType.BIST100,
        currentPrice: 198.50,
        currency: 'TRY',
        lastUpdated: new Date(),
        changePercentage: 1.4,
        changeValue: 2.74
      },
      {
        id: '19',
        symbol: 'MGROS',
        name: 'Migros',
        type: AssetType.BIST100,
        currentPrice: 34.65,
        currency: 'TRY',
        lastUpdated: new Date(),
        changePercentage: -0.6,
        changeValue: -0.21
      },
      {
        id: '20',
        symbol: 'KOZAL',
        name: 'Koza Altın',
        type: AssetType.BIST100,
        currentPrice: 156.80,
        currency: 'TRY',
        lastUpdated: new Date(),
        changePercentage: 4.2,
        changeValue: 6.33
      },
      {
        id: '21',
        symbol: 'PETKM',
        name: 'Petkim',
        type: AssetType.BIST100,
        currentPrice: 8.92,
        currency: 'TRY',
        lastUpdated: new Date(),
        changePercentage: -3.1,
        changeValue: -0.29
      },
      {
        id: '22',
        symbol: 'TOASO',
        name: 'Tofaş',
        type: AssetType.BIST100,
        currentPrice: 78.25,
        currency: 'TRY',
        lastUpdated: new Date(),
        changePercentage: 2.7,
        changeValue: 2.06
      },
      {
        id: '23',
        symbol: 'FROTO',
        name: 'Ford Otosan',
        type: AssetType.BIST100,
        currentPrice: 145.60,
        currency: 'TRY',
        lastUpdated: new Date(),
        changePercentage: 1.9,
        changeValue: 2.72
      },
      // US Stocks
      {
        id: '4',
        symbol: 'AAPL',
        name: 'Apple Inc.',
        type: AssetType.US_STOCK,
        currentPrice: 182.52,
        currency: 'USD',
        lastUpdated: new Date(),
        changePercentage: 1.8,
        changeValue: 3.22
      },
      {
        id: '5',
        symbol: 'GOOGL',
        name: 'Alphabet Inc.',
        type: AssetType.US_STOCK,
        currentPrice: 138.45,
        currency: 'USD',
        lastUpdated: new Date(),
        changePercentage: -0.5,
        changeValue: -0.69
      },
      // Precious Metals
      {
        id: '6',
        symbol: 'GOLD',
        name: 'Gold',
        type: AssetType.GOLD,
        currentPrice: 2045.30,
        currency: 'USD',
        lastUpdated: new Date(),
        changePercentage: 0.8,
        changeValue: 16.20
      },
      {
        id: '7',
        symbol: 'SILVER',
        name: 'Silver',
        type: AssetType.SILVER,
        currentPrice: 24.85,
        currency: 'USD',
        lastUpdated: new Date(),
        changePercentage: -1.1,
        changeValue: -0.28
      },
      // Funds
      {
        id: '8',
        symbol: 'IVV',
        name: 'iShares Core S&P 500 ETF',
        type: AssetType.FUND,
        currentPrice: 456.78,
        currency: 'USD',
        lastUpdated: new Date(),
        changePercentage: 0.6,
        changeValue: 2.74
      }
    ];

    this.assetsSubject.next(dummyAssets);
  }

  private startPriceUpdates(): void {
    // Simulate real-time price updates every 30 seconds
    interval(30000).subscribe(() => {
      const currentAssets = this.assetsSubject.value;
      const updatedAssets = currentAssets.map(asset => ({
        ...asset,
        currentPrice: this.simulatePriceChange(asset.currentPrice),
        lastUpdated: new Date(),
        changePercentage: Math.random() * 6 - 3, // Random between -3% and +3%
        changeValue: 0 // Will be calculated
      }));

      updatedAssets.forEach(asset => {
        asset.changeValue = asset.currentPrice * (asset.changePercentage / 100);
      });

      this.assetsSubject.next(updatedAssets);
    });
  }

  private simulatePriceChange(currentPrice: number): number {
    const change = (Math.random() - 0.5) * 0.1; // ±5% max change
    return Math.round((currentPrice * (1 + change)) * 100) / 100;
  }

  getAssets(): Observable<Asset[]> {
    return this.assets$;
  }

  getAssetsByType(type: AssetType): Observable<Asset[]> {
    return this.assets$.pipe(
      map(assets => assets.filter(asset => asset.type === type))
    );
  }

  getAssetById(id: string): Observable<Asset | undefined> {
    return this.assets$.pipe(
      map(assets => assets.find(asset => asset.id === id))
    );
  }

  searchAssets(query: string): Observable<Asset[]> {
    return this.assets$.pipe(
      map(assets => assets.filter(asset => 
        asset.symbol.toLowerCase().includes(query.toLowerCase()) ||
        asset.name.toLowerCase().includes(query.toLowerCase())
      ))
    );
  }
}
