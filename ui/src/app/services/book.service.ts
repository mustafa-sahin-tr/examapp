import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

import { Book, BookTest } from '../models/book';

@Injectable({
  providedIn: 'root',
})
export class BookService {
  private apiUrl = '/api/exam/books'; // Backend API URL
  constructor(private http: HttpClient) {}

  getAll(): Observable<Book[]> {
    return this.http.get<Book[]>(`${this.apiUrl}`);
  }

  getTestsByBook(bookId: number): Observable<BookTest[]> {
    return this.http.get<BookTest[]>(`${this.apiUrl}/${bookId}/tests`);
  }
}
