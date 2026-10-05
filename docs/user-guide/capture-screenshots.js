// Kullanıcı kılavuzu ekran görüntüsü script'i (issue #353).
//
// Kullanım (Playwright kurulu bir klasörden ya da NODE_PATH ile):
//   npm i playwright && npx playwright install chromium
//   EXAMAPP_TEST_PASSWORD=... node docs/user-guide/capture-screenshots.js [filtre]
//
// Şifre YALNIZCA ortam değişkeninden okunur; repoya yazılmaz.
// İsteğe bağlı ortam değişkenleri:
//   EXAMAPP_BASE_URL   (varsayılan http://localhost:5678; gateway, UI ve /app giriş ekranı aynı adreste)
//   EXAMAPP_STUDENT, EXAMAPP_TEACHER, EXAMAPP_TEACHER2, EXAMAPP_TUTOR, EXAMAPP_ADMIN, EXAMAPP_NOROLE  (hesap adları)
// [filtre] verilirse yalnızca adı bu metni içeren ekranlar çekilir (örn. "ogrenci-").
// Çıktı: docs/user-guide/img/<rol>-<ekran>.png (1440x900, açık tema, tr-TR).

const path = require('path');
const { chromium } = require('playwright');

const BASE = process.env.EXAMAPP_BASE_URL || 'http://localhost:5678';
const PASSWORD = process.env.EXAMAPP_TEST_PASSWORD;
const OUT = path.join(__dirname, 'img');
const FILTER = process.argv[2] || '';
const USERS = {
  student: process.env.EXAMAPP_STUDENT || 'student10@hedefokul.com',
  teacher: process.env.EXAMAPP_TEACHER || 'teacher10@hedefokul.com',
  teacher2: process.env.EXAMAPP_TEACHER2 || 't2@hedefokul.com',
  tutor: process.env.EXAMAPP_TUTOR || 'oo1@hedefokul.com',
  admin: process.env.EXAMAPP_ADMIN || 'admin',
  norole: process.env.EXAMAPP_NOROLE || 's55@hedefokul.com',
};

