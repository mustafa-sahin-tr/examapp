import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { CreateProgramRequest, ProgramStudyPageScheduleRequest, ProgramSubject, UserProgram } from '../models/program.interfaces';

@Injectable({
  providedIn: 'root',
})
export class ProgramService {
  private apiUrl = '/api/exam/program'; // gateway üzerinden, göreli (aynı origin)

  constructor(private http: HttpClient) {}

  /** #885: takvime girecek dersler (öğrencinin sınıfı); sihirbazın ders adımı ve önizlemesi. */
  getProgramSubjects(): Observable<ProgramSubject[]> {
    return this.http.get<ProgramSubject[]>(`${this.apiUrl}/subjects`);
  }

  createProgram(request: CreateProgramRequest): Observable<UserProgram> {
    return this.http.post<UserProgram>(`${this.apiUrl}/create`, request);
  }

  getMyPrograms(): Observable<UserProgram[]> {
    return this.http.get<UserProgram[]>(`${this.apiUrl}/my-programs`);
  }

  getProgramById(programId: number): Observable<UserProgram> {
    return this.http.get<UserProgram>(`${this.apiUrl}/${programId}`);
  }

  addStudyPages(programId: number, request: ProgramStudyPageScheduleRequest): Observable<UserProgram> {
    return this.http.post<UserProgram>(`${this.apiUrl}/${programId}/study-pages`, request);
  }

  deleteProgram(programId: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${programId}`);
  }
}
