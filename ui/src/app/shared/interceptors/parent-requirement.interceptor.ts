import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';

import {
  PARENT_ACTIVATION_REQUIRED_ERROR_CODE,
  PARENT_REQUIRED_ERROR_CODE,
  isParentRestrictionError,
} from '../../models/parent-status.model';
import { ParentRequirementHandler } from '../../services/parent-requirement-handler.service';
import { isCrossOriginUrl } from '../utils/request-url.util';

/**
 * Issue #440 / #441: veli kuralının 403'leri (deseni `teacherNotApprovedInterceptor`).
 * - `ParentRequired`: veli durumu tazelenir; süre gerçekten dolduysa (`Expired`) "veli bilgisi ekle" ekranına gidilir. Aynı kod
 *   #439'da booking'in "velisi yok" yanıtı da olduğundan yönlendirme kararı istemci tahminine değil tazelenen duruma dayanır
 *   (booking dialog'u kendi mesajını göstermeye devam eder).
 * - `ParentActivationRequired`: durum tazelenir (kısıtlı mod arayüzü açılsın); yazma isteklerinde bilgi snackbar'ı.
 * Hata her durumda çağırana iletilir.
 */
export const parentRequirementInterceptor: HttpInterceptorFn = (req, next) => {
  const handler = inject(ParentRequirementHandler);

  return next(req).pipe(
    catchError((error: unknown) => {
      if (!isCrossOriginUrl(req.url)) {
        if (isParentRestrictionError(error, PARENT_REQUIRED_ERROR_CODE)) {
          handler.handleParentRequired();
        } else if (isParentRestrictionError(error, PARENT_ACTIVATION_REQUIRED_ERROR_CODE)) {
          handler.handleActivationRequired(req.method !== 'GET');
        }
      }
      return throwError(() => error);
    })
  );
};
