/**
 * Bidi gömme/geçersiz kılma/izolasyon (U+202A–U+202E, U+2066–U+2069), sıfır genişlikli ve yön işaretleri
 * (U+200B–U+200F) ile BOM (U+FEFF). `<bdi>` kullanılamayan düz metin yüzeylerinde (snackbar) metnin görsel sırasını
 * bozmasınlar diye silinir (issue #105 security D1).
 */
const INVISIBLE_CONTROL_PATTERN = /[‪-‮⁦-⁩​-‏﻿]/g;

export function stripInvisibleControls(value: string): string {
  return value.replace(INVISIBLE_CONTROL_PATTERN, '');
}
