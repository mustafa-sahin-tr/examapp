import {
  STUDY_PAGE_CONTENT_TYPE_ICONS,
  STUDY_PAGE_PLATFORM_ICONS,
  StudyPageContentType,
  StudyPageLinkPlatform,
} from '../../models/study-page';

/**
 * Çalışma etkinliği (StudyItem) tipine göre görüntü kuralları — issue #141/#142.
 * Program detayı, plan detay dialog'u, etkinlik seçici ve takvim gün dialog'u aynı kuralı
 * paylaşır: link şema kontrolü, kitap satırı formatı, tip/platform ikonu ve etiketi.
 * Angular DI'sı yoktur; çeviri `translate` fonksiyonuyla dışarıdan verilir.
 */

/** `TranslocoService.translate` ile uyumlu çeviri fonksiyonu. */
export type TranslateFn = (key: string, params?: Record<string, unknown>) => string;

/** Hem `UserProgramStudyPageSchedule`, `CalendarEvent` hem `StudyPage` bu şekle uyar. */
export interface StudyItemDisplaySource {
  contentType?: StudyPageContentType | null;
  url?: string | null;
  platform?: StudyPageLinkPlatform | null;
  bookName?: string | null;
  bookTestName?: string | null;
  startPage?: number | null;
  endPage?: number | null;
}

export interface StudyItemDisplay {
  kind: 'image' | 'link' | 'book';
  typeIcon: string;
  typeLabel: string;
  /** Yalnızca link tipinde. */
  platformIcon: string | null;
  platformLabel: string | null;
  /** Yalnızca http/https şemalı geçerli adres; aksi halde null (link olarak render edilmez). */
  href: string | null;
  /** `href` varsa sitenin host'u (örn. "youtube.com"). */
  host: string | null;
  /** Yalnızca kitap tipinde ve okunabilir bir alan varsa: "Kitap — Test, sayfa X-Y". */
  bookLine: string | null;
}

const TYPE_LABEL_KEYS: Record<StudyPageContentType, string> = {
  [StudyPageContentType.Image]: 'shared.studyItem.type.image',
  [StudyPageContentType.Link]: 'shared.studyItem.type.link',
  [StudyPageContentType.BookPageRange]: 'shared.studyItem.type.bookPageRange',
};

const PLATFORM_LABEL_KEYS: Record<StudyPageLinkPlatform, string> = {
  [StudyPageLinkPlatform.Other]: 'shared.studyItem.platform.other',
  [StudyPageLinkPlatform.Eba]: 'shared.studyItem.platform.eba',
  [StudyPageLinkPlatform.YouTube]: 'shared.studyItem.platform.youtube',
};

/**
 * Adres yalnızca `http:` / `https:` şemasıysa normalize edilmiş halini döner; boş, çözümlenemeyen
 * veya `javascript:` / `data:` gibi şemalarda null. Dış bağlantı render eden her yer bunu kullanır.
 */
export function safeExternalUrl(url: string | null | undefined): string | null {
  const trimmed = url?.trim();
  if (!trimmed) {
    return null;
  }
  try {
    const parsed = new URL(trimmed);
    return parsed.protocol === 'http:' || parsed.protocol === 'https:' ? parsed.href : null;
  } catch {
    return null;
  }
}

/** Güvenli adresin host'u ("www." atılır); güvenli değilse null. */
export function externalUrlHost(url: string | null | undefined): string | null {
  const safe = safeExternalUrl(url);
  return safe ? new URL(safe).hostname.replace(/^www\./, '') : null;
}

/**
 * Kitap etkinliği satırı:
 * - "Kitap — Test, sayfa X-Y"; test yoksa "Kitap, sayfa X-Y"
 * - StartPage == EndPage ise "sayfa X"
 * - yalnızca tek sayfa alanı doluysa "sayfa X"; sayfa yoksa yalnızca ad kısmı
 * - hiçbir alan yoksa null
 */
export function formatBookPageRange(source: StudyItemDisplaySource, translate: TranslateFn): string | null {
  const book = source.bookName?.trim() || null;
  const test = source.bookTestName?.trim() || null;
  const name = book && test ? `${book} — ${test}` : (book ?? test);

  const start = source.startPage ?? null;
  const end = source.endPage ?? null;
  let pages: string | null = null;
  if (start !== null && end !== null && start !== end) {
    pages = translate('shared.studyItem.book.pageRange', { start, end });
  } else if (start !== null || end !== null) {
    pages = translate('shared.studyItem.book.pageSingle', { page: start ?? end });
  }

  const parts = [name, pages].filter((p): p is string => !!p);
  return parts.length > 0 ? parts.join(', ') : null;
}

/** Tip ikonu; bilinmeyen/eksik `contentType` Image gibi davranır (eski kayıtlarla uyum). */
export function studyItemTypeIcon(contentType: StudyPageContentType | null | undefined): string {
  return STUDY_PAGE_CONTENT_TYPE_ICONS[contentType ?? StudyPageContentType.Image] ?? STUDY_PAGE_CONTENT_TYPE_ICONS[0];
}

export function describeStudyItem(source: StudyItemDisplaySource, translate: TranslateFn): StudyItemDisplay {
  const contentType = source.contentType ?? StudyPageContentType.Image;
  const typeIcon = studyItemTypeIcon(contentType);
  const typeLabel = translate(TYPE_LABEL_KEYS[contentType] ?? TYPE_LABEL_KEYS[StudyPageContentType.Image]);

  if (contentType === StudyPageContentType.Link) {
    const platform = source.platform ?? StudyPageLinkPlatform.Other;
    return {
      kind: 'link',
      typeIcon,
      typeLabel,
      platformIcon: STUDY_PAGE_PLATFORM_ICONS[platform] ?? STUDY_PAGE_PLATFORM_ICONS[StudyPageLinkPlatform.Other],
      platformLabel: translate(PLATFORM_LABEL_KEYS[platform] ?? PLATFORM_LABEL_KEYS[StudyPageLinkPlatform.Other]),
      href: safeExternalUrl(source.url),
      host: externalUrlHost(source.url),
      bookLine: null,
    };
  }

  if (contentType === StudyPageContentType.BookPageRange) {
    return {
      kind: 'book',
      typeIcon,
      typeLabel,
      platformIcon: null,
      platformLabel: null,
      href: null,
      host: null,
      bookLine: formatBookPageRange(source, translate),
    };
  }

  return {
    kind: 'image',
    typeIcon,
    typeLabel,
    platformIcon: null,
    platformLabel: null,
    href: null,
    host: null,
    bookLine: null,
  };
}
