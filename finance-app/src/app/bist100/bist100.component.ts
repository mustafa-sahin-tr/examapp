import { Component } from '@angular/core';
import { AssetPortfolioComponent } from '../asset-portfolio/asset-portfolio.component';
import { AssetType } from '../models/asset.model';

@Component({
  selector: 'app-bist100',
  imports: [AssetPortfolioComponent],
  templateUrl: './bist100.component.html',
  styleUrl: './bist100.component.scss'
})
export class Bist100Component {
  AssetType = AssetType;
}
