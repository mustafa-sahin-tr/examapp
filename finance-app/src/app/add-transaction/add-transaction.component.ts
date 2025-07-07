import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Observable, Subject, takeUntil, distinctUntilChanged, map } from 'rxjs';
import { Asset, AssetType, TransactionType } from '../models/asset.model';
import { AssetService } from '../services/asset.service';
import { TransactionService } from '../services/transaction.service';

@Component({
  selector: 'app-add-transaction',
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  templateUrl: './add-transaction.component.html',
  styleUrl: './add-transaction.component.scss',
})
export class AddTransactionComponent implements OnInit, OnDestroy {
  transactionForm!: FormGroup;
  assets$!: Observable<Asset[]>;
  filteredAssets: Asset[] = [];
  allAssets: Asset[] = [];

  private destroy$ = new Subject<void>();

  AssetType = AssetType;
  TransactionType = TransactionType;

  assetTypes = [
    { value: AssetType.Stock, label: 'BIST 100' },
    { value: AssetType.USStock, label: 'US Stocks' },
    { value: AssetType.Gold, label: 'Gold' },
    { value: AssetType.Silver, label: 'Silver' },
    { value: AssetType.Fund, label: 'Funds' },
    { value: AssetType.FixedDeposit, label: 'Vadeli Mevduat' },
  ];

  transactionTypes = [
    { value: TransactionType.BUY, label: 'Buy' },
    { value: TransactionType.SELL, label: 'Sell' },
    { value: TransactionType.DEPOSIT_ADD, label: 'Para Ekle' },
    { value: TransactionType.DEPOSIT_WITHDRAW, label: 'Para Çıkar' },
    { value: TransactionType.DEPOSIT_INCOME, label: 'Faiz Geliri' },
  ];

  constructor(
    private fb: FormBuilder,
    private assetService: AssetService,
    private transactionService: TransactionService,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.initializeForm();
    this.assets$ = this.assetService.getAssets();

    // Subscribe to asset changes and maintain state
    this.assets$.pipe(takeUntil(this.destroy$)).subscribe((assets) => {
      this.allAssets = assets;

      // Re-filter assets if asset type is already selected
      const selectedAssetType = this.transactionForm.get('assetType')?.value;
      if (selectedAssetType) {
        this.filterAssetsByType(selectedAssetType);
      } else {
        this.filteredAssets = assets;
      }
    });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private initializeForm(): void {
    this.transactionForm = this.fb.group({
      assetType: ['', Validators.required],
      assetId: ['', Validators.required],
      transactionType: [TransactionType.BUY, Validators.required],
      quantity: ['', [Validators.required, Validators.min(0.01)]],
      price: ['', [Validators.required, Validators.min(0.01)]],
      date: [new Date().toISOString().split('T')[0], Validators.required],
      fees: [0, [Validators.min(0)]],
      notes: [''],
    });

    // Filter assets when asset type changes
    this.transactionForm
      .get('assetType')
      ?.valueChanges.pipe(takeUntil(this.destroy$), distinctUntilChanged())
      .subscribe((assetType) => {
        console.log('Asset type changed:', assetType, typeof assetType);
        this.filterAssetsByType(assetType);
        // Clear asset selection when type changes
        this.transactionForm.patchValue({ assetId: '', transactionType: '' });
      });

    // Auto-calculate fees for BIST transactions
    this.transactionForm
      .get('quantity')
      ?.valueChanges.pipe(takeUntil(this.destroy$))
      .subscribe(() => {
        this.calculateBistFees();
      });

    this.transactionForm
      .get('price')
      ?.valueChanges.pipe(takeUntil(this.destroy$))
      .subscribe(() => {
        this.calculateBistFees();
      });

    this.transactionForm
      .get('assetType')
      ?.valueChanges.pipe(takeUntil(this.destroy$))
      .subscribe(() => {
        this.calculateBistFees();
      });
  }

  private filterAssetsByType(assetType: string): void {
    if (assetType && assetType !== '') {
      // Convert string to number for comparison
      const selectedAssetType = parseInt(assetType);
      console.log('Selected asset type as number:', selectedAssetType);

      // Filter from all assets, not creating new subscription
      this.filteredAssets = this.allAssets.filter((asset) => {
        console.log(
          `Comparing asset ${asset.symbol}: ${asset.type} === ${selectedAssetType}`,
          asset.type === selectedAssetType
        );
        return asset.type === selectedAssetType;
      });

      console.log('Filtered assets:', this.filteredAssets);
    } else {
      this.filteredAssets = [];
    }
  }

  onSubmit(): void {
    if (this.transactionForm.valid) {
      const formValue = this.transactionForm.value;

      const transaction = {
        assetId: formValue.assetId,
        type: formValue.transactionType,
        quantity: parseFloat(formValue.quantity),
        price: parseFloat(formValue.price),
        date: new Date(formValue.date),
        fees: parseFloat(formValue.fees) || 0,
        notes: formValue.notes || '',
      };

      this.transactionService.addTransaction(transaction).subscribe({
        next: (savedTransaction) => {
          console.log('Transaction saved:', savedTransaction);
          this.router.navigate(['/dashboard']);
        },
        error: (error) => {
          console.error('Error saving transaction:', error);
        },
      });
    }
  }

  onCancel(): void {
    this.router.navigate(['/dashboard']);
  }

  getSelectedAsset(): Asset | undefined {
    const assetId = this.transactionForm.get('assetId')?.value;
    return this.filteredAssets.find((asset) => asset.id === assetId);
  }

  calculateTotal(): number {
    const quantity = parseFloat(this.transactionForm.get('quantity')?.value) || 0;
    const price = parseFloat(this.transactionForm.get('price')?.value) || 0;
    const fees = parseFloat(this.transactionForm.get('fees')?.value) || 0;
    return quantity * price + fees;
  }

  private calculateBistFees(): void {
    const assetType = parseInt(this.transactionForm.get('assetType')?.value);
    const quantity = parseFloat(this.transactionForm.get('quantity')?.value) || 0;
    const price = parseFloat(this.transactionForm.get('price')?.value) || 0;

    // BIST için otomatik fee hesaplama (on binde dört = %0.04)
    if (assetType === AssetType.Stock && quantity > 0 && price > 0) {
      const transactionAmount = quantity * price;
      const bistFee = transactionAmount * 0.0004; // %0.04 = 0.0004

      // Fees input'unu güncelle
      this.transactionForm.patchValue({ fees: bistFee.toFixed(2) }, { emitEvent: false });
    } else if (assetType !== AssetType.Stock) {
      // BIST değilse fee'yi sıfırla
      this.transactionForm.patchValue({ fees: 0 }, { emitEvent: false });
    }
  }

  isBistTransaction(): boolean {
    const assetType = parseInt(this.transactionForm.get('assetType')?.value);
    return assetType === AssetType.Stock;
  }

  getFilteredTransactionTypes(): any[] {
    const assetType = parseInt(this.transactionForm.get('assetType')?.value);

    if (assetType === AssetType.FixedDeposit) {
      // Vadeli mevduat için özel transaction tipleri
      return [
        { value: TransactionType.DEPOSIT_ADD, label: 'Para Ekle' },
        { value: TransactionType.DEPOSIT_WITHDRAW, label: 'Para Çıkar' },
        { value: TransactionType.DEPOSIT_INCOME, label: 'Faiz Geliri' },
      ];
    } else {
      // Diğer asset'ler için normal transaction tipleri
      return [
        { value: TransactionType.BUY, label: 'Buy' },
        { value: TransactionType.SELL, label: 'Sell' },
      ];
    }
  }
}
