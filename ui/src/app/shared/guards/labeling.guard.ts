import { inject } from '@angular/core';
import { CanActivateFn } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { deniedRedirect } from './denied-redirect';

/**
 * İşaretleme ekranı: Admin ve QuestionTrainer her kipte girer. Öğretmen yalnız kendi hızlı ekleme fotoğrafını düzeltmek
 * için girer (#1060: `?bookImport=…&kind=QuickAdd`); işin sahipliği, plan ve Claude kotası backend'de denetlenir.
 */
export const labelingGuard: CanActivateFn = (route) => {
  const auth = inject(AuthService);
  if (auth.hasRealmRole('Admin') || auth.hasRealmRole('QuestionTrainer')) {
    return true;
  }
  const params = route.queryParamMap;
  if (auth.hasRealmRole('Teacher') && params.get('kind') === 'QuickAdd' && !!params.get('bookImport')) {
    return true;
  }
  return deniedRedirect('common.routeDenied.role');
};
