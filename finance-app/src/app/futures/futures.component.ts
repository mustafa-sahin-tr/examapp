import { Component } from '@angular/core';
import { AssetPortfolioComponent } from '../asset-portfolio/asset-portfolio.component';
import { AssetType } from '../models/asset.model';

@Component({
  selector: 'app-futures',
  imports: [AssetPortfolioComponent],
  templateUrl: './futures.component.html',
  styleUrl: './futures.component.scss'
})
export class FuturesComponent {
  AssetType = AssetType;
}
