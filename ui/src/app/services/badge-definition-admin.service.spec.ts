import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { BadgeDefinitionAdminService } from './badge-definition-admin.service';
import { CreateBadgeDefinitionRequest } from '../models/badge-definition-admin.model';

describe('BadgeDefinitionAdminService', () => {
  const base = '/api/badge/admin/badge-definitions';
  let service: BadgeDefinitionAdminService;
  let http: HttpTestingController;

  const body: CreateBadgeDefinitionRequest = {
    code: 'question-hunter-9',
    name: 'Soru Avcısı 9',
    description: 'd',
    iconUrl: 'achievements/a.svg',
    category: 'Çözüm',
    ruleType: 'AnswerCount',
    ruleConfigJson: '{"target":10}',
    pathKey: null,
    pathName: null,
    pathOrder: null,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(BadgeDefinitionAdminService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('getRuleTypes_GetsRuleTypesThroughGateway', () => {
    service.getRuleTypes().subscribe();
    const req = http.expectOne(`${base}/rule-types`);
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('list_SendsIncludeInactiveSkipTake', () => {
    service.list(true, 50, 50).subscribe((page) => expect(page.totalCount).toBe(0));
    const req = http.expectOne((r) => r.url === base);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('includeInactive')).toBe('true');
    expect(req.request.params.get('skip')).toBe('50');
    expect(req.request.params.get('take')).toBe('50');
    req.flush({ items: [], totalCount: 0 });
  });

  it('list_ActiveOnly_SendsIncludeInactiveFalse', () => {
    service.list(false, 0, 25).subscribe();
    const req = http.expectOne((r) => r.url === base);
    expect(req.request.params.get('includeInactive')).toBe('false');
    req.flush({ items: [], totalCount: 0 });
  });

  it('get_GetsById', () => {
    service.get('abc').subscribe();
    const req = http.expectOne(`${base}/abc`);
    expect(req.request.method).toBe('GET');
    req.flush({});
  });

  it('create_PostsBody', () => {
    service.create(body).subscribe();
    const req = http.expectOne(base);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush({});
  });

  it('update_PutsBodyWithoutCode', () => {
    const { code: _code, ...update } = body;
    service.update('id-1', update).subscribe();
    const req = http.expectOne(`${base}/id-1`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(update);
    expect('code' in req.request.body).toBeFalse();
    req.flush({});
  });

  it('deactivate_PostsToDeactivate', () => {
    service.deactivate('id-1').subscribe();
    const req = http.expectOne(`${base}/id-1/deactivate`);
    expect(req.request.method).toBe('POST');
    req.flush({});
  });

  it('activate_PostsToActivate', () => {
    service.activate('id-1').subscribe();
    const req = http.expectOne(`${base}/id-1/activate`);
    expect(req.request.method).toBe('POST');
    req.flush({});
  });
});