if (!PASSWORD) {
  console.error('EXAMAPP_TEST_PASSWORD ortam değişkeni gerekli.');
  process.exit(1);
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// Yardımcılar: metne göre tıkla (bulunamazsa sessizce geç).
async function clickText(p, text, opts = {}) {
  const loc = p.getByText(text, { exact: opts.exact ?? false }).nth(opts.nth ?? 0);
  await loc.click({ timeout: opts.timeout ?? 8000 });
}
async function clickRole(p, role, name, nth = 0) {
  await p.getByRole(role, { name }).nth(nth).click({ timeout: 8000 });
}

// Ekran listesi. user: null = oturumsuz. act: sayfa açıldıktan sonra yapılacak işlem.
const SHOTS = [
  // --- Ortak ---
  { id: 'ortak-karsilama', user: null, url: '/welcome' },
  { id: 'ortak-giris', user: null, url: '/app/login', wait: 4000 },
  { id: 'ortak-giris-hata', user: null, url: '/app/login', act: async (p) => { await p.fill('#username', 'kilavuz-ornek@ornek.com'); await p.fill('#password', 'yanlis-sifre'); await p.click('#kc-login, button[type=submit]'); } },
  { id: 'ortak-sifremi-unuttum', user: null, url: '/app/login', act: (p) => clickText(p, 'Parolanızı mı unuttunuz?') },
  { id: 'ortak-kayit', user: null, url: '/app/login', act: (p) => clickText(p, 'Kayıt ol', { exact: true }) },
  { id: 'ortak-kayit-ogrenci', user: 'norole', url: '/register?role=student' },
  { id: 'ortak-kayit-ogretmen', user: 'norole', url: '/register?role=teacher' },
  { id: 'ortak-kayit-veli', user: 'norole', url: '/register?role=parent' },
  { id: 'ortak-profil-menusu', user: 'student', url: '/dashboard', act: (p) => p.locator('.profile-menu').first().click({ timeout: 8000 }) },
  { id: 'ortak-bildirimler', user: 'teacher', url: '/notifications' },
  { id: 'ortak-dil-secimi', user: 'student', url: '/dashboard', act: (p) => p.locator('.toolbar-language-switcher button').first().click({ timeout: 8000 }) },
  { id: 'ortak-koyu-tema', user: 'student', url: '/dashboard', dark: true },

  // --- Öğrenci ---
  { id: 'ogrenci-dashboard', user: 'student', url: '/dashboard', wait: 9000 },
  { id: 'ogrenci-sinavlar-kesfet', user: 'student', url: '/tests' },
  { id: 'ogrenci-sinavlar-bana-atananlar', user: 'student', url: '/tests', act: (p) => clickText(p, 'Bana Atananlar') },
  { id: 'ogrenci-sinavlar-devam-edenler', user: 'student', url: '/tests', act: (p) => clickText(p, 'Devam Edenler') },
  { id: 'ogrenci-sinavlar-tamamlananlar', user: 'student', url: '/tests', act: (p) => clickText(p, 'Tamamlananlar', { nth: 0 }) },
  { id: 'ogrenci-test-detay', user: 'student', url: '/test/1' },
  { id: 'ogrenci-test-cozme', user: 'student', url: '/testsolve/7', wait: 9000 },
  { id: 'ogrenci-test-sonuc', user: 'student', url: '/test/7', wait: 9000 },
  { id: 'ogrenci-pratik', user: 'student', url: '/practice' },
  { id: 'ogrenci-pratik-soru', user: 'student', url: '/practice', act: (p) => clickText(p, 'Başla', { exact: true }), wait: 9000 },
  { id: 'ogrenci-ders-calisma', user: 'student', url: '/study' },
  { id: 'ogrenci-ders-calisma-konular', user: 'student', url: '/study', act: (p) => clickText(p, 'Matematik', { exact: true }) },
  { id: 'ogrenci-programlar', user: 'student', url: '/programs' },
  { id: 'ogrenci-program-detay', user: 'student', url: '/programs/1/detail' },
  { id: 'ogrenci-program-olustur', user: 'student', url: '/program-create' },
  { id: 'ogrenci-planim', user: 'student', url: '/my-calendar' },
  { id: 'ogrenci-ogretmen-ara', user: 'student', url: '/tutors' },
  { id: 'ogrenci-ogretmen-profili', user: 'student', url: '/tutors/9' },
  { id: 'ogrenci-randevu-al', user: 'student', url: '/tutors/9', act: (p) => clickText(p, 'Randevu Al') },
  { id: 'ogrenci-randevularim', user: 'student', url: '/my-bookings' },
  { id: 'ogrenci-video-ders', user: 'student', url: '/lessons/1/video', wait: 10000 },
  { id: 'ogrenci-raporlar-rozetler', user: 'student', url: '/certificates', wait: 9000 },
  { id: 'ogrenci-ayarlar-neler-var', user: 'student', url: '/student-profile' },
  { id: 'ogrenci-ayarlar-bilgilerim', user: 'student', url: '/student-profile', act: (p) => clickRole(p, 'tab', 'Bilgilerim') },
  { id: 'ogrenci-ayarlar-rozetler-liderlik', user: 'student', url: '/student-profile', act: (p) => clickRole(p, 'tab', 'Rozetler') },
  { id: 'ogrenci-ayarlar-odevler', user: 'student', url: '/student-profile', act: (p) => clickRole(p, 'tab', 'Ödevler ve Sınavlar') },
  { id: 'ogrenci-ayarlar-haftalik-takvim', user: 'student', url: '/student-profile', act: (p) => clickRole(p, 'tab', 'Haftalık Takvim') },
  { id: 'ogrenci-ayarlar-tema', user: 'student', url: '/student-profile', act: (p) => clickRole(p, 'tab', 'Tema Ayarları') },

  // --- Öğretmen (okula bağlı) ---
  { id: 'ogretmen-dashboard', user: 'teacher', url: '/dashboard', wait: 9000 },
  { id: 'ogretmen-sinavlar', user: 'teacher', url: '/tests' },
  { id: 'ogretmen-test-ata', user: 'teacher', url: '/test/8', act: (p) => clickText(p, 'Öğrenci Seç'), wait: 6000 },
  { id: 'ogretmen-test-detay', user: 'teacher', url: '/test/8', wait: 9000 },
  { id: 'ogretmen-test-olustur', user: 'teacher', url: '/exam' },
  { id: 'ogretmen-test-duzenle', user: 'teacher', url: '/exam/8', wait: 9000 },
  { id: 'ogretmen-soru-ekle', user: 'teacher', url: '/questioncanvas' },
  { id: 'ogretmen-soru-transferi', user: 'teacher', url: '/question-transfer' },
  { id: 'ogretmen-calisma-sayfalari', user: 'teacher', url: '/study-pages' },
  { id: 'ogretmen-calisma-sayfasi-yeni', user: 'teacher', url: '/study-pages/new' },
  { id: 'ogretmen-calisma-linkleri', user: 'teacher', url: '/study-links' },
  { id: 'ogretmen-planim', user: 'teacher', url: '/my-calendar' },
  { id: 'ogretmen-musait-saatler', user: 'teacher', url: '/availability' },
  { id: 'ogretmen-randevu-talepleri', user: 'teacher', url: '/booking-requests' },
  { id: 'ogretmen-atama-izin-talepleri', user: 'teacher2', url: '/assignment-permission-requests', act: (p) => clickText(p, 'Tümü', { exact: true }) },
  { id: 'ogretmen-ozel-ders-profili-okullu', user: 'teacher', url: '/tutor-profile' },
  { id: 'ogretmen-raporlar', user: 'teacher', url: '/certificates', wait: 9000 },

  // --- Bağımsız (özel ders) öğretmen ---
  { id: 'bagimsiz-ozel-ders-profili', user: 'tutor', url: '/tutor-profile' },
  { id: 'bagimsiz-musait-saatler', user: 'tutor', url: '/availability' },
  { id: 'bagimsiz-randevu-talepleri', user: 'tutor', url: '/booking-requests', act: (p) => clickText(p, 'Tümü', { exact: true }) },
  { id: 'bagimsiz-test-yorumlari', user: 'tutor', url: '/test/12', wait: 9000, fullPage: true },

  // --- Admin ---
  { id: 'admin-dashboard', user: 'admin', url: '/admin/dashboard', wait: 9000 },
  { id: 'admin-yonetim-taksonomi', user: 'admin', url: '/admin', act: (p) => clickText(p, '5. Sınıf', { exact: true }) },
  { id: 'admin-yonetim-siniflandirma-cache', user: 'admin', url: '/admin', act: (p) => clickText(p, 'Sınıflandırma Cache') },
  { id: 'admin-ogretmen-basvurulari', user: 'admin', url: '/admin/teacher-approvals', act: (p) => clickText(p, 'Tümü', { exact: true }) },
  { id: 'admin-ogretmenler', user: 'admin', url: '/admin/teachers' },
  { id: 'admin-sifre-sifirlama', user: 'admin', url: '/admin/teachers', act: (p) => p.locator('button[aria-label*="şifreyi sıfırla"]').nth(10).click({ timeout: 8000 }) },
  { id: 'admin-ogretmen-askiya-alma', user: 'admin', url: '/admin/teachers', act: (p) => p.locator('button[aria-label*="onayını askıya al"]').nth(10).click({ timeout: 8000 }) },
  { id: 'admin-ogrenciler', user: 'admin', url: '/admin/students' },
  { id: 'admin-ogrenci-okul-degistir', user: 'admin', url: '/admin/students', act: (p) => p.locator('button[aria-label*="okulunu değiştir"]').nth(1).click({ timeout: 8000 }) },
  { id: 'admin-okullar', user: 'admin', url: '/admin/schools' },
  { id: 'admin-rozetler', user: 'admin', url: '/admin/badge-definitions' },
  { id: 'admin-rozet-yeni', user: 'admin', url: '/admin/badge-definitions', act: (p) => clickText(p, 'Yeni rozet') },
  { id: 'admin-yorum-sikayetleri', user: 'admin', url: '/admin/comment-reports' },
];

async function newContext(browser, dark) {
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'tr-TR', colorScheme: dark ? 'dark' : 'light' });
  await ctx.addInitScript((scheme) => {
    try { localStorage.setItem('color-scheme', scheme); } catch (e) { /* yok say */ }
  }, dark ? 'dark' : 'light');
  return ctx;
}

