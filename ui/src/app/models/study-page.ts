// Backend karşılığı: api/ExamApp.Api/Data/StudyItemContentType.cs
// Enum'lar JSON'da sayısal olarak taşınır (backend'de JsonStringEnumConverter yok).
export enum StudyPageContentType {
  Image = 0,
  Link = 1,
  BookPageRange = 2,
}

export enum StudyPageLinkPlatform {
  Other = 0,
  Eba = 1,
  YouTube = 2,
}

export interface StudyPageImage {
  id: number;
  imageUrl: string;
  sortOrder: number;
  fileName?: string | null;
}

// Backend karşılığı: api/ExamApp.Api/Models/Dtos/StudyItemDto.cs
export interface StudyPage {
  id: number;
  title: string;
  description: string;
  gradeId?: number | null;
  subjectId?: number | null;
  topicId?: number | null;
  subTopicId?: number | null;
  isPublished: boolean;
  createdByUserId: number;
  createdByName: string;
  createdByRole: string;
  createTime: string;

  contentType: StudyPageContentType;

  // Link
  url?: string | null;
  platform?: StudyPageLinkPlatform | null;

  // BookPageRange
  bookId?: number | null;
  bookName?: string | null;
  bookTestId?: number | null;
  bookTestName?: string | null;
  startPage?: number | null;
  endPage?: number | null;

  // Image
  imageCount: number;
  coverImageUrl?: string | null;
  images: StudyPageImage[];
}

export interface StudyPageFilter {
  search?: string | null;
  subjectId?: number | null;
  topicId?: number | null;
  subTopicId?: number | null;
  contentType?: StudyPageContentType | null;
  pageNumber?: number;
  pageSize?: number;
}

export interface StudyPageMinioImage {
  bookName: string;
  pageNumber: number;
  minioUrl: string;
}

// Backend karşılığı: CreateStudyItemRequestDto (api/ExamApp.Api/Models/Dtos/StudyItemRequestDtos.cs)
export interface StudyPageWriteRequest {
  title: string;
  description: string;
  gradeId?: number | null;
  subjectId?: number | null;
  topicId?: number | null;
  subTopicId?: number | null;
  isPublished: boolean;

  contentType: StudyPageContentType;

  // Link
  url?: string | null;
  platform?: StudyPageLinkPlatform | null;

  // BookPageRange — bookId/bookTestId var olan kayıt, newBookName/newBookTestName inline oluşturma
  bookId?: number | null;
  bookTestId?: number | null;
  newBookName?: string | null;
  newBookTestName?: string | null;
  startPage?: number | null;
  endPage?: number | null;

  // Image
  minioImages?: StudyPageMinioImage[];
}

// Backend karşılığı: UpdateStudyItemRequestDto
export interface StudyPageUpdateRequest extends StudyPageWriteRequest {
  removedImageIds: number[];
}

export const STUDY_PAGE_CONTENT_TYPE_LABELS: Record<StudyPageContentType, string> = {
  [StudyPageContentType.Image]: 'Görsel',
  [StudyPageContentType.Link]: 'Link',
  [StudyPageContentType.BookPageRange]: 'Kitap Sayfası',
};

export const STUDY_PAGE_CONTENT_TYPE_ICONS: Record<StudyPageContentType, string> = {
  [StudyPageContentType.Image]: 'image',
  [StudyPageContentType.Link]: 'link',
  [StudyPageContentType.BookPageRange]: 'menu_book',
};

export const STUDY_PAGE_PLATFORM_LABELS: Record<StudyPageLinkPlatform, string> = {
  [StudyPageLinkPlatform.Other]: 'Diğer',
  [StudyPageLinkPlatform.Eba]: 'EBA',
  [StudyPageLinkPlatform.YouTube]: 'YouTube',
};

export const STUDY_PAGE_PLATFORM_ICONS: Record<StudyPageLinkPlatform, string> = {
  [StudyPageLinkPlatform.Other]: 'link',
  [StudyPageLinkPlatform.Eba]: 'school',
  [StudyPageLinkPlatform.YouTube]: 'smart_display',
};
