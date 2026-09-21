import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';

import { LocaleService } from '../../services/locale.service';

const ACCEPT_LANGUAGE = 'Accept-Language';

/**
 * Aktif dili her API isteğine `Accept-Language` olarak ekler (issue #181). Backend, oturum
 * açmamış kullanıcılar için istek kültürünü bu header'dan çözer.
 *
 * Yalnızca `/api/` altındaki isteklere dokunulur; statik varlıklar (`/i18n/*.json`, resimler)
 * etkilenmez. İstek header'ı elle set edilmişse üzerine yazılmaz.
 */
export const localeInterceptor: HttpInterceptorFn = (req, next) => {
  if (!isApiRequest(req.url) || req.headers.has(ACCEPT_LANGUAGE)) {
    return next(req);
  }

  const locale = inject(LocaleService).locale();

  return next(req.clone({ setHeaders: { [ACCEPT_LANGUAGE]: locale } }));
};

function isApiRequest(url: string): boolean {
  if (url.startsWith('/api/')) {
    return true;
  }

  try {
    return new URL(url, 'http://localhost').pathname.startsWith('/api/');
  } catch {
    return false;
  }
}
