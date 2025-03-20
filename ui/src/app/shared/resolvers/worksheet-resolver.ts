import { inject } from '@angular/core';
import { ActivatedRouteSnapshot, ResolveFn } from '@angular/router';
import { TestService } from '../../services/test.service';
import { Paged, Test } from '../../models/test-instance';


export const worksheetListResolver: ResolveFn<Paged<Test>> = (route: ActivatedRouteSnapshot) => {
    const testService = inject(TestService);    
    return testService.search('', [], undefined, 1)
} 