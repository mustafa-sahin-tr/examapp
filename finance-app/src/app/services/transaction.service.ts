import { Injectable } from '@angular/core';
import { Observable, of, BehaviorSubject, catchError, map, tap } from 'rxjs';
import { Transaction, TransactionType, AssetType } from '../models/asset.model';
import { ApiService } from './api.service';
import { environment } from '../../environments/environment';

@Injectable({
  providedIn: 'root',
})
export class TransactionService {
  private transactionsSubject = new BehaviorSubject<Transaction[]>([]);
  public transactions$ = this.transactionsSubject.asObservable();

  constructor(private apiService: ApiService) {
    this.loadTransactions();
  }

  private loadTransactions(): void {
    this.getTransactionsFromApi().subscribe({
      next: (transactions) => {
        this.transactionsSubject.next(transactions);
      },
      error: (error) => {
        console.error('Error loading transactions:', error);
        this.transactionsSubject.next([]);
      },
    });
  }

  private getTransactionsFromApi(): Observable<Transaction[]> {
    return this.apiService.get<Transaction[]>('/transactions').pipe(
      map((transactions: any[]) => {
        // Convert date strings to Date objects
        return transactions.map((t) => ({
          ...t,
          date: new Date(t.date),
        }));
      }),
      catchError((error) => {
        console.error('Transaction API call failed:', error);
        return of([]);
      })
    );
  }

  getTransactions(): Observable<Transaction[]> {
    console.log('🔍 TransactionService.getTransactions() called');
    console.log('📊 Current transactions count:', this.transactionsSubject.value.length);
    return this.transactions$;
  }

  getTransactionsByAssetId(assetId: string): Observable<Transaction[]> {
    return new Observable((observer) => {
      this.transactions$.subscribe((transactions) => {
        const filtered = transactions.filter((t) => t.assetId === assetId);
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
    // Backend'e POST request gönder
    const transactionDto = {
      assetId: transaction.assetId,
      type: transaction.type,
      quantity: transaction.quantity,
      price: transaction.price,
      date: transaction.date.toISOString(),
      fees: transaction.fees || 0,
      notes: transaction.notes || '',
    };

    console.log('Sending transaction to backend:', transactionDto);
    console.log('API URL will be:', `${environment.apiUrl || 'http://localhost:5678/api/finance'}/transactions`);

    return this.apiService.post<Transaction>('/transactions', transactionDto).pipe(
      map((newTransaction: Transaction) => {
        console.log('Transaction successfully created in backend:', newTransaction);
        // Backend'den dönen transaction'ı local state'e ekle
        const currentTransactions = this.transactionsSubject.value;
        this.transactionsSubject.next([...currentTransactions, newTransaction]);
        return newTransaction;
      }),
      catchError((error) => {
        console.error('Error adding transaction to backend:', error);
        console.error('Error details:', error.error, error.status, error.statusText);

        // Backend hata verirse fallback olarak local state'e ekle
        const newTransaction: Transaction = {
          ...transaction,
          id: Date.now().toString(),
        };

        console.log('Falling back to local storage:', newTransaction);
        const currentTransactions = this.transactionsSubject.value;
        this.transactionsSubject.next([...currentTransactions, newTransaction]);

        return of(newTransaction);
      })
    );
  }

  updateTransaction(id: string, updates: Partial<Transaction>): Observable<Transaction | null> {
    const currentTransactions = this.transactionsSubject.value;
    const index = currentTransactions.findIndex((t) => t.id === id);

    if (index === -1) {
      return of(null);
    }

    const payload: any = { ...updates };
    if (updates.date instanceof Date) {
      payload.date = updates.date.toISOString();
    }

    return this.apiService.put<Transaction>(`/transactions/${id}`, payload).pipe(
      map((response) => ({ ...response, date: new Date(response.date) } as Transaction)),
      tap((updatedTransaction) => {
        const updatedTransactions = [...this.transactionsSubject.value];
        updatedTransactions[index] = { ...updatedTransactions[index], ...updatedTransaction };
        this.transactionsSubject.next(updatedTransactions);
      }),
      catchError((error) => {
        console.error('Error updating transaction on backend:', error);
        const fallbackUpdated = { ...currentTransactions[index], ...updates } as Transaction;
        const updatedTransactions = [...this.transactionsSubject.value];
        updatedTransactions[index] = fallbackUpdated;
        this.transactionsSubject.next(updatedTransactions);
        return of(fallbackUpdated);
      })
    );
  }

  deleteTransaction(id: string): Observable<boolean> {
    const currentTransactions = this.transactionsSubject.value;
    const filteredTransactions = currentTransactions.filter((t) => t.id !== id);

    if (filteredTransactions.length === currentTransactions.length) {
      return of(false); // Transaction not found
    }

    this.transactionsSubject.next(filteredTransactions);
    return of(true);
  }
}
