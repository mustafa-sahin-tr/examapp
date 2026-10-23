// Issue #81: renk şemasını ilk paint'ten önce uygula (FOUC önleme). Anahtar: localStorage 'color-scheme'.
// Issue #333: index.html'de satır içi betik yerine ayrı dosya — CSP `script-src 'self'` hash'siz yeterli olsun.
(function () {
  var scheme = 'dark';
  try {
    var stored = localStorage.getItem('color-scheme');
    if (stored === 'light' || stored === 'dark') { scheme = stored; }
  } catch (e) { /* localStorage erişilemez (gizli mod vb.) → varsayılan dark */ }
  document.documentElement.classList.add(scheme + '-theme');
})();
