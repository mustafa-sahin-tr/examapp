import { NgModule } from '@angular/core';
import { CacheService } from '../services/cache.service';
import { OnlineStatusService } from '../services/online-status.service';

@NgModule({
  providers: [CacheService, OnlineStatusService],
})
export class CoreModule {}
