import { BootstrapContext, bootstrapApplication } from '@angular/platform-browser';
import { AppComponent } from './app/app.component';
import { config } from './app/app.config.server';

// Angular 19.2.15+ (GHSA-68x2-mx4q-78m7 düzeltmesi) sunucuda her isteğe ayrı platform
// verebilmek için BootstrapContext ister; verilmezse prerender NG0401 ile düşer.
const bootstrap = (context: BootstrapContext) => bootstrapApplication(AppComponent, config, context);

export default bootstrap;
