import { Component } from '@angular/core';
import { AssetPortfolioComponent } from '../asset-portfolio/asset-portfolio.component';
import { AssetType } from '../models/asset.model';

@Component({
  selector: 'app-precious-metals-silver',
  imports: [AssetPortfolioComponent],
  templateUrl: './precious-metals-silver.component.html',
  styleUrl: './precious-metals-silver.component.scss',
})
export class PreciousMetalsSilverComponent {
  AssetType = AssetType;
}
