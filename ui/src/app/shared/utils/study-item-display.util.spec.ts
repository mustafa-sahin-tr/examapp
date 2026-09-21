import {
  describeStudyItem,
  formatBookPageRange,
  safeExternalUrl,
  externalUrlHost,
  StudyItemDisplay,
  StudyItemDisplaySource,
} from './study-item-display.util';
import { StudyPageContentType, StudyPageLinkPlatform } from '../../models/study-page';

describe('study-item-display.util', () => {
  const translate = (key: string, params?: Record<string, unknown>): string => {
    const translations: Record<string, string> = {
      'shared.studyItem.type.image': 'Image',
      'shared.studyItem.type.link': 'Link',
      'shared.studyItem.type.bookPageRange': 'Book Pages',
      'shared.studyItem.platform.other': 'Other',
      'shared.studyItem.platform.eba': 'EBA',
      'shared.studyItem.platform.youtube': 'YouTube',
      'shared.studyItem.book.pageRange': `pages ${params?.['start']}-${params?.['end']}`,
      'shared.studyItem.book.pageSingle': `page ${params?.['page']}`,
    };
    return translations[key] ?? key;
  };

  describe('safeExternalUrl', () => {
    // AC #1: link tipi etkinliğe tıklandığında yeni sekmede açılıyor
    // JavaScript ve data: url'ler link olarak render edilmeyecek

    it('safeExternalUrl_ValidHttpsUrl_ReturnsParsedUrl', () => {
      const result = safeExternalUrl('https://www.youtube.com/watch?v=test');
      expect(result).toBe('https://www.youtube.com/watch?v=test');
    });

    it('safeExternalUrl_ValidHttpUrl_ReturnsParsedUrl', () => {
      const result = safeExternalUrl('http://example.com/path');
      expect(result).toBe('http://example.com/path');
    });

    it('safeExternalUrl_JavaScriptUrl_ReturnsNull', () => {
      const result = safeExternalUrl('javascript:alert("xss")');
      expect(result).toBeNull();
    });

    it('safeExternalUrl_DataUrl_ReturnsNull', () => {
      const result = safeExternalUrl('data:text/html,<script>alert("xss")</script>');
      expect(result).toBeNull();
    });

    it('safeExternalUrl_EmptyString_ReturnsNull', () => {
      const result = safeExternalUrl('');
      expect(result).toBeNull();
    });

    it('safeExternalUrl_Null_ReturnsNull', () => {
      const result = safeExternalUrl(null);
      expect(result).toBeNull();
    });

    it('safeExternalUrl_Undefined_ReturnsNull', () => {
      const result = safeExternalUrl(undefined);
      expect(result).toBeNull();
    });

    it('safeExternalUrl_WhitespaceOnly_ReturnsNull', () => {
      const result = safeExternalUrl('   ');
      expect(result).toBeNull();
    });

    it('safeExternalUrl_MalformedUrl_ReturnsNull', () => {
      const result = safeExternalUrl('not a url at all');
      expect(result).toBeNull();
    });

    it('safeExternalUrl_FtpScheme_ReturnsNull', () => {
      const result = safeExternalUrl('ftp://example.com');
      expect(result).toBeNull();
    });
  });

  describe('externalUrlHost', () => {
    // AC #1: platform ikonu gösteriliyor — host adı çıkarılıyor

    it('externalUrlHost_ValidUrl_ReturnsHostnameWithoutWww', () => {
      const result = externalUrlHost('https://www.youtube.com/watch?v=test');
      expect(result).toBe('youtube.com');
    });

    it('externalUrlHost_UrlWithoutWww_ReturnsHostname', () => {
      const result = externalUrlHost('https://example.com');
      expect(result).toBe('example.com');
    });

    it('externalUrlHost_UnsafeUrl_ReturnsNull', () => {
      const result = externalUrlHost('javascript:alert("xss")');
      expect(result).toBeNull();
    });

    it('externalUrlHost_EmptyUrl_ReturnsNull', () => {
      const result = externalUrlHost('');
      expect(result).toBeNull();
    });
  });

  describe('formatBookPageRange', () => {
    // AC #2: BookPageRange "Kitap Adı — Test Adı, sayfa X-Y" formatında

    it('formatBookPageRange_BookAndTestWithPageRange_ReturnsFormattedString', () => {
      const source: StudyItemDisplaySource = {
        bookName: 'Matematik',
        bookTestName: 'Test 1',
        startPage: 10,
        endPage: 20,
      };
      const result = formatBookPageRange(source, translate);
      expect(result).toBe('Matematik — Test 1, pages 10-20');
    });

    it('formatBookPageRange_BookOnlyWithPageRange_ReturnsFormattedString', () => {
      const source: StudyItemDisplaySource = {
        bookName: 'Matematikmatik',
        startPage: 5,
        endPage: 15,
      };
      const result = formatBookPageRange(source, translate);
      expect(result).toBe('Matematikmatik, pages 5-15');
    });

    it('formatBookPageRange_TestOnlyWithPageRange_ReturnsFormattedString', () => {
      const source: StudyItemDisplaySource = {
        bookTestName: 'Test 2',
        startPage: 10,
        endPage: 20,
      };
      const result = formatBookPageRange(source, translate);
      expect(result).toBe('Test 2, pages 10-20');
    });

    it('formatBookPageRange_SameSinglePage_ReturnsSinglePageFormat', () => {
      const source: StudyItemDisplaySource = {
        bookName: 'Fizik',
        startPage: 42,
        endPage: 42,
      };
      const result = formatBookPageRange(source, translate);
      expect(result).toBe('Fizik, page 42');
    });

    it('formatBookPageRange_OnlyStartPagePresent_ReturnsPageFormat', () => {
      const source: StudyItemDisplaySource = {
        bookName: 'Kimya',
        startPage: 10,
      };
      const result = formatBookPageRange(source, translate);
      expect(result).toBe('Kimya, page 10');
    });

    it('formatBookPageRange_OnlyEndPagePresent_ReturnsPageFormat', () => {
      const source: StudyItemDisplaySource = {
        bookName: 'Biyoloji',
        endPage: 25,
      };
      const result = formatBookPageRange(source, translate);
      expect(result).toBe('Biyoloji, page 25');
    });

    it('formatBookPageRange_BookNameOnlyNoPages_ReturnsBookNameOnly', () => {
      const source: StudyItemDisplaySource = {
        bookName: 'Tarih',
      };
      const result = formatBookPageRange(source, translate);
      expect(result).toBe('Tarih');
    });

    it('formatBookPageRange_NoFieldsPresent_ReturnsNull', () => {
      const source: StudyItemDisplaySource = {};
      const result = formatBookPageRange(source, translate);
      expect(result).toBeNull();
    });

    it('formatBookPageRange_WhitespaceBookName_TreatedAsEmpty', () => {
      const source: StudyItemDisplaySource = {
        bookName: '  ',
        startPage: 10,
      };
      const result = formatBookPageRange(source, translate);
      expect(result).toBe('page 10');
    });
  });

  describe('describeStudyItem', () => {
    it('describeStudyItem_ImageType_ReturnsImageDisplay', () => {
      // AC #3: Image mevcut davranışıyla değişmeden
      const source: StudyItemDisplaySource = {
        contentType: StudyPageContentType.Image,
      };
      const result = describeStudyItem(source, translate);

      expect(result.kind).toBe('image');
      expect(result.typeLabel).toBe('Image');
      expect(result.platformIcon).toBeNull();
      expect(result.platformLabel).toBeNull();
      expect(result.href).toBeNull();
      expect(result.host).toBeNull();
      expect(result.bookLine).toBeNull();
    });

    it('describeStudyItem_LinkTypeWithYouTube_ReturnsLinkDisplayWithPlatformIcon', () => {
      // AC #1: Link tipi, platform ikonu
      const source: StudyItemDisplaySource = {
        contentType: StudyPageContentType.Link,
        url: 'https://www.youtube.com/watch?v=test',
        platform: StudyPageLinkPlatform.YouTube,
      };
      const result = describeStudyItem(source, translate);

      expect(result.kind).toBe('link');
      expect(result.typeLabel).toBe('Link');
      expect(result.platformLabel).toBe('YouTube');
      expect(result.href).toBe('https://www.youtube.com/watch?v=test');
      expect(result.host).toBe('youtube.com');
      expect(result.bookLine).toBeNull();
    });

    it('describeStudyItem_LinkTypeWithEba_ReturnsLinkDisplayWithEbaPlatformIcon', () => {
      const source: StudyItemDisplaySource = {
        contentType: StudyPageContentType.Link,
        url: 'https://www.eba.gov.tr/path',
        platform: StudyPageLinkPlatform.Eba,
      };
      const result = describeStudyItem(source, translate);

      expect(result.kind).toBe('link');
      expect(result.platformLabel).toBe('EBA');
    });

    it('describeStudyItem_LinkTypeWithOtherPlatform_ReturnsOtherPlatformLabel', () => {
      const source: StudyItemDisplaySource = {
        contentType: StudyPageContentType.Link,
        url: 'https://example.com',
        platform: StudyPageLinkPlatform.Other,
      };
      const result = describeStudyItem(source, translate);

      expect(result.kind).toBe('link');
      expect(result.platformLabel).toBe('Other');
    });

    it('describeStudyItem_LinkTypeWithUnsafeUrl_ReturnsNullHref', () => {
      const source: StudyItemDisplaySource = {
        contentType: StudyPageContentType.Link,
        url: 'javascript:alert("xss")',
        platform: StudyPageLinkPlatform.Other,
      };
      const result = describeStudyItem(source, translate);

      expect(result.kind).toBe('link');
      expect(result.href).toBeNull();
      expect(result.host).toBeNull();
    });

    it('describeStudyItem_BookPageRangeType_ReturnsBookDisplay', () => {
      // AC #2: BookPageRange format
      const source: StudyItemDisplaySource = {
        contentType: StudyPageContentType.BookPageRange,
        bookName: 'Matematik',
        bookTestName: 'Test 1',
        startPage: 10,
        endPage: 20,
      };
      const result = describeStudyItem(source, translate);

      expect(result.kind).toBe('book');
      expect(result.typeLabel).toBe('Book Pages');
      expect(result.bookLine).toBe('Matematik — Test 1, pages 10-20');
      expect(result.platformIcon).toBeNull();
      expect(result.platformLabel).toBeNull();
      expect(result.href).toBeNull();
      expect(result.host).toBeNull();
    });

    it('describeStudyItem_UndefinedContentType_TreatsAsImage', () => {
      // AC #3: contentType eksik/undefined → Image gibi
      const source: StudyItemDisplaySource = {
        contentType: undefined,
      };
      const result = describeStudyItem(source, translate);

      expect(result.kind).toBe('image');
      expect(result.typeLabel).toBe('Image');
    });

    it('describeStudyItem_NullContentType_TreatsAsImage', () => {
      const source: StudyItemDisplaySource = {
        contentType: null,
      };
      const result = describeStudyItem(source, translate);

      expect(result.kind).toBe('image');
    });

    it('describeStudyItem_EmptySource_TreatsAsImage', () => {
      const source: StudyItemDisplaySource = {};
      const result = describeStudyItem(source, translate);

      expect(result.kind).toBe('image');
    });
  });
});
