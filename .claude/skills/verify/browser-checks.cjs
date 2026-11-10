// Verify oturumunda sayfa boyunca console / JS / API hatalarını toplar. Kullanım (betik scratchpad'de):
//   const { chromium } = require('<repo>/ui/node_modules/playwright');
//   const { watchPage } = require('<repo>/.claude/skills/verify/browser-checks.cjs');
//   const checks = watchPage(page, { allow: [/\/api\/exam\/notifications\/unread/] });
//   ... akış ...
//   checks.print(); if (!checks.ok()) process.exitCode = 1;
// `allow`: beklenen hatalar (ör. bilerek tetiklenen 403, kasıtlı geçersiz istek). URL ya da console metniyle eşleşir.

const API_PATH = /\/api\//;

// İlk satır, en fazla 200 karakter: Angular uyarıları DOM düğümünü de basar.
const short = (text) => text.split('\n')[0].slice(0, 200);

function watchPage(page, { allow = [] } = {}) {
  const console = [];
  const pageErrors = [];
  const apiErrors = [];
  const failed = [];
  const allowed = (text) => allow.some((re) => re.test(text));

  page.on('console', (msg) => {
    const type = msg.type();
    if (type !== 'error' && type !== 'warning') return;
    const text = msg.text();
    if (!allowed(text)) console.push(`${type}: ${short(text)}`);
  });
  page.on('pageerror', (err) => {
    if (!allowed(err.message)) pageErrors.push(short(err.message));
  });
  page.on('response', (res) => {
    const url = res.url();
    if (res.status() >= 400 && API_PATH.test(url) && !allowed(url)) {
      apiErrors.push(`${res.status()} ${res.request().method()} ${url}`);
    }
  });
  page.on('requestfailed', (req) => {
    const url = req.url();
    const reason = req.failure()?.errorText ?? 'failed';
    // Gezinme / switchMap iptalleri hata değildir.
    if (reason.includes('ERR_ABORTED') || allowed(url)) return;
    failed.push(`${reason} ${req.method()} ${url}`);
  });

  return {
    console,
    pageErrors,
    apiErrors,
    failed,
    ok: () => !console.length && !pageErrors.length && !apiErrors.length && !failed.length,
    print() {
      // Aynı mesaj bir kez, tekrar sayısıyla.
      const line = (name, list) => {
        process.stdout.write(`${name}: ${list.length}\n`);
        const counts = new Map();
        for (const item of list) counts.set(item, (counts.get(item) ?? 0) + 1);
        for (const [item, n] of counts) process.stdout.write(`  - ${n > 1 ? `${n}× ` : ''}${item}\n`);
      };
      line('console error/warning', console);
      line('page error', pageErrors);
      line('API >=400', apiErrors);
      line('request failed', failed);
    },
  };
}

module.exports = { watchPage };
