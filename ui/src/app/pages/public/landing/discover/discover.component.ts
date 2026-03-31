import { Component } from '@angular/core';
import { LightgalleryModule } from 'lightgallery/angular';

@Component({
  selector: 'app-discover',
  imports: [LightgalleryModule],
  templateUrl: './discover.component.html',
  styleUrls: ['./discover.component.scss'],
  standalone: true,
})
export class DiscoverComponent {}
