import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ProgramStep } from '../models/programstep';
import { CreateProgramRequest, UserProgram } from '../models/program.interfaces';

@Injectable({
  providedIn: 'root',
})
export class ProgramService {
  private apiUrl = 'api/exam/program'; // API endpoint with exam prefix

  constructor(private http: HttpClient) {}

  getProgramSteps(): Observable<ProgramStep[]> {
    return this.http.get<ProgramStep[]>(`${this.apiUrl}/steps`);
  }

  createProgram(request: CreateProgramRequest): Observable<UserProgram> {
    return this.http.post<UserProgram>(`${this.apiUrl}/create`, request);
  }

  getMyPrograms(): Observable<UserProgram[]> {
    return this.http.get<UserProgram[]>(`${this.apiUrl}/my-programs`);
  }
}
