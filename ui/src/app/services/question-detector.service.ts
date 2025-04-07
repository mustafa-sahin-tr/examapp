import { HttpClient } from "@angular/common/http";
import { Injectable } from "@angular/core";
import { Observable } from "rxjs";
import { PredictionList } from "../models/prediction";

@Injectable({
  providedIn: 'root'
})
export class QuestionDetectorService {
      constructor(private http: HttpClient) {}

    predict(bulkDto: any) : Observable<PredictionList> {
        return this.http.post<PredictionList>(`http://localhost/question-detector-dev/predict`, bulkDto);
    }

    readQrData(imageData: any) : Observable<PredictionList> {
      return this.http.post<PredictionList>(`http://localhost/question-detector-dev/read-qr`, imageData);
    }

    sendtoFix(fixData: any) : Observable<any> {
        return this.http.post<any>(`http://localhost/question-detector-dev/send-to-fix`, fixData);
    }

    sendtoFixForAnswer(fixData: any) : Observable<any> {
      return this.http.post<any>(`http://localhost/question-detector-dev/send-to-fix-for-answers`, fixData);
  }
 // http://localhost/minio-api/exam-questions/questions/04a3609b-8a98-4849-949d-32c14a6d72c1.jpg   
}