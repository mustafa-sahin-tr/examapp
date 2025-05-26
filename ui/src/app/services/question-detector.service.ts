import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { catchError, Observable, of } from 'rxjs';
import { PredictionList } from '../models/prediction';

@Injectable({
  providedIn: 'root',
})
export class QuestionDetectorService {
  constructor(private http: HttpClient) {}

  predict(bulkDto: any): Observable<PredictionList> {
    return this.http.post<PredictionList>(`/question-detector-dev/predict`, bulkDto);
  }

  getContents(imageData: any): Observable<PredictionList> {
    return this.http.post<PredictionList>(`/question-detector-dev/headerlist`, imageData);
  }

  readQrData(imageData: any): Observable<PredictionList> {
    return this.http.post<PredictionList>(`/question-detector-dev/read-qr`, imageData);
  }

  sendtoFix(fixData: any): Observable<any> {
    // return this.http.post<any>(`http://localhost/question-detector-dev/send-to-fix`, fixData);

    return this.http.post<any>(`/question-detector-dev/send-to-fix`, fixData).pipe(
      catchError((ex) => {
        // ❗️ Tüm 200 dışı yanıtlar buraya düşer
        // console.error('Request failed:', error.status, error.message);

        // İstersen kullanıcıya özel bir bilgi döndürebilirsin:
        return of({ success: false, status: ex.status, message: ex.error.detail });
      })
    );
  }

  sendtoFixForAnswer(fixData: any): Observable<any> {
    return this.http.post<any>(`/question-detector-dev/send-to-fix-for-answers`, fixData);
  }
}
