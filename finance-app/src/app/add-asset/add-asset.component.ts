import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { AssetService } from '../services/asset.service';
import { AssetType } from '../models/asset.model';
import { AllowedCryptoDto, AllowedCryptoService } from '../services/allowed-crypto.service';

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

  enabledAllowedCryptos: AllowedCryptoDto[] = [];

  assetTypes = [
    { value: AssetType.Stock, label: 'BIST 100' },
    { value: AssetType.USStock, label: 'US Stock' },
    { value: AssetType.Gold, label: 'Gold' },
    { value: AssetType.Silver, label: 'Silver' },
    { value: AssetType.Fund, label: 'Fund' },
    { value: AssetType.FixedDeposit, label: 'Vadeli Mevduat' },
    { value: AssetType.Crypto, label: 'Crypto' },
  ];

  currencies = [
    { value: 'TRY', label: 'Turkish Lira (₺)' },
    { value: 'USD', label: 'US Dollar ($)' },
    { value: 'EUR', label: 'Euro (€)' },
    { value: 'GBP', label: 'British Pound (£)' },
  ];

  constructor(
    private fb: FormBuilder,
    private assetService: AssetService,
    private allowedCryptoService: AllowedCryptoService,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.initializeForm();

    this.type?.valueChanges.subscribe((rawType) => {
      const selectedType = parseInt(rawType);

      if (selectedType === AssetType.Crypto) {
        this.currency?.setValue('USD', { emitEvent: false });
        this.currency?.disable({ emitEvent: false });

        this.allowedCryptoService.getEnabled().subscribe({
          next: (items) => {
            this.enabledAllowedCryptos = items;
          },
          error: (err) => {
            console.error('Failed to load allowed cryptos:', err);
            this.enabledAllowedCryptos = [];
          },
        });
      } else {
        this.currency?.enable({ emitEvent: false });
      }
    });
  }

  get isCryptoSelected(): boolean {
    const rawType = this.type?.value;
    return parseInt(rawType) === AssetType.Crypto;
  }

  onAllowedCryptoSelected(symbol: string): void {
    const selected = this.enabledAllowedCryptos.find((c) => c.symbol === symbol);
    if (!selected) {
      return;
    }

    this.assetForm.patchValue(
      {
        symbol: selected.symbol,
        name: selected.name,
      },
      { emitEvent: false }
    );
  }

  onAllowedCryptoChange(event: Event): void {
    const target = event.target as HTMLSelectElement | null;
    const symbol = target?.value ?? '';
    this.onAllowedCryptoSelected(symbol);
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
      const selectedType = parseInt(formValue.type);

      if (selectedType === AssetType.Crypto) {
        const symbol = (formValue.symbol || '').toString().trim().toUpperCase();
        const isAllowed = this.enabledAllowedCryptos.some((c) => c.symbol === symbol);
        if (!isAllowed) {
          this.submitError = 'Selected crypto is not allowed.';
          this.isSubmitting = false;
          return;
        }
      }

      const newAsset = {
        symbol: formValue.symbol.toUpperCase(),
        name: formValue.name,
        type: selectedType,
        currentPrice: formValue.currentPrice,
        currency: (this.currency?.disabled ? this.currency?.value : formValue.currency) ?? 'USD',
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

  getAssetTypeLabel(type: AssetType | string): string {
    const parsed = typeof type === 'string' ? parseInt(type) : type;
    const assetType = this.assetTypes.find((t) => t.value === parsed);
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
