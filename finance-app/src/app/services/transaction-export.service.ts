import { Injectable } from '@angular/core';
import { Observable, of, forkJoin } from 'rxjs';
import { Transaction, TransactionType, AssetType, Asset } from '../models/asset.model';
import { ApiService } from './api.service';
import { AssetService } from './asset.service';
import { TransactionService } from './transaction.service';
import { AllowedCryptoService } from './allowed-crypto.service';
import * as ExcelJS from 'exceljs';
import { saveAs } from 'file-saver';

// User-friendly lookup values for Excel export/import
export const ASSET_TYPE_LABELS = {
  [AssetType.Stock]: 'BIST 100',
  [AssetType.USStock]: 'US Stocks',
  [AssetType.PreciousMetals]: 'Precious Metals',
  [AssetType.Fund]: 'Funds',
  [AssetType.FixedDeposit]: 'Vadeli Mevduat',
  [AssetType.Crypto]: 'Crypto',
};

export const TRANSACTION_TYPE_LABELS = {
  [TransactionType.BUY]: 'Buy',
  [TransactionType.SELL]: 'Sell',
  [TransactionType.DEPOSIT_ADD]: 'Para Ekle',
  [TransactionType.DEPOSIT_WITHDRAW]: 'Para Çıkar',
  [TransactionType.DEPOSIT_INCOME]: 'Faiz Geliri',
};

// Reverse lookup for import
export const ASSET_TYPE_VALUES = Object.entries(ASSET_TYPE_LABELS).reduce(
  (acc, [key, value]) => {
    acc[value] = parseInt(key);
    return acc;
  },
  {} as Record<string, AssetType>
);

// Backward-compatible aliases (old exports/imports)
ASSET_TYPE_VALUES['Gold'] = AssetType.PreciousMetals;
ASSET_TYPE_VALUES['Silver'] = AssetType.PreciousMetals;

export const TRANSACTION_TYPE_VALUES = Object.entries(TRANSACTION_TYPE_LABELS).reduce(
  (acc, [key, value]) => {
    acc[value] = key as TransactionType;
    return acc;
  },
  {} as Record<string, TransactionType>
);

export interface ExcelTransactionRow {
  'Asset Symbol': string;
  'Asset Name': string;
  'Asset Type': string;
  'Transaction Type': string;
  Quantity: number;
  Price: number;
  Date: string;
  Fees: number;
  Notes: string;
  Currency: string;
}

@Injectable({
  providedIn: 'root',
})
export class TransactionExportService {
  constructor(
    private apiService: ApiService,
    private assetService: AssetService,
    private transactionService: TransactionService,
    private allowedCryptoService: AllowedCryptoService
  ) {}

  /**
   * Export transactions to Excel with user-friendly labels
   */
  exportTransactionsToExcel(): Observable<void> {
    console.log('📊 Starting export process...');

    return new Observable((observer) => {
      console.log('🔄 Making fresh API calls for transactions and assets...');

      // Direkt API çağrıları yap, cached data değil
      forkJoin({
        transactions: this.apiService.get<Transaction[]>('/transactions'),
        assets: this.apiService.get<Asset[]>('/assets'),
      }).subscribe({
        next: async ({ transactions, assets }) => {
          try {
            console.log('📋 Fresh data received from API:');
            console.log('- Transactions:', transactions.length);
            console.log('- Assets:', assets.length);
            console.log('- Sample transaction:', transactions[0]);
            console.log('- Sample asset:', assets[0]);

            const excelData = this.prepareExcelData(transactions, assets);
            console.log('📝 Prepared Excel data:', excelData.length, 'rows');

            await this.createExcelFile(excelData);
            console.log('📄 Excel file created and download triggered');

            observer.next();
            observer.complete();
          } catch (error) {
            console.error('❌ Error in export process:', error);
            observer.error(error);
          }
        },
        error: (error) => {
          console.error('❌ API calls failed:', error);
          observer.error(error);
        },
      });
    });
  }

