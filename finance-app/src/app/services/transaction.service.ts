import { Injectable } from '@angular/core';
import { Observable, of, BehaviorSubject } from 'rxjs';
import { Transaction, TransactionType, AssetType } from '../models/asset.model';

@Injectable({
  providedIn: 'root'
})
export class TransactionService {
  private transactionsSubject = new BehaviorSubject<Transaction[]>([]);
  public transactions$ = this.transactionsSubject.asObservable();

  constructor() {
    this.initializeDummyTransactions();
  }

  private initializeDummyTransactions(): void {
    const dummyTransactions: Transaction[] = [
      {
        id: '1',
        assetId: '1', // TUPRS
        type: TransactionType.BUY,
        quantity: 100,
        price: 80.25,
        date: new Date('2024-01-15'),
        fees: 5.50,
        notes: 'İlk alış'
      },
      {
        id: '2',
        assetId: '1', // TUPRS
        type: TransactionType.BUY,
        quantity: 50,
        price: 83.40,
        date: new Date('2024-02-20'),
        fees: 2.75,
        notes: 'Ek alış'
      },
      {
        id: '3',
        assetId: '4', // AAPL
        type: TransactionType.BUY,
        quantity: 25,
        price: 175.20,
        date: new Date('2024-01-10'),
        fees: 12.00,
        notes: 'Apple hisse alımı'
      },
      {
        id: '4',
        assetId: '6', // GOLD
        type: TransactionType.BUY,
        quantity: 2,
        price: 1950.00,
        date: new Date('2024-03-05'),
        fees: 25.00,
        notes: 'Altın yatırımı'
      },
      {
        id: '5',
        assetId: '2', // AKBNK
        type: TransactionType.BUY,
        quantity: 200,
        price: 40.15,
        date: new Date('2024-02-28'),
        fees: 8.00,
        notes: 'Banka hissesi'
      },
      {
        id: '6',
        assetId: '2', // AKBNK
        type: TransactionType.SELL,
        quantity: 50,
        price: 43.20,
        date: new Date('2024-06-15'),
        fees: 2.00,
        notes: 'Kar realizasyonu'
      },
      // BIST100 hisse senetleri için ek transaction'lar
      {
        id: '7',
        assetId: '3', // THYAO
        type: TransactionType.BUY,
        quantity: 75,
        price: 148.20,
        date: new Date('2024-01-20'),
        fees: 6.20,
        notes: 'THY alış'
      },
      {
        id: '8',
        assetId: '9', // ASELS
        type: TransactionType.BUY,
        quantity: 120,
        price: 118.50,
        date: new Date('2024-02-05'),
        fees: 7.80,
        notes: 'Savunma sanayi yatırımı'
      },
      {
        id: '9',
        assetId: '10', // EREGL
        type: TransactionType.BUY,
        quantity: 300,
        price: 29.15,
        date: new Date('2024-02-12'),
        fees: 4.50,
        notes: 'Çelik sektörü alış'
      },
      {
        id: '10',
        assetId: '11', // KCHOL
        type: TransactionType.BUY,
        quantity: 60,
        price: 158.40,
        date: new Date('2024-01-25'),
        fees: 9.50,
        notes: 'Holding hissesi'
      },
      {
        id: '11',
        assetId: '12', // SAHOL
        type: TransactionType.BUY,
        quantity: 180,
        price: 47.20,
        date: new Date('2024-03-01'),
        fees: 6.80,
        notes: 'Sabancı Holding'
      },
      {
        id: '12',
        assetId: '13', // VAKBN
        type: TransactionType.BUY,
        quantity: 500,
        price: 15.90,
        date: new Date('2024-02-18'),
        fees: 8.20,
        notes: 'Banka hissesi alım'
      },
      {
        id: '13',
        assetId: '14', // ISCTR
        type: TransactionType.BUY,
        quantity: 800,
        price: 10.45,
        date: new Date('2024-01-30'),
        fees: 12.50,
        notes: 'İş Bankası alış'
      },
      {
        id: '14',
        assetId: '15', // GARAN
        type: TransactionType.BUY,
        quantity: 90,
        price: 86.20,
        date: new Date('2024-02-25'),
        fees: 7.20,
        notes: 'Garanti BBVA'
      },
      {
        id: '15',
        assetId: '16', // TCELL
        type: TransactionType.BUY,
        quantity: 150,
        price: 48.80,
        date: new Date('2024-03-10'),
        fees: 5.40,
        notes: 'Turkcell alış'
      },
      {
        id: '16',
        assetId: '17', // TTKOM
        type: TransactionType.BUY,
        quantity: 400,
        price: 14.25,
        date: new Date('2024-02-08'),
        fees: 6.60,
        notes: 'Türk Telekom'
      },
      {
        id: '17',
        assetId: '18', // BIMAS
        type: TransactionType.BUY,
        quantity: 40,
        price: 185.20,
        date: new Date('2024-01-18'),
        fees: 8.90,
        notes: 'BİM perakende'
      },
      {
        id: '18',
        assetId: '19', // MGROS
        type: TransactionType.BUY,
        quantity: 220,
        price: 36.80,
        date: new Date('2024-03-15'),
        fees: 5.20,
        notes: 'Migros alış'
      },
      {
        id: '19',
        assetId: '20', // KOZAL
        type: TransactionType.BUY,
        quantity: 80,
        price: 142.50,
        date: new Date('2024-02-14'),
        fees: 9.80,
        notes: 'Altın madenciliği'
      },
      {
        id: '20',
        assetId: '21', // PETKM
        type: TransactionType.BUY,
        quantity: 600,
        price: 9.85,
        date: new Date('2024-01-28'),
        fees: 7.50,
        notes: 'Petrokimya yatırımı'
      },
      {
        id: '21',
        assetId: '22', // TOASO
        type: TransactionType.BUY,
        quantity: 110,
        price: 74.60,
        date: new Date('2024-03-08'),
        fees: 6.40,
        notes: 'Otomotiv sektörü'
      },
      {
        id: '22',
        assetId: '23', // FROTO
        type: TransactionType.BUY,
        quantity: 65,
        price: 138.90,
        date: new Date('2024-02-22'),
        fees: 8.10,
        notes: 'Ford Otosan alış'
      },
      // Bazı SELL transaction'ları da ekleyelim
      {
        id: '23',
        assetId: '3', // THYAO
        type: TransactionType.SELL,
        quantity: 25,
        price: 155.80,
        date: new Date('2024-06-20'),
        fees: 2.50,
        notes: 'Kar realizasyonu'
      },
      {
        id: '24',
        assetId: '13', // VAKBN
        type: TransactionType.SELL,
        quantity: 100,
        price: 16.90,
        date: new Date('2024-06-18'),
        fees: 1.80,
        notes: 'Kısmi satış'
      },
      {
        id: '25',
        assetId: '16', // TCELL
        type: TransactionType.SELL,
        quantity: 50,
        price: 51.20,
        date: new Date('2024-06-25'),
        fees: 1.60,
        notes: 'Pozisyon küçültme'
      }
    ];

    this.transactionsSubject.next(dummyTransactions);
  }

