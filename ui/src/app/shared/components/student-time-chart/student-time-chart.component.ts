import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { BubbleChartComponent, BubbleChartMultiSeries, LegendPosition, NgxChartsModule, ScaleType } from '@swimlane/ngx-charts';
import { Color, colorSets } from '@swimlane/ngx-charts';
import { bubble } from './data';



@Component({
  selector: 'app-bubble-chart',
  standalone: true,
  imports: [CommonModule, NgxChartsModule],
  template: `
    <ngx-charts-bubble-chart
[view]="view"
class="chart-container"
[results]="bubble"
[animations]="animations"
[showGridLines]="showGridLines"
[legend]="showLegend"
[legendTitle]="legendTitle"
[legendPosition]="legendPosition"
[xAxis]="showXAxis"
[yAxis]="showYAxis"
[showXAxisLabel]="showXAxisLabel"
[showYAxisLabel]="showYAxisLabel"
[xAxisLabel]="xAxisLabel"
[yAxisLabel]="yAxisLabel"
[autoScale]="autoScale"
[xScaleMin]="xScaleMin"
[xScaleMax]="xScaleMax"
[yScaleMin]="yScaleMin"
[yScaleMax]="yScaleMax"
[scheme]="colorScheme"
[schemeType]="schemeType"
[roundDomains]="roundDomains"
[tooltipDisabled]="tooltipDisabled"
[minRadius]="minRadius"
[maxRadius]="maxRadius"
[trimXAxisTicks]="trimXAxisTicks"
[trimYAxisTicks]="trimYAxisTicks"
[rotateXAxisTicks]="rotateXAxisTicks"
[maxXAxisTickLength]="maxXAxisTickLength"
[maxYAxisTickLength]="maxYAxisTickLength"
[wrapTicks]="wrapTicks"
(activate)="activate($event)"
(deactivate)="deactivate($event)"
(select)="select($event)"
>
</ngx-charts-bubble-chart>
  `,
  styles: [`
    :host { display: block; }
  `]
})
export class StudentTimeChartComponent {

  bubble: any;
  width: number = 700;
  height: number = 300;
  view: [number, number] = [this.width, this.height];
  
  // options
  showXAxis = true;
  showYAxis = true;
  gradient = false;
  showLegend = true;
  legendTitle = 'Legend';
  legendPosition = LegendPosition.Right;
  showXAxisLabel = true;
  tooltipDisabled = false;
  showText = true;
  xAxisLabel = 'Country';
  showYAxisLabel = true;
  yAxisLabel = 'GDP Per Capita';
  showGridLines = true;
  innerPadding = '10%';
  barPadding = 8;
  groupPadding = 16;
  roundDomains = false;
  maxRadius = 10;
  minRadius = 3;
  showSeriesOnHover = true;
  roundEdges: boolean = true;
  animations: boolean = true;
  xScaleMin: any;
  xScaleMax: any;
  yScaleMin: number = 0;
  yScaleMax: number = 100;
  showDataLabel: boolean = false;
  noBarWhenZero: boolean = true;
  trimXAxisTicks: boolean = true;
  trimYAxisTicks: boolean = true;
  rotateXAxisTicks: boolean = true;
  maxXAxisTickLength: number = 16;
  maxYAxisTickLength: number = 16;
  strokeColor: string = '#FFFFFF';
  strokeWidth: number = 2;
  wrapTicks = false;
  target = 90;
  showLabel: boolean = true;

  schemeType = ScaleType.Ordinal;

  colorScheme = {
    name: 'cool',
    selectable: true,
    group: ScaleType.Ordinal,
    domain: [
      '#a8385d',
      '#7aa3e5',
      '#a27ea8',
      '#aae3f5',
      '#adcded',
      '#a95963',
      '#8796c0',
      '#7ed3ed',
      '#50abcc',
      '#ad6886'
    ]
  };
  autoScale = true;

  constructor() {
    Object.assign(this, { bubble });
  }


  select(data: any) {
    console.log('Item clicked', JSON.parse(JSON.stringify(data)));
  }

  activate(data: any) {
    console.log('Activate', JSON.parse(JSON.stringify(data)));
  }

  deactivate(data: any) {
    console.log('Deactivate', JSON.parse(JSON.stringify(data)));
  }
}