  /**
   * Import transactions from Excel file
   */
  importTransactionsFromExcel(file: File): Observable<{ success: number; errors: string[] }> {
    return new Observable((observer) => {
      const reader = new FileReader();
      reader.onload = async (e) => {
        try {
          if (!e.target?.result) {
            observer.error('File could not be read');
            return;
          }

          const buffer = e.target.result as ArrayBuffer;
          const workbook = new ExcelJS.Workbook();
          await workbook.xlsx.load(buffer);

          const worksheet = workbook.getWorksheet(1); // First worksheet
          if (!worksheet) {
            observer.error('No worksheet found');
            return;
          }

          const jsonData: ExcelTransactionRow[] = [];

          // Get headers from first row
          const headerRow = worksheet.getRow(1);
          const headers: string[] = [];
          headerRow.eachCell((cell, colNumber) => {
            headers[colNumber] = cell.value?.toString() || '';
          });

          // Process data rows
          worksheet.eachRow((row, rowNumber) => {
            if (rowNumber === 1) return; // Skip header row

            const rowData: any = {};
            row.eachCell((cell, colNumber) => {
              const header = headers[colNumber];
              if (header) {
                rowData[header] = cell.value;
              }
            });

            // Only add rows that have required data
            if (rowData['Asset Symbol'] && rowData['Asset Type'] && rowData['Transaction Type']) {
              jsonData.push(rowData as ExcelTransactionRow);
            }
          });

          this.processImportData(jsonData).subscribe({
            next: (result) => {
              observer.next(result);
              observer.complete();
            },
            error: (error) => observer.error(error),
          });
        } catch (error) {
          observer.error(error);
        }
      };

      reader.onerror = () => observer.error('Error reading file');
      reader.readAsArrayBuffer(file);
    });
  }

  private prepareExcelData(transactions: Transaction[], assets: Asset[]): ExcelTransactionRow[] {
    const assetMap = new Map<string, Asset>();
    assets.forEach((asset) => assetMap.set(asset.id, asset));

    return transactions.map((transaction) => {
      const asset = assetMap.get(transaction.assetId);

      // Ensure date is a Date object
      const transactionDate = transaction.date instanceof Date ? transaction.date : new Date(transaction.date);

      return {
        'Asset Symbol': asset?.symbol || 'Unknown',
        'Asset Name': asset?.name || 'Unknown',
        'Asset Type': asset ? ASSET_TYPE_LABELS[asset.type] : 'Unknown',
        'Transaction Type': TRANSACTION_TYPE_LABELS[transaction.type],
        Quantity: transaction.quantity,
        Price: transaction.price,
        Date: transactionDate.toISOString().split('T')[0],
        Fees: transaction.fees || 0,
        Notes: transaction.notes || '',
        Currency: asset?.currency || 'TRY',
      };
    });
  }

  private async createExcelFile(data: ExcelTransactionRow[]): Promise<void> {
    const workbook = new ExcelJS.Workbook();
    const worksheet = workbook.addWorksheet('Transactions');

    // Add headers
    const headers = [
      'Asset Symbol',
      'Asset Name',
      'Asset Type',
      'Transaction Type',
      'Quantity',
      'Price',
      'Date',
      'Fees',
      'Notes',
      'Currency',
    ];

    worksheet.addRow(headers);

    // Style headers
    const headerRow = worksheet.getRow(1);
    headerRow.font = { bold: true };
    headerRow.fill = {
      type: 'pattern',
      pattern: 'solid',
      fgColor: { argb: 'FFE6E6FA' },
    };

    // Add data rows
    data.forEach((row) => {
      worksheet.addRow([
        row['Asset Symbol'],
        row['Asset Name'],
        row['Asset Type'],
        row['Transaction Type'],
        row['Quantity'],
        row['Price'],
        row['Date'],
        row['Fees'],
        row['Notes'],
        row['Currency'],
      ]);
    });

    // Set column widths
    worksheet.columns = [
      { width: 15 }, // Asset Symbol
      { width: 30 }, // Asset Name
      { width: 15 }, // Asset Type
      { width: 15 }, // Transaction Type
      { width: 12 }, // Quantity
      { width: 12 }, // Price
      { width: 12 }, // Date
      { width: 10 }, // Fees
      { width: 30 }, // Notes
      { width: 10 }, // Currency
    ];

    // Generate Excel file
    const buffer = await workbook.xlsx.writeBuffer();
    const blob = new Blob([buffer], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });

    const fileName = `transactions_${new Date().toISOString().split('T')[0]}.xlsx`;
    saveAs(blob, fileName);
  }

  private processImportData(data: ExcelTransactionRow[]): Observable<{ success: number; errors: string[] }> {
    return new Observable((observer) => {
      const errors: string[] = [];
      let successCount = 0;
      let processedCount = 0;

      if (data.length === 0) {
        observer.next({ success: 0, errors: ['No data found in Excel file'] });
        observer.complete();
        return;
      }

      // Process each row
      data.forEach((row, index) => {
        this.processTransactionRow(row, index + 1).subscribe({
          next: () => {
            successCount++;
            processedCount++;
            this.checkCompletion();
          },
          error: (error) => {
            errors.push(`Row ${index + 1}: ${error}`);
            processedCount++;
            this.checkCompletion();
          },
        });
      });

      const checkCompletion = () => {
        if (processedCount === data.length) {
          observer.next({ success: successCount, errors });
          observer.complete();
        }
      };

      this.checkCompletion = checkCompletion;
    });
  }

  private checkCompletion: () => void = () => {};

  private processTransactionRow(row: ExcelTransactionRow, rowNumber: number): Observable<void> {
    return new Observable((observer) => {
      try {
        // Validate required fields
        if (!row['Asset Symbol'] || !row['Asset Type'] || !row['Transaction Type']) {
          observer.error('Missing required fields (Asset Symbol, Asset Type, Transaction Type)');
          return;
        }

        // Convert user-friendly labels back to enum values
        const assetType = ASSET_TYPE_VALUES[row['Asset Type']];
        const transactionType = TRANSACTION_TYPE_VALUES[row['Transaction Type']];

        if (assetType === undefined) {
          observer.error(`Invalid Asset Type: ${row['Asset Type']}`);
          return;
        }

        if (!transactionType) {
          observer.error(`Invalid Transaction Type: ${row['Transaction Type']}`);
          return;
        }

        // Find or create asset
        this.findOrCreateAsset(row, assetType).subscribe({
          next: (asset) => {
            // Create transaction
            const transaction = {
              assetId: asset.id,
              type: transactionType,
              quantity: row['Quantity'],
              price: row['Price'],
              date: new Date(row['Date']),
              fees: row['Fees'] || 0,
              notes: row['Notes'] || '',
            };

            this.transactionService.addTransaction(transaction).subscribe({
              next: () => {
                observer.next();
                observer.complete();
              },
              error: (error) => observer.error(`Failed to create transaction: ${error}`),
            });
          },
          error: (error) => observer.error(`Failed to find/create asset: ${error}`),
        });
      } catch (error) {
        observer.error(`Invalid row data: ${error}`);
      }
    });
  }

  private findOrCreateAsset(row: ExcelTransactionRow, assetType: AssetType): Observable<Asset> {
    return new Observable((observer) => {
      // First try to find existing asset
      this.assetService.getAssets().subscribe({
        next: (assets) => {
          const symbol = (row['Asset Symbol'] || '').toString().trim().toUpperCase();
          const existingAsset = assets.find((a) => a.symbol === symbol && a.type === assetType);

          if (existingAsset) {
            observer.next(existingAsset);
            observer.complete();
            return;
          }

          const createAsset = () => {
            // Create new asset if not found
            const newAsset = {
              symbol,
              name: row['Asset Name'] || symbol,
              type: assetType,
              currentPrice: row['Price'],
              currency: assetType === AssetType.Crypto ? 'USD' : row['Currency'] || 'TRY',
            };

            this.assetService.addAsset(newAsset).subscribe({
              next: (createdAsset) => {
                observer.next(createdAsset);
                observer.complete();
              },
              error: (error) => observer.error(error),
            });
          };

          if (assetType === AssetType.Crypto) {
            this.allowedCryptoService.getEnabled().subscribe({
              next: (allowed) => {
                const isAllowed = allowed.some((c) => c.symbol === symbol);
                if (!isAllowed) {
                  observer.error(`Crypto is not allowed: ${symbol}`);
                  return;
                }

                createAsset();
              },
              error: (error) => observer.error(error),
            });
            return;
          }

          createAsset();
        },
        error: (error) => observer.error(error),
      });
    });
  }

  /**
   * Download template Excel file for import
   */
  async downloadTemplate(): Promise<void> {
    const templateData: ExcelTransactionRow[] = [
      // BIST 100 Examples
      {
        'Asset Symbol': 'AKBNK',
        'Asset Name': 'Akbank T.A.S.',
        'Asset Type': 'BIST 100',
        'Transaction Type': 'Buy',
        Quantity: 100,
        Price: 25.5,
        Date: '2024-01-15',
        Fees: 10.2,
        Notes: 'BIST 100 hisse alımı',
        Currency: 'TRY',
      },
      {
        'Asset Symbol': 'AKBNK',
        'Asset Name': 'Akbank T.A.S.',
        'Asset Type': 'BIST 100',
        'Transaction Type': 'Sell',
        Quantity: 50,
        Price: 26.0,
        Date: '2024-02-15',
        Fees: 5.5,
        Notes: 'BIST 100 hisse satımı',
        Currency: 'TRY',
      },
      {
        'Asset Symbol': 'THYAO',
        'Asset Name': 'Türk Hava Yolları A.O.',
        'Asset Type': 'BIST 100',
        'Transaction Type': 'Buy',
        Quantity: 200,
        Price: 150.0,
        Date: '2024-01-20',
        Fees: 15.0,
        Notes: 'THY hisse alımı',
        Currency: 'TRY',
      },

      // US Stocks Examples
      {
        'Asset Symbol': 'AAPL',
        'Asset Name': 'Apple Inc.',
        'Asset Type': 'US Stocks',
        'Transaction Type': 'Buy',
        Quantity: 10,
        Price: 150.0,
        Date: '2024-01-16',
        Fees: 1.0,
        Notes: 'Apple hisse alımı',
        Currency: 'USD',
      },

      // Crypto Examples
      {
        'Asset Symbol': 'BTC',
        'Asset Name': 'Bitcoin',
        'Asset Type': 'Crypto',
        'Transaction Type': 'Buy',
        Quantity: 0.01,
        Price: 40000,
        Date: '2024-01-10',
        Fees: 0,
        Notes: 'BTC alımı',
        Currency: 'USD',
      },
      {
        'Asset Symbol': 'ETH',
        'Asset Name': 'Ethereum',
        'Asset Type': 'Crypto',
        'Transaction Type': 'Buy',
        Quantity: 0.25,
        Price: 2000,
        Date: '2024-01-12',
        Fees: 0,
        Notes: 'ETH alımı',
        Currency: 'USD',
      },
      {
        'Asset Symbol': 'AAPL',
        'Asset Name': 'Apple Inc.',
        'Asset Type': 'US Stocks',
        'Transaction Type': 'Sell',
        Quantity: 5,
        Price: 155.0,
        Date: '2024-02-16',
        Fees: 0.5,
        Notes: 'Apple hisse satımı',
        Currency: 'USD',
      },
      {
        'Asset Symbol': 'MSFT',
        'Asset Name': 'Microsoft Corporation',
        'Asset Type': 'US Stocks',
        'Transaction Type': 'Buy',
        Quantity: 8,
        Price: 300.0,
        Date: '2024-01-18',
        Fees: 1.2,
        Notes: 'Microsoft hisse alımı',
        Currency: 'USD',
      },
      {
        'Asset Symbol': 'TSLA',
        'Asset Name': 'Tesla Inc.',
        'Asset Type': 'US Stocks',
        'Transaction Type': 'Buy',
        Quantity: 15,
        Price: 200.0,
        Date: '2024-01-25',
        Fees: 2.0,
        Notes: 'Tesla hisse alımı',
        Currency: 'USD',
      },

      // Gold Examples
      {
        'Asset Symbol': 'XAU/USD',
        'Asset Name': 'Gold',
        'Asset Type': 'Gold',
        'Transaction Type': 'Buy',
        Quantity: 5,
        Price: 2000.0,
        Date: '2024-01-10',
        Fees: 25.0,
        Notes: 'Altın alımı',
        Currency: 'USD',
      },
      {
        'Asset Symbol': 'XAU/USD',
        'Asset Name': 'Gold',
        'Asset Type': 'Gold',
        'Transaction Type': 'Sell',
        Quantity: 2,
        Price: 2050.0,
        Date: '2024-02-10',
        Fees: 15.0,
        Notes: 'Altın satımı',
        Currency: 'USD',
      },
      {
        'Asset Symbol': 'GAU',
        'Asset Name': 'Gram Altın',
        'Asset Type': 'Gold',
        'Transaction Type': 'Buy',
        Quantity: 50,
        Price: 2100.0,
        Date: '2024-01-12',
        Fees: 50.0,
        Notes: 'Gram altın alımı',
        Currency: 'TRY',
      },

      // Silver Examples
      {
        'Asset Symbol': 'XAG/USD',
        'Asset Name': 'Silver',
        'Asset Type': 'Silver',
        'Transaction Type': 'Buy',
        Quantity: 100,
        Price: 25.0,
        Date: '2024-01-08',
        Fees: 12.0,
        Notes: 'Gümüş alımı',
        Currency: 'USD',
      },
      {
        'Asset Symbol': 'XAG/USD',
        'Asset Name': 'Silver',
        'Asset Type': 'Silver',
        'Transaction Type': 'Sell',
        Quantity: 40,
        Price: 26.0,
        Date: '2024-02-08',
        Fees: 8.0,
        Notes: 'Gümüş satımı',
        Currency: 'USD',
      },

      // Funds Examples
      {
        'Asset Symbol': 'AKFON',
        'Asset Name': 'Ak Portföy Yeni Ekonomi Fonu',
        'Asset Type': 'Funds',
        'Transaction Type': 'Buy',
        Quantity: 1000,
        Price: 1.25,
        Date: '2024-01-05',
        Fees: 5.0,
        Notes: 'Yatırım fonu alımı',
        Currency: 'TRY',
      },
      {
        'Asset Symbol': 'AKFON',
        'Asset Name': 'Ak Portföy Yeni Ekonomi Fonu',
        'Asset Type': 'Funds',
        'Transaction Type': 'Sell',
        Quantity: 500,
        Price: 1.3,
        Date: '2024-02-05',
        Fees: 3.0,
        Notes: 'Yatırım fonu satımı',
        Currency: 'TRY',
      },
      {
        'Asset Symbol': 'TFFON',
        'Asset Name': 'TF Portföy Teknoloji Fonu',
        'Asset Type': 'Funds',
        'Transaction Type': 'Buy',
        Quantity: 750,
        Price: 2.15,
        Date: '2024-01-22',
        Fees: 8.0,
        Notes: 'Teknoloji fonu alımı',
        Currency: 'TRY',
      },

      // Fixed Deposit Examples
      {
        'Asset Symbol': 'DEPOSIT-001',
        'Asset Name': 'Vadeli Mevduat 6 Ay',
        'Asset Type': 'Vadeli Mevduat',
        'Transaction Type': 'Para Ekle',
        Quantity: 1,
        Price: 50000.0,
        Date: '2024-01-01',
        Fees: 0.0,
        Notes: '6 aylık vadeli mevduat açılışı',
        Currency: 'TRY',
      },
      {
        'Asset Symbol': 'DEPOSIT-001',
        'Asset Name': 'Vadeli Mevduat 6 Ay',
        'Asset Type': 'Vadeli Mevduat',
        'Transaction Type': 'Faiz Geliri',
        Quantity: 1,
        Price: 2500.0,
        Date: '2024-04-01',
        Fees: 0.0,
        Notes: '3 aylık faiz geliri',
        Currency: 'TRY',
      },
      {
        'Asset Symbol': 'DEPOSIT-001',
        'Asset Name': 'Vadeli Mevduat 6 Ay',
        'Asset Type': 'Vadeli Mevduat',
        'Transaction Type': 'Para Çıkar',
        Quantity: 1,
        Price: 52500.0,
        Date: '2024-07-01',
        Fees: 0.0,
        Notes: 'Vadeli mevduat kapatılması',
        Currency: 'TRY',
      },
      {
        'Asset Symbol': 'DEPOSIT-USD',
        'Asset Name': 'USD Vadeli Mevduat',
        'Asset Type': 'Vadeli Mevduat',
        'Transaction Type': 'Para Ekle',
        Quantity: 1,
        Price: 10000.0,
        Date: '2024-01-15',
        Fees: 0.0,
        Notes: 'USD vadeli mevduat açılışı',
        Currency: 'USD',
      },
      {
        'Asset Symbol': 'DEPOSIT-USD',
        'Asset Name': 'USD Vadeli Mevduat',
        'Asset Type': 'Vadeli Mevduat',
        'Transaction Type': 'Faiz Geliri',
        Quantity: 1,
        Price: 250.0,
        Date: '2024-04-15',
        Fees: 0.0,
        Notes: 'USD mevduat faiz geliri',
        Currency: 'USD',
      },

      // Additional Cash Operations Examples
      {
        'Asset Symbol': 'CASH-TRY',
        'Asset Name': 'Nakit TRY',
        'Asset Type': 'Vadeli Mevduat',
        'Transaction Type': 'Para Ekle',
        Quantity: 1,
        Price: 15000.0,
        Date: '2024-01-03',
        Fees: 0.0,
        Notes: 'Hesaba para yatırma',
        Currency: 'TRY',
      },
      {
        'Asset Symbol': 'CASH-TRY',
        'Asset Name': 'Nakit TRY',
        'Asset Type': 'Vadeli Mevduat',
        'Transaction Type': 'Para Çıkar',
        Quantity: 1,
        Price: 5000.0,
        Date: '2024-01-28',
        Fees: 0.0,
        Notes: 'Hesaptan para çekme',
        Currency: 'TRY',
      },
      {
        'Asset Symbol': 'CASH-USD',
        'Asset Name': 'Nakit USD',
        'Asset Type': 'Vadeli Mevduat',
        'Transaction Type': 'Para Ekle',
        Quantity: 1,
        Price: 3000.0,
        Date: '2024-02-01',
        Fees: 0.0,
        Notes: 'USD hesabına para yatırma',
        Currency: 'USD',
      },
    ];

    const workbook = new ExcelJS.Workbook();
    const worksheet = workbook.addWorksheet('Template');

    // Add headers
    const headers = [
      'Asset Symbol',
      'Asset Name',
      'Asset Type',
      'Transaction Type',
      'Quantity',
      'Price',
      'Date',
      'Fees',
      'Notes',
      'Currency',
    ];

    worksheet.addRow(headers);

    // Style headers
    const headerRow = worksheet.getRow(1);
    headerRow.font = { bold: true };
    headerRow.fill = {
      type: 'pattern',
      pattern: 'solid',
      fgColor: { argb: 'FFE6E6FA' },
    };

    // Add template data
    templateData.forEach((row) => {
      worksheet.addRow([
        row['Asset Symbol'],
        row['Asset Name'],
        row['Asset Type'],
        row['Transaction Type'],
        row['Quantity'],
        row['Price'],
        row['Date'],
        row['Fees'],
        row['Notes'],
        row['Currency'],
      ]);
    });

    // Set column widths
    worksheet.columns = [
      { width: 15 }, // Asset Symbol
      { width: 30 }, // Asset Name
      { width: 15 }, // Asset Type
      { width: 15 }, // Transaction Type
      { width: 12 }, // Quantity
      { width: 12 }, // Price
      { width: 12 }, // Date
      { width: 10 }, // Fees
      { width: 30 }, // Notes
      { width: 10 }, // Currency
    ];

    // Generate Excel file
    const buffer = await workbook.xlsx.writeBuffer();
    const blob = new Blob([buffer], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });

    saveAs(blob, 'transaction_import_template.xlsx');
  }
}
