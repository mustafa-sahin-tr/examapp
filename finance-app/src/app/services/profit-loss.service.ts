import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';

export interface ProfitLossHistoryDto {
  id: string;
  timestamp: Date;
  totalProfitLoss: number;
  totalInvestment: number;
  totalCurrentValue: number;
  profitLossPercentage: number;
  assetTypeBreakdown: AssetTypeBreakdownDto[];
}

export interface AssetTypeBreakdownDto {
  assetType: string;
  profitLoss: number;
  investment: number;
  currentValue: number;
  profitLossPercentage: number;
  assetCount: number;
}

export interface AssetTypeProfitLossDto {
  id: string;
  assetType: string;
  profitLoss: number;
  investment: number;
  currentValue: number;
  profitLossPercentage: number;
  assetCount: number;
  timestamp: Date;
}

export interface AssetProfitLossDto {
  id: string;
  assetId: string;
  assetSymbol: string;
  assetName: string;
  assetType: string;
  profitLoss: number;
  investment: number;
  currentValue: number;
  profitLossPercentage: number;
  quantity: number;
  averagePrice: number;
  currentPrice: number;
  timestamp: Date;
}

@Injectable({
  providedIn: 'root',
})
export class ProfitLossService {
  constructor(private apiService: ApiService) {}

  /**
   * Kar/zarar geçmişini döndürür
   */
  getProfitLossHistory(
    userId: string = 'default-user',
    startDate?: Date,
    endDate?: Date,
    timeFrame: 'hourly' | 'daily' | 'weekly' | 'monthly' = 'hourly'
  ): Observable<ProfitLossHistoryDto[]> {
    const params = new URLSearchParams();
    params.append('userId', userId);
    params.append('timeFrame', timeFrame);

    if (startDate) {
      params.append('startDate', startDate.toISOString());
    }
    if (endDate) {
      params.append('endDate', endDate.toISOString());
    }

    return this.apiService.get<ProfitLossHistoryDto[]>(`/profitloss/history?${params.toString()}`);
  }

  /**
   * Asset tipi bazında kar/zarar geçmişini döndürür
   */
  getAssetTypeProfitLossHistory(
    assetType: string,
    userId: string = 'default-user',
    startDate?: Date,
    endDate?: Date,
    timeFrame: 'hourly' | 'daily' | 'weekly' | 'monthly' = 'hourly'
  ): Observable<AssetTypeProfitLossDto[]> {
    const params = new URLSearchParams();
    params.append('userId', userId);
    params.append('timeFrame', timeFrame);

    if (startDate) {
      params.append('startDate', startDate.toISOString());
    }
    if (endDate) {
      params.append('endDate', endDate.toISOString());
    }

    return this.apiService.get<AssetTypeProfitLossDto[]>(
      `/profitloss/history/asset-type/${assetType}?${params.toString()}`
    );
  }

  /**
   * Belirli bir asset için kar/zarar geçmişini döndürür
   */
  getAssetProfitLossHistory(
    assetId: string,
    userId: string = 'default-user',
    startDate?: Date,
    endDate?: Date,
    timeFrame: 'hourly' | 'daily' | 'weekly' | 'monthly' = 'hourly'
  ): Observable<AssetProfitLossDto[]> {
    const params = new URLSearchParams();
    params.append('userId', userId);
    params.append('timeFrame', timeFrame);

    if (startDate) {
      params.append('startDate', startDate.toISOString());
    }
    if (endDate) {
      params.append('endDate', endDate.toISOString());
    }

    return this.apiService.get<AssetProfitLossDto[]>(`/profitloss/history/asset/${assetId}?${params.toString()}`);
  }

  /**
   * Güncel kar/zarar durumunu döndürür
   */
  getCurrentProfitLoss(userId: string = 'default-user'): Observable<ProfitLossHistoryDto> {
    return this.apiService.get<ProfitLossHistoryDto>(`/profitloss/current?userId=${userId}`);
  }
}
