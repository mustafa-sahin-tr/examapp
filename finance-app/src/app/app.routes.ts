import { Routes } from '@angular/router';
import { DashboardComponent } from './dashboard/dashboard.component';
import { Bist100Component } from './bist100/bist100.component';
import { UsStocksComponent } from './us-stocks/us-stocks.component';
import { PreciousMetalsComponent } from './precious-metals/precious-metals.component';
import { FundsComponent } from './funds/funds.component';
import { FuturesComponent } from './futures/futures.component';
import { AddTransactionComponent } from './add-transaction/add-transaction.component';
import { HomeComponent } from './home/home.component';
import { DetailpageComponent } from './detailpage/detailpage.component';

export const routes: Routes = [
  { path: '', redirectTo: '/dashboard', pathMatch: 'full' },
  { path: 'dashboard', component: DashboardComponent },
  { path: 'bist100', component: Bist100Component },
  { path: 'us-stocks', component: UsStocksComponent },
  { path: 'precious-metals', component: PreciousMetalsComponent },
  { path: 'funds', component: FundsComponent },
  { path: 'futures', component: FuturesComponent },
  { path: 'add-transaction', component: AddTransactionComponent },
  { path: 'home', component: HomeComponent },
  { path: 'detailpage', component: DetailpageComponent },
  { path: '**', redirectTo: '/dashboard' }
];
