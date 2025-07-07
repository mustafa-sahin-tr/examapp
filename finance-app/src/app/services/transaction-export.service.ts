import { Injectable } from '@angular/core';
import { Observable, of, forkJoin } from 'rxjs';
import { Transaction, TransactionType, AssetType, Asset } from '../models/asset.model';
import { ApiService } from './api.service';
import { AssetService } from './asset.service';
import { TransactionService } from './transaction.service';
import * as ExcelJS from 'exceljs';
import { saveAs } from 'file-saver';

// User-friendly lookup values for Excel export/import
export const ASSET_TYPE_LABELS = {
  [AssetType.Stock]: 'BIST 100',
  [AssetType.USStock]: 'US Stocks',
  [AssetType.Gold]: 'Gold',
  [AssetType.Silver]: 'Silver',
  [AssetType.Fund]: 'Funds',
  [AssetType.FixedDeposit]: 'Vadeli Mevduat',
};

export const TRANSACTION_TYPE_LABELS = {
  [TransactionType.BUY]: 'Buy',
  [TransactionType.SELL]: 'Sell',
  [TransactionType.DEPOSIT_ADD]: 'Para Ekle',
  [TransactionType.DEPOSIT_WITHDRAW]: 'Para Çıkar',
  [TransactionType.DEPOSIT_INCOME]: 'Faiz Geliri',
};

// Reverse lookup for import
export const ASSET_TYPE_VALUES = Object.entries(ASSET_TYPE_LABELS).reduce((acc, [key, value]) => {
  acc[value] = parseInt(key);
  return acc;
}, {} as Record<string, AssetType>);

export const TRANSACTION_TYPE_VALUES = Object.entries(TRANSACTION_TYPE_LABELS).reduce((acc, [key, value]) => {
  acc[value] = key as TransactionType;
  return acc;
}, {} as Record<string, TransactionType>);

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
    private transactionService: TransactionService
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
          const existingAsset = assets.find((a) => a.symbol === row['Asset Symbol'] && a.type === assetType);

          if (existingAsset) {
            observer.next(existingAsset);
            observer.complete();
            return;
          }

          // Create new asset if not found
          const newAsset = {
            symbol: row['Asset Symbol'],
            name: row['Asset Name'] || row['Asset Symbol'],
            type: assetType,
            currentPrice: row['Price'],
            currency: row['Currency'] || 'TRY',
          };

          this.assetService.addAsset(newAsset).subscribe({
            next: (createdAsset) => {
              observer.next(createdAsset);
              observer.complete();
            },
            error: (error) => observer.error(error),
          });
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
      {
        'Asset Symbol': 'AKBNK',
        'Asset Name': 'Akbank T.A.S.',
        'Asset Type': 'BIST 100',
        'Transaction Type': 'Buy',
        Quantity: 100,
        Price: 25.5,
        Date: '2024-01-15',
        Fees: 10.2,
        Notes: 'Sample transaction',
        Currency: 'TRY',
      },
      {
        'Asset Symbol': 'AAPL',
        'Asset Name': 'Apple Inc.',
        'Asset Type': 'US Stocks',
        'Transaction Type': 'Buy',
        Quantity: 10,
        Price: 150.0,
        Date: '2024-01-16',
        Fees: 1.0,
        Notes: 'US stock purchase',
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