  getTransactions(): Observable<Transaction[]> {
    return this.transactions$;
  }

  getTransactionsByAssetId(assetId: string): Observable<Transaction[]> {
    return new Observable(observer => {
      this.transactions$.subscribe(transactions => {
        const filtered = transactions.filter(t => t.assetId === assetId);
        observer.next(filtered);
      });
    });
  }

  getTransactionsByType(type: AssetType): Observable<Transaction[]> {
    // This would need asset information to filter by type
    // For now, return all transactions
    return this.transactions$;
  }

  addTransaction(transaction: Omit<Transaction, 'id'>): Observable<Transaction> {
    const newTransaction: Transaction = {
      ...transaction,
      id: Date.now().toString()
    };

    const currentTransactions = this.transactionsSubject.value;
    this.transactionsSubject.next([...currentTransactions, newTransaction]);

    return of(newTransaction);
  }

  updateTransaction(id: string, updates: Partial<Transaction>): Observable<Transaction | null> {
    const currentTransactions = this.transactionsSubject.value;
    const index = currentTransactions.findIndex(t => t.id === id);
    
    if (index === -1) {
      return of(null);
    }

    const updatedTransaction = { ...currentTransactions[index], ...updates };
    currentTransactions[index] = updatedTransaction;
    this.transactionsSubject.next([...currentTransactions]);

    return of(updatedTransaction);
  }

  deleteTransaction(id: string): Observable<boolean> {
    const currentTransactions = this.transactionsSubject.value;
    const filteredTransactions = currentTransactions.filter(t => t.id !== id);
    
    if (filteredTransactions.length === currentTransactions.length) {
      return of(false); // Transaction not found
    }

    this.transactionsSubject.next(filteredTransactions);
    return of(true);
  }
}
