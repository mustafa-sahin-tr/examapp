import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { LEADERBOARD_PAGE_SIZE, LeaderboardService } from './leaderboard.service';
import { LeaderboardResult } from '../models/leaderboard.model';

describe('LeaderboardService', () => {
  let service: LeaderboardService;
  let httpMock: HttpTestingController;

  const sampleResult: LeaderboardResult = {
    success: true,
    scope: 'global',
    schoolId: null,
    schoolScopeAvailable: true,
    totalCount: 1,
    skip: 0,
    take: 20,
    entries: [{ rank: 1, xp: 120, level: 2, fullName: 'Ayşe', avatarUrl: '', isMe: true }],
    myRank: 1,
    myXp: 120,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(LeaderboardService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('getLeaderboard_Default_SendsGetToGatewayWithScopeSkipTake', () => {
    service.getLeaderboard('global').subscribe();

    const req = httpMock.expectOne((r) => r.url === '/api/exam/leaderboard');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('scope')).toBe('global');
    expect(req.request.params.get('skip')).toBe('0');
    expect(req.request.params.get('take')).toBe(String(LEADERBOARD_PAGE_SIZE));
    req.flush(sampleResult);
  });

  it('getLeaderboard_SchoolWithPaging_SendsGivenParams', () => {
    service.getLeaderboard('school', 40, 10).subscribe();

    const req = httpMock.expectOne((r) => r.url === '/api/exam/leaderboard');
    expect(req.request.params.get('scope')).toBe('school');
    expect(req.request.params.get('skip')).toBe('40');
    expect(req.request.params.get('take')).toBe('10');
    req.flush(sampleResult);
  });

  it('getLeaderboard_Success_ReturnsTypedResult', (done) => {
    service.getLeaderboard('global').subscribe((result) => {
      expect(result.entries[0].fullName).toBe('Ayşe');
      expect(result.myRank).toBe(1);
      done();
    });

    httpMock.expectOne((r) => r.url === '/api/exam/leaderboard').flush(sampleResult);
  });

  it('getLeaderboard_400_ErrorsWithStatusAndBodyMessage', (done) => {
    service.getLeaderboard('school').subscribe({
      next: () => fail('400 hata olarak akmalı'),
      error: (err: HttpErrorResponse) => {
        expect(err.status).toBe(400);
        expect(service.extractError(err)).toBe('Okul bulunamadı');
        done();
      },
    });

    httpMock
      .expectOne((r) => r.url === '/api/exam/leaderboard')
      .flush({ message: 'Okul bulunamadı' }, { status: 400, statusText: 'Bad Request' });
  });

  it('extractError_NoBodyMessage_ReturnsNull', () => {
    expect(service.extractError(new HttpErrorResponse({ status: 500, error: null }))).toBeNull();
    expect(service.extractError(new HttpErrorResponse({ status: 400, error: { message: '   ' } }))).toBeNull();
  });
});
