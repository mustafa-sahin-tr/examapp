import { Component } from '@angular/core';
import { AssetPortfolioComponent } from '../asset-portfolio/asset-portfolio.component';
import { AssetType } from '../models/asset.model';

@Component({
  selector: 'app-funds',
  imports: [AssetPortfolioComponent],
  templateUrl: './funds.component.html',
  styleUrl: './funds.component.scss'
})
export class FundsComponent {
  AssetType = AssetType;
}
