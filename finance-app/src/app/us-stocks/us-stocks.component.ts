import { Component } from '@angular/core';
import { AssetPortfolioComponent } from '../asset-portfolio/asset-portfolio.component';
import { AssetType } from '../models/asset.model';

@Component({
  selector: 'app-us-stocks',
  imports: [AssetPortfolioComponent],
  templateUrl: './us-stocks.component.html',
  styleUrl: './us-stocks.component.scss'
})
export class UsStocksComponent {
  AssetType = AssetType;
}
