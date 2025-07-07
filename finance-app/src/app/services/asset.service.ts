import { Injectable } from '@angular/core';
import { Observable, BehaviorSubject, catchError, of, map } from 'rxjs';
import { Asset, AssetType } from '../models/asset.model';
import { ApiService } from './api.service';
import { SignalRService, PriceUpdate } from './signalr.service';

@Injectable({
  providedIn: 'root',
})
export class AssetService {
  private assetsSubject = new BehaviorSubject<Asset[]>([]);
  public assets$ = this.assetsSubject.asObservable();

  constructor(private apiService: ApiService, private signalRService: SignalRService) {
    this.loadAssets();
    this.setupPriceUpdates();
  }

  private loadAssets(): void {
    this.getAssetsFromApi().subscribe({
      next: (assets) => {
        this.assetsSubject.next(assets);
      },
      error: (error) => {
        console.error('Error loading assets:', error);
        this.assetsSubject.next([]);
      },
    });
  }

  private setupPriceUpdates(): void {
    this.signalRService.priceUpdates$.subscribe((priceUpdates: PriceUpdate[]) => {
      if (priceUpdates.length > 0) {
        this.updateAssetPrices(priceUpdates);
      }
    });
  }

  private updateAssetPrices(priceUpdates: PriceUpdate[]): void {
    const currentAssets = this.assetsSubject.value;
    const updatedAssets = currentAssets.map((asset) => {
      const priceUpdate = priceUpdates.find(
        (update) => update.assetId === asset.id || (update.type === asset.type && update.symbol === asset.symbol)
      );

      if (priceUpdate) {
        return {
          ...asset,
          currentPrice: priceUpdate.currentPrice,
          changeValue: priceUpdate.change,
          changePercentage: priceUpdate.changePercent,
          lastUpdated: new Date(priceUpdate.lastUpdated),
        };
      }

      return asset;
    });

    this.assetsSubject.next(updatedAssets);
  }

  private getAssetsFromApi(): Observable<Asset[]> {
    return this.apiService.get<Asset[]>('/assets').pipe(
      map((assets: any[]) => {
        // Convert date strings to Date objects
        return assets.map((a) => ({
          ...a,
          lastUpdated: new Date(a.lastUpdated),
        }));
      }),
      catchError((error) => {
        console.error('API call failed:', error);
        return of([]);
      })
    );
  }

  getAssets(): Observable<Asset[]> {
    console.log('🔍 AssetService.getAssets() called');
    console.log('📈 Current assets count:', this.assetsSubject.value.length);
    return this.assets$;
  }

  getAssetsByType(type: AssetType): Observable<Asset[]> {
    return this.apiService.get<Asset[]>(`/assets/type/${type}`).pipe(
      catchError((error) => {
        console.error(`API call failed for type ${type}:`, error);
        return this.assets$.pipe(map((assets) => assets.filter((asset) => asset.type === type)));
      })
    );
  }

  getBist100Assets(): Observable<Asset[]> {
    return this.getAssetsByType(AssetType.Stock);
  }

  getUsStocks(): Observable<Asset[]> {
    return this.getAssetsByType(AssetType.USStock);
  }

  getPreciousMetals(): Observable<Asset[]> {
    return this.assets$.pipe(
      map((assets) => assets.filter((asset) => asset.type === AssetType.Gold || asset.type === AssetType.Silver))
    );
  }

  getFunds(): Observable<Asset[]> {
    return this.getAssetsByType(AssetType.Fund);
  }

  getAllAssets(): Observable<Asset[]> {
    return this.assets$;
  }

  searchAssets(query: string): Observable<Asset[]> {
    return this.assets$.pipe(
      map((assets) =>
        assets.filter(
          (asset) =>
            asset.symbol.toLowerCase().includes(query.toLowerCase()) ||
            asset.name.toLowerCase().includes(query.toLowerCase())
        )
      )
    );
  }

  simulatePriceUpdates(): Observable<any> {
    return this.apiService.post('/assets/simulate-price-updates', {});
  }

  refreshAssets(): void {
    this.loadAssets();
  }

  // Yeni asset ekleme metodu
  addAsset(asset: Omit<Asset, 'id' | 'lastUpdated' | 'changePercentage' | 'changeValue'>): Observable<Asset> {
    return this.apiService.post<Asset>('/assets', asset).pipe(
      map((newAsset) => {
        // Local state'i güncelle
        const currentAssets = this.assetsSubject.value;
        this.assetsSubject.next([...currentAssets, newAsset]);
        return newAsset;
      }),
      catchError((error) => {
        console.error('Error adding asset:', error);
        throw error;
      })
    );
  }

  // Asset güncelleme metodu
  updateAsset(id: string, asset: Partial<Asset>): Observable<Asset> {
    return this.apiService.put<Asset>(`/assets/${id}`, asset).pipe(
      map((updatedAsset) => {
        // Local state'i güncelle
        const currentAssets = this.assetsSubject.value;
        const index = currentAssets.findIndex((a) => a.id === id);
        if (index !== -1) {
          currentAssets[index] = updatedAsset;
          this.assetsSubject.next([...currentAssets]);
        }
        return updatedAsset;
      }),
      catchError((error) => {
        console.error('Error updating asset:', error);
        throw error;
      })
    );
  }

  // Asset silme metodu
  deleteAsset(id: string): Observable<void> {
    return this.apiService.delete<void>(`/assets/${id}`).pipe(
      map(() => {
        // Local state'i güncelle
        const currentAssets = this.assetsSubject.value.filter((a) => a.id !== id);
        this.assetsSubject.next(currentAssets);
      }),
      catchError((error) => {
        console.error('Error deleting asset:', error);
        throw error;
      })
    );
  }
}
