import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router, provideRouter } from '@angular/router';
import { provideNativeDateAdapter } from '@angular/material/core';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';

import { TeacherAvailabilityComponent } from './teacher-availability.component';
import { BookingService } from '../../services/booking.service';
import { AvailabilitySlot } from '../../models/booking.model';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import { HttpErrorResponse } from '@angular/common/http';
import { NO_ERRORS_SCHEMA } from '@angular/core';

const translocoTesting = translocoTestingModule();

describe('TeacherAvailabilityComponent', () => {
  let component: TeacherAvailabilityComponent;
  let fixture: ComponentFixture<TeacherAvailabilityComponent>;
  let bookingService: jasmine.SpyObj<BookingService>;
  let snackBar: jasmine.SpyObj<MatSnackBar>;

  const mockSlot: AvailabilitySlot = {
    id: 1,
    teacherId: 10,
    date: '2026-09-20',
    startTime: '14:00:00',
    endTime: '15:00:00',
    createdAt: '2026-09-15T10:00:00Z',
    startUtc: '2026-09-20T12:00:00Z',
    endUtc: '2026-09-20T13:00:00Z',
    isBooked: false,
  };

  beforeEach(async () => {
    bookingService = jasmine.createSpyObj<BookingService>('BookingService', [
      'getAllMySlots',
      'createSlot',
      'deleteSlot',
      'extractError',
    ]);
    bookingService.getAllMySlots.and.returnValue(of({ items: [mockSlot], success: true }));
    bookingService.createSlot.and.returnValue(of({ success: true, slot: mockSlot }));
    bookingService.deleteSlot.and.returnValue(of());
    bookingService.extractError.and.returnValue('Error message');

    snackBar = jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']);

    await TestBed.configureTestingModule({
      imports: [TeacherAvailabilityComponent, HttpClientTestingModule, translocoTesting, BrowserAnimationsModule],
      providers: [
        { provide: BookingService, useValue: bookingService },
        { provide: MatSnackBar, useValue: snackBar },
        provideRouter([]),
        provideNativeDateAdapter(),
      ],
      schemas: [NO_ERRORS_SCHEMA],
    }).compileComponents();

    fixture = TestBed.createComponent(TeacherAvailabilityComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should call getAllMySlots on init', fakeAsync(() => {
    fixture.detectChanges();
    tick();

    expect(bookingService.getAllMySlots).toHaveBeenCalled();
  }));

  it('should render page title', fakeAsync(() => {
    fixture.detectChanges();
    tick();

    const title = fixture.nativeElement.querySelector('h1');
    expect(title).toBeTruthy();
  }));

  it('should render form with date input', fakeAsync(() => {
    fixture.detectChanges();
    tick();

    const form = fixture.nativeElement.querySelector('.avail__form');
    expect(form).toBeTruthy();
  }));

  it('should render grid component when no error', fakeAsync(() => {
    fixture.detectChanges();
    tick();

    const grid = fixture.nativeElement.querySelector('app-availability-week-grid');
    expect(grid).toBeTruthy();
  }));

  it('should not render grid when error occurs', fakeAsync(() => {
    const errorResponse = new HttpErrorResponse({ status: 500, statusText: 'Server Error' });
    bookingService.getAllMySlots.and.returnValue(throwError(() => errorResponse));
    bookingService.extractError.and.returnValue('Failed to load slots');

    fixture.detectChanges();
    tick();

    const grid = fixture.nativeElement.querySelector('app-availability-week-grid');
    expect(grid).toBeFalsy();
  }));

  it('should show error message when load fails', fakeAsync(() => {
    const errorResponse = new HttpErrorResponse({ status: 500, statusText: 'Server Error' });
    bookingService.getAllMySlots.and.returnValue(throwError(() => errorResponse));
    bookingService.extractError.and.returnValue('Failed to load slots');

    fixture.detectChanges();
    tick();

    const errorBlock = fixture.nativeElement.querySelector('.avail__state--error');
    expect(errorBlock).toBeTruthy();
  }));

  it('should show retry button in error state', fakeAsync(() => {
    const errorResponse = new HttpErrorResponse({ status: 500, statusText: 'Server Error' });
    bookingService.getAllMySlots.and.returnValue(throwError(() => errorResponse));
    bookingService.extractError.and.returnValue('Failed to load slots');

    fixture.detectChanges();
    tick();

    const errorBlock = fixture.nativeElement.querySelector('.avail__state--error');
    const retryButton = errorBlock?.querySelector('button');
    expect(retryButton).toBeTruthy();
  }));

  it('should show empty state when no slots', fakeAsync(() => {
    bookingService.getAllMySlots.and.returnValue(of({ items: [], success: true }));

    fixture.detectChanges();
    tick();

    const emptyIcon = fixture.nativeElement.querySelector('.avail__empty-icon');
    expect(emptyIcon).toBeTruthy();
  }));

  it('should render list items for each slot', fakeAsync(() => {
    const slots = [
      mockSlot,
      {
        id: 2,
        teacherId: 10,
        date: '2026-09-21',
        startTime: '14:00:00',
        endTime: '15:00:00',
        createdAt: '2026-09-15T10:00:00Z',
        startUtc: '2026-09-21T12:00:00Z',
        endUtc: '2026-09-21T13:00:00Z',
        isBooked: false,
      },
    ];
    bookingService.getAllMySlots.and.returnValue(of({ items: slots, success: true }));

    fixture.detectChanges();
    tick();

    const listItems = fixture.nativeElement.querySelectorAll('.avail__row');
    expect(listItems.length).toBe(2);
  }));

  it('should show delete button for free slots', fakeAsync(() => {
    fixture.detectChanges();
    tick();

    const deleteButton = fixture.nativeElement.querySelector('.avail__row button');
    expect(deleteButton).toBeTruthy();
  }));

  it('should show locked icon for booked slots', fakeAsync(() => {
    const bookedSlot: AvailabilitySlot = {
      id: 1,
      teacherId: 10,
      date: '2026-09-20',
      startTime: '14:00:00',
      endTime: '15:00:00',
      createdAt: '2026-09-15T10:00:00Z',
      startUtc: '2026-09-20T12:00:00Z',
      endUtc: '2026-09-20T13:00:00Z',
      isBooked: true,
      bookingStatus: 'Approved',
    };
    bookingService.getAllMySlots.and.returnValue(of({ items: [bookedSlot], success: true }));

    fixture.detectChanges();
    tick();

    const lockedIcon = fixture.nativeElement.querySelector('.avail__locked');
    expect(lockedIcon).toBeTruthy();
  }));

  it('should call deleteSlot when delete button clicked', fakeAsync(() => {
    fixture.detectChanges();
    tick();

    const deleteButton = fixture.nativeElement.querySelector('.avail__row button') as HTMLButtonElement;
    deleteButton?.click();
    tick();

    expect(bookingService.deleteSlot).toHaveBeenCalledWith(1);
  }));

  it('should have visible delete button for free slots', fakeAsync(() => {
    fixture.detectChanges();
    tick();

    const deleteButton = fixture.nativeElement.querySelector('.avail__row button');
    expect(deleteButton).toBeTruthy();
  }));

  it('should have submit button in form', fakeAsync(() => {
    fixture.detectChanges();
    tick();

    const submitButton = fixture.nativeElement.querySelector('button[type="submit"]');
    expect(submitButton).toBeTruthy();
  }));

  it('should display booking requests button', fakeAsync(() => {
    fixture.detectChanges();
    tick();

    const button = fixture.nativeElement.querySelector('a[routerLink="/booking-requests"]');
    expect(button).toBeTruthy();
  }));

  it('should display slot information in list', fakeAsync(() => {
    fixture.detectChanges();
    tick();

    const daySpan = fixture.nativeElement.querySelector('.avail__row-day');
    expect(daySpan?.textContent).toBeTruthy();
  }));

  it('should display time range in list', fakeAsync(() => {
    fixture.detectChanges();
    tick();

    const timeRange = fixture.nativeElement.querySelector('.avail__row-range');
    expect(timeRange?.textContent).toContain('–');
  }));

  it('should display status chip', fakeAsync(() => {
    fixture.detectChanges();
    tick();

    const chip = fixture.nativeElement.querySelector('.avail__chip');
    expect(chip).toBeTruthy();
  }));

  it('should handle error on slot delete', fakeAsync(() => {
    const deleteError = new HttpErrorResponse({ status: 403, statusText: 'Forbidden' });
    bookingService.deleteSlot.and.returnValue(throwError(() => deleteError));
    bookingService.extractError.and.returnValue('Not your slot');

    fixture.detectChanges();
    tick();

    const deleteButton = fixture.nativeElement.querySelector('.avail__row button') as HTMLButtonElement;
    deleteButton?.click();
    tick();

    expect(snackBar.open).toHaveBeenCalled();
  }));

  it('should keep form and list visible even with error', fakeAsync(() => {
    const errorResponse = new HttpErrorResponse({ status: 500, statusText: 'Server Error' });
    bookingService.getAllMySlots.and.returnValue(throwError(() => errorResponse));

    fixture.detectChanges();
    tick();

    const form = fixture.nativeElement.querySelector('.avail__form');
    expect(form).toBeTruthy();
  }));
});
