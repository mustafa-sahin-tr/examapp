/**
 * Issue #1183: çoktan seçmeli şıkların harfleri. Şıkkın harfi dizideki sırasıdır (0 → A); tespit (Claude) ya da kayıt harf
 * verdiyse ve harfler tam bir takımsa (A'dan başlayıp boşluksuz, tekil) şıklar o harflere dizilir.
 */
const LETTERS = 'ABCDE';

/** "A)", "b.", " (C) " → "A", "B", "C"; A–E dışında / boşsa null. */
export function normalizeOptionLetter(raw: string | null | undefined): string | null {
  const letter = (raw ?? '').trim().replace(/^\(/, '').charAt(0).toUpperCase();
  return letter && LETTERS.includes(letter) ? letter : null;
}

/** Sıradaki harf: 0 → "A". */
export function optionLetterAt(index: number): string {
  return String.fromCharCode(65 + index);
}

/**
 * Harfler tam takımsa öğeleri harf sırasına dizer, değilse null (konumdan harflendirilir). `letterOf` her öğenin ham harfi.
 */
export function orderByLetters<T>(items: readonly T[], letterOf: (item: T) => string | null | undefined): T[] | null {
  if (items.length === 0 || items.length > LETTERS.length) return null;
  const letters = items.map((item) => normalizeOptionLetter(letterOf(item)));
  const sorted = [...letters].sort();
  if (sorted.some((letter, index) => letter !== LETTERS[index])) return null;
  return items
    .map((item, index) => ({ item, letter: letters[index]! }))
    .sort((a, b) => a.letter.localeCompare(b.letter))
    .map((pair) => pair.item);
}

/** Şıkkı `from` sırasından `to` sırasına taşır; diğerleri kayar. */
export function moveOption<T>(items: readonly T[], from: number, to: number): T[] {
  const next = [...items];
  const [moved] = next.splice(from, 1);
  next.splice(to, 0, moved);
  return next;
}
