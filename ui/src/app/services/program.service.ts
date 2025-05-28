import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ProgramStep } from '../models/programstep';

@Injectable({
  providedIn: 'root',
})
export class ProgramService {
  private apiUrl = 'api/exam/program'; // Replace with your actual API endpoint

  constructor(private http: HttpClient) {}

  getProgramSteps(): Observable<ProgramStep[]> {
    return this.http.get<ProgramStep[]>(`${this.apiUrl}/steps`);
  }
}
