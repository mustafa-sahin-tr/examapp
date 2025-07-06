import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { AssetService } from '../services/asset.service';
import { AssetType } from '../models/asset.model';

@Component({
  selector: 'app-add-asset',
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './add-asset.component.html',
  styleUrl: './add-asset.component.scss',
})
export class AddAssetComponent implements OnInit {
  assetForm!: FormGroup;
  AssetType = AssetType;
  isSubmitting = false;
  submitError: string | null = null;

  assetTypes = [
    { value: AssetType.Stock, label: 'BIST 100' },
    { value: AssetType.USStock, label: 'US Stock' },
    { value: AssetType.Gold, label: 'Gold' },
    { value: AssetType.Silver, label: 'Silver' },
    { value: AssetType.Fund, label: 'Fund' },
    { value: AssetType.FixedDeposit, label: 'Vadeli Mevduat' },
  ];

  currencies = [
    { value: 'TRY', label: 'Turkish Lira (₺)' },
    { value: 'USD', label: 'US Dollar ($)' },
    { value: 'EUR', label: 'Euro (€)' },
    { value: 'GBP', label: 'British Pound (£)' },
  ];

  constructor(private fb: FormBuilder, private assetService: AssetService, private router: Router) {}

  ngOnInit(): void {
    this.initializeForm();
  }

  private initializeForm(): void {
    this.assetForm = this.fb.group({
      symbol: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(10)]],
      name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(200)]],
      type: ['', Validators.required],
      currentPrice: [0, [Validators.required, Validators.min(0.01)]],
      currency: ['TRY', Validators.required],
    });
  }

  onSubmit(): void {
    if (this.assetForm.valid && !this.isSubmitting) {
      this.isSubmitting = true;
      this.submitError = null;

      const formValue = this.assetForm.value;
      const newAsset = {
        symbol: formValue.symbol.toUpperCase(),
        name: formValue.name,
        type: parseInt(formValue.type),
        currentPrice: formValue.currentPrice,
        currency: formValue.currency,
      };

      this.assetService.addAsset(newAsset).subscribe({
        next: (asset) => {
          console.log('Asset added successfully:', asset);
          this.router.navigate(['/dashboard']);
        },
        error: (error) => {
          console.error('Error adding asset:', error);
          this.submitError = 'Failed to add asset. Please try again.';
          this.isSubmitting = false;
        },
      });
    } else {
      this.markFormGroupTouched();
    }
  }

  onCancel(): void {
    this.router.navigate(['/dashboard']);
  }

  private markFormGroupTouched(): void {
    Object.keys(this.assetForm.controls).forEach((key) => {
      const control = this.assetForm.get(key);
      control?.markAsTouched();
    });
  }

  getAssetTypeLabel(type: AssetType): string {
    const assetType = this.assetTypes.find((t) => t.value === type);
    return assetType ? assetType.label : 'Unknown';
  }

  getCurrencySymbol(currency: string): string {
    const symbols: { [key: string]: string } = {
      TRY: '₺',
      USD: '$',
      EUR: '€',
      GBP: '£',
    };
    return symbols[currency] || currency;
  }

  // Form validation getters
  get symbol() {
    return this.assetForm.get('symbol');
  }
  get name() {
    return this.assetForm.get('name');
  }
  get type() {
    return this.assetForm.get('type');
  }
  get currentPrice() {
    return this.assetForm.get('currentPrice');
  }
  get currency() {
    return this.assetForm.get('currency');
  }
}