async function gotoRetry(p, url, tries = 3) {
  for (let i = 0; i < tries; i++) {
    try { await p.goto(url, { waitUntil: 'domcontentloaded', timeout: 60000 }); return; } catch (e) { if (i === tries - 1) throw e; await sleep(3000); }
  }
}

async function login(browser, key, dark) {
  const ctx = await newContext(browser, dark);
  const p = await ctx.newPage();
  await gotoRetry(p, BASE + '/app/login');
  await p.waitForSelector('#username', { timeout: 60000 });
  await p.fill('#username', USERS[key]);
  await p.fill('#password', PASSWORD);
  await p.click('#kc-login, button[type=submit], input[type=submit]');
  await p.waitForURL((u) => !/\/realms\/|callback|\/app\//.test(u.toString()), { timeout: 60000 }).catch(() => {});
  await sleep(3000);
  return { ctx, p };
}

(async () => {
  const browser = await chromium.launch();
  const sessions = {};
  const results = [];
  for (const s of SHOTS.filter((x) => x.id.includes(FILTER))) {
    let ok = false, note = '';
    for (let attempt = 1; attempt <= 2 && !ok; attempt++) {
      try {
        let p, ctx;
        const key = s.user ? s.user + (s.dark ? '-dark' : '') : null;
        if (key) {
          if (!sessions[key]) sessions[key] = await login(browser, s.user, s.dark);
          ({ p } = sessions[key]);
        } else {
          ctx = await newContext(browser, false);
          p = await ctx.newPage();
        }
        await gotoRetry(p, BASE + s.url);
        await sleep(s.act ? 6000 : (s.wait ?? 7000));
        if (s.act) {
          try { await s.act(p); } catch (e) { note = 'işlem yapılamadı: ' + e.message.split('\n')[0]; }
          await sleep(s.wait ?? 4000);
        }
        await p.screenshot({ path: path.join(OUT, s.id + '.png'), fullPage: !!s.fullPage });
        await p.keyboard.press('Escape').catch(() => {});
        if (ctx) await ctx.close();
        ok = true;
        results.push(`${s.id}\tOK\t${p.url().replace(BASE, '')}${note ? '\t' + note : ''}`);
      } catch (e) {
        note = e.message.split('\n')[0];
        if (attempt === 2) results.push(`${s.id}\tHATA\t${note}`);
        await sleep(3000);
      }
    }
    console.log(results[results.length - 1]);
  }
  await browser.close();
})();
