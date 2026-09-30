// Issue #98: `ng build` sonrası Excalidraw'ın self-host asset'lerini doğrular (CDN fallback'ine düşülmesin).
// Kullanım: `npm run verify:whiteboard-assets` (önce `ng build`). Hata varsa çıkış kodu 1.
//
// Kontroller:
//  1. Excalidraw kodunun başvurduğu her font (`./fonts/<Aile>/<dosya>.woff2`) dist'te var — Xiaolai (CJK) hariç:
//     bilinçli olarak kopyalanmaz (angular.json assets ignore); CJK metinde Excalidraw esm.sh fallback'ine düşer.
//  2. Xiaolai dist'e kopyalanmamış.
//  3. Excalidraw CSS'i ayrı bundle olarak var (`excalidraw.css`) ve index.html'e enjekte EDİLMEMİŞ (tembel yüklenir).
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

const root = new URL('..', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1');
const pkgDist = join(root, 'node_modules/@excalidraw/excalidraw/dist/prod');
const browser = join(root, 'dist/exam-app/browser');
const fontsOut = join(browser, 'excalidraw-assets/fonts');
const EXCLUDED_FAMILIES = new Set(['Xiaolai']);

const errors = [];

if (!existsSync(browser)) {
  console.error(`dist bulunamadı: ${browser} — önce "ng build" çalıştırın.`);
  process.exit(1);
}

const referenced = new Set();
for (const file of readdirSync(pkgDist).filter((f) => f.endsWith('.js'))) {
  for (const match of readFileSync(join(pkgDist, file), 'utf8').matchAll(/\.\/fonts\/([A-Za-z]+)\/([\w.-]+\.woff2)/g)) {
    referenced.add(`${match[1]}/${match[2]}`);
  }
}
if (referenced.size === 0) {
  errors.push('Excalidraw kodunda font başvurusu bulunamadı (paket yapısı değişmiş olabilir).');
}

let checked = 0;
for (const ref of referenced) {
  const family = ref.split('/')[0];
  if (EXCLUDED_FAMILIES.has(family)) {
    continue;
  }
  checked++;
  if (!existsSync(join(fontsOut, ref))) {
    errors.push(`eksik font: excalidraw-assets/fonts/${ref}`);
  }
}

for (const family of EXCLUDED_FAMILIES) {
  if (existsSync(join(fontsOut, family))) {
    errors.push(`hariç tutulması gereken font ailesi kopyalanmış: ${family}`);
  }
}

const css = readdirSync(browser).find((f) => /^excalidraw(-[A-Z0-9]+)?\.css$/.test(f));
if (!css) {
  errors.push('excalidraw.css bundle bulunamadı (angular.json styles bundleName: "excalidraw").');
} else if (css !== 'excalidraw.css') {
  errors.push(`excalidraw CSS adı hash'li (${css}); loader sabit "excalidraw.css" bekler.`);
}
for (const index of ['index.html', 'index.csr.html']) {
  const path = join(browser, index);
  if (existsSync(path) && readFileSync(path, 'utf8').includes('excalidraw')) {
    errors.push(`${index} excalidraw'a başvuruyor (inject: false olmalı).`);
  }
}

if (errors.length > 0) {
  console.error(`Excalidraw asset doğrulaması BAŞARISIZ:\n - ${errors.join('\n - ')}`);
  process.exit(1);
}
console.log(`Excalidraw asset doğrulaması tamam: ${checked} font, excalidraw.css (enjekte edilmemiş), Xiaolai hariç.`);
