import { RenderMode, ServerRoute } from '@angular/ssr';

export const serverRoutes: ServerRoute[] = [
  // Public sayfalar — build sırasında statik HTML olarak üretilir (SEO için)
  { path: 'welcome', renderMode: RenderMode.Prerender },
  { path: 'privacy-policy', renderMode: RenderMode.Prerender },
  { path: 'terms', renderMode: RenderMode.Prerender },

  // Authenticated / dinamik sayfalar — tarayıcıda render edilir (SPA)
  { path: '**', renderMode: RenderMode.Client },
];
