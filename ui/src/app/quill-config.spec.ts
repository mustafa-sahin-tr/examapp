import { EnvironmentInjector, createEnvironmentInjector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TRANSLOCO_LOADER } from '@jsverse/transloco';
import { QUILL_CONFIG_TOKEN } from 'ngx-quill/config';
import { of } from 'rxjs';

import { appConfig } from './app.config';

// Security review L1 (#552): ngx-quill'in görünüm/editör HTML'i Angular sanitizer'ından geçer (quill 2.0.3 XSS'i, yaması yok).
describe('ngx-quill config', () => {
    it('appConfig_EnablesQuillSanitize', () => {
        vi.spyOn(console, 'warn').mockReturnValue(undefined);
        const env = createEnvironmentInjector(
            [...appConfig.providers, { provide: TRANSLOCO_LOADER, useValue: { getTranslation: () => of({}) } }],
            TestBed.inject(EnvironmentInjector),
        );
        try {
            expect(env.get(QUILL_CONFIG_TOKEN).sanitize).toBe(true);
        } finally {
            env.destroy();
        }
    });
});
