export enum AssetType {
  Stock = 0,
  USStock = 1,
  Gold = 2,
  Silver = 3,
  Fund = 4,
  FixedDeposit = 5,
  Crypto = 6,
}

export enum TransactionType {
  BUY = 'BUY',
  SELL = 'SELL',
  DEPOSIT_ADD = 'DEPOSIT_ADD',
  DEPOSIT_WITHDRAW = 'DEPOSIT_WITHDRAW',
  DEPOSIT_INCOME = 'DEPOSIT_INCOME',
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
  assetSymbol: string;
  assetName: string;
  totalQuantity: number;
  quantity: number; // alias for totalQuantity
  averagePrice: number;
  currentValue: number;
  totalCost: number;
  profitLoss: number;
  profitLossPercentage: number;
  currency: string;
  lastUpdated: Date;
  transactions: Transaction[];
}

export interface DashboardSummary {
  totalValue: number;
  totalCost: number;
  totalProfitLoss: number;
  totalProfitLossPercentage: number;
  portfoliosByType: Map<AssetType, Portfolio[]>;
  displayCurrency?: string;
}
