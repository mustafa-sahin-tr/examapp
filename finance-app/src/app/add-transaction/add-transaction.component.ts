import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { Asset, AssetType, TransactionType } from '../models/asset.model';
import { AssetService } from '../services/asset.service';
import { TransactionService } from '../services/transaction.service';

@Component({
  selector: 'app-add-transaction',
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  templateUrl: './add-transaction.component.html',
  styleUrl: './add-transaction.component.scss',
})
export class AddTransactionComponent implements OnInit {
  transactionForm!: FormGroup;
  assets$!: Observable<Asset[]>;
  filteredAssets: Asset[] = [];

  AssetType = AssetType;
  TransactionType = TransactionType;

  assetTypes = [
    { value: AssetType.BIST100, label: 'BIST 100' },
    { value: AssetType.US_STOCK, label: 'US Stocks' },
    { value: AssetType.GOLD, label: 'Gold' },
    { value: AssetType.SILVER, label: 'Silver' },
    { value: AssetType.FUND, label: 'Funds' },
    { value: AssetType.FUTURES, label: 'Futures' },
  ];

  transactionTypes = [
    { value: TransactionType.BUY, label: 'Buy' },
    { value: TransactionType.SELL, label: 'Sell' },
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
    this.assets$.subscribe((assets) => {
      this.filteredAssets = assets;
    });
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
    this.transactionForm.get('assetType')?.valueChanges.subscribe((assetType) => {
      console.log('Asset type changed:', assetType, typeof assetType);

      this.assets$.subscribe((assets) => {
        console.log('All assets:', assets);

        if (assetType && assetType !== '') {
          // Convert string to number for comparison
          const selectedAssetType = parseInt(assetType);
          console.log('Selected asset type as number:', selectedAssetType);

          this.filteredAssets = assets.filter((asset) => {
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

        // Clear asset selection when type changes
        this.transactionForm.patchValue({ assetId: '' });
      });
    });
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
}
