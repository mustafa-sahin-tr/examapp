export enum AssetType {
  BIST100 = 'BIST100',
  US_STOCK = 'US_STOCK',
  GOLD = 'GOLD',
  SILVER = 'SILVER',
  FUND = 'FUND',
  FUTURES = 'FUTURES'
}

export enum TransactionType {
  BUY = 'BUY',
  SELL = 'SELL'
}

export interface Asset {
  id: string;
  symbol: string;
  name: string;
  type: AssetType;
  currentPrice: number;
  currency: string;
  lastUpdated: Date;
  changePercentage: number;
  changeValue: number;
}

export interface Transaction {
  id: string;
  assetId: string;
  asset?: Asset;
  type: TransactionType;
  quantity: number;
  price: number;
  date: Date;
  fees?: number;
  notes?: string;
}

export interface Portfolio {
  assetId: string;
  asset?: Asset;
  totalQuantity: number;
  averagePrice: number;
  currentValue: number;
  totalCost: number;
  profitLoss: number;
  profitLossPercentage: number;
  transactions: Transaction[];
}

export interface DashboardSummary {
  totalValue: number;
  totalCost: number;
  totalProfitLoss: number;
  totalProfitLossPercentage: number;
  portfoliosByType: Map<AssetType, Portfolio[]>;
}
