import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthService } from '../../services/auth.service';
import { isTeacherNotApprovedError } from '../../models/teacher-approval.model';
import { isCrossOriginUrl } from '../utils/request-url.util';

/**
 * Issue #287: öğretmen özelliği gerektiren bir uç 403 `{ errorCode: "TeacherNotApproved" }` döndüğünde öğretmen
 * başvuru durumu sayfasına yönlendirir ve profili yeniler (`AuthService.handleTeacherNotApproved` — tek sefer,
 * döngüsüz). Hata yine çağırana iletilir; komponentler kendi hata durumlarını göstermeye devam eder.
 */
export const teacherNotApprovedInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);

  return next(req).pipe(
    catchError((error: unknown) => {
      if (isTeacherNotApprovedError(error) && !isCrossOriginUrl(req.url)) {
        authService.handleTeacherNotApproved();
      }
      return throwError(() => error);
    })
  );
};
