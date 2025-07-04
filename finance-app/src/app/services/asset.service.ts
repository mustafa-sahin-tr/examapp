import { Injectable } from '@angular/core';
import { Observable, BehaviorSubject, interval, switchMap, catchError, of, map } from 'rxjs';
import { Asset, AssetType } from '../models/asset.model';
import { ApiService } from './api.service';

@Injectable({
  providedIn: 'root',
})
export class AssetService {
  private assetsSubject = new BehaviorSubject<Asset[]>([]);
  public assets$ = this.assetsSubject.asObservable();

  constructor(private apiService: ApiService) {
    this.loadAssets();
    this.startPriceUpdates();
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

  private getAssetsFromApi(): Observable<Asset[]> {
    return this.apiService.get<Asset[]>('/assets').pipe(
      catchError((error) => {
        console.error('API call failed:', error);
        return of([]);
      })
    );
  }

  getAssets(): Observable<Asset[]> {
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
    return this.getAssetsByType(AssetType.BIST100);
  }

  getUsStocks(): Observable<Asset[]> {
    return this.getAssetsByType(AssetType.US_STOCK);
  }

  getPreciousMetals(): Observable<Asset[]> {
    return this.assets$.pipe(
      map((assets) => assets.filter((asset) => asset.type === AssetType.GOLD || asset.type === AssetType.SILVER))
    );
  }

  getFunds(): Observable<Asset[]> {
    return this.getAssetsByType(AssetType.FUND);
  }

  getFutures(): Observable<Asset[]> {
    return this.getAssetsByType(AssetType.FUTURES);
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

  private startPriceUpdates(): void {
    // Her 30 saniyede bir fiyat güncellemesi simüle et
    interval(30000)
      .pipe(
        switchMap(() => this.simulatePriceUpdates()),
        switchMap(() => this.getAssetsFromApi()),
        catchError((error) => {
          console.error('Error in price updates:', error);
          return of([]);
        })
      )
      .subscribe((assets) => {
        this.assetsSubject.next(assets);
      });
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
