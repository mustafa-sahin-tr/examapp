/**
 * Program adım seçeneğinin `icon` alanı Material Symbols adını taşır (issue #319), ör. `timer`, `looks_one`.
 * Geçiş döneminde eski `.svg` yolu ya da bozuk bir değer gelebilir; bunlar `<img>`'e değil nötr bir
 * yedek ikona düşer. Yalnız `[a-z0-9_]+` geçerli sayılır — ad doğrudan ligatür metni olarak yazılır.
 */
export const PROGRAM_OPTION_FALLBACK_ICON = 'radio_button_unchecked';

const MATERIAL_SYMBOL_NAME = /^[a-z0-9_]+$/;

export function resolveProgramOptionIcon(icon: string | null | undefined): string {
  const name = icon?.trim() ?? '';
  return MATERIAL_SYMBOL_NAME.test(name) ? name : PROGRAM_OPTION_FALLBACK_ICON;
}
