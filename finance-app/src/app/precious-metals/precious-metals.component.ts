import { Component } from '@angular/core';
import { AssetPortfolioComponent } from '../asset-portfolio/asset-portfolio.component';
import { AssetType } from '../models/asset.model';

@Component({
  selector: 'app-precious-metals',
  imports: [AssetPortfolioComponent],
  templateUrl: './precious-metals.component.html',
  styleUrl: './precious-metals.component.scss'
})
export class PreciousMetalsComponent {
  AssetType = AssetType;
}
