import { Injectable } from '@angular/core';
import { Observable, of, BehaviorSubject, catchError } from 'rxjs';
import { Transaction, TransactionType, AssetType } from '../models/asset.model';
import { ApiService } from './api.service';

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
      catchError((error) => {
        console.error('Transaction API call failed:', error);
        return of([]);
      })
    );
  }

  getTransactions(): Observable<Transaction[]> {
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
    const newTransaction: Transaction = {
      ...transaction,
      id: Date.now().toString(),
    };

    const currentTransactions = this.transactionsSubject.value;
    this.transactionsSubject.next([...currentTransactions, newTransaction]);

    return of(newTransaction);
  }

  updateTransaction(id: string, updates: Partial<Transaction>): Observable<Transaction | null> {
    const currentTransactions = this.transactionsSubject.value;
    const index = currentTransactions.findIndex((t) => t.id === id);

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
    const filteredTransactions = currentTransactions.filter((t) => t.id !== id);

    if (filteredTransactions.length === currentTransactions.length) {
      return of(false); // Transaction not found
    }

    this.transactionsSubject.next(filteredTransactions);
    return of(true);
  }
}
