export interface StudyPageImage {
  id: number;
  imageUrl: string;
  sortOrder: number;
  fileName?: string | null;
}

export interface StudyPage {
  id: number;
  title: string;
  description: string;
  subjectId?: number | null;
  topicId?: number | null;
  subTopicId?: number | null;
  isPublished: boolean;
  createdByUserId: number;
  createdByName: string;
  createdByRole: string;
  createTime: string;
  imageCount: number;
  coverImageUrl?: string | null;
  images: StudyPageImage[];
}

export interface StudyPageFilter {
  search?: string | null;
  subjectId?: number | null;
  topicId?: number | null;
  subTopicId?: number | null;
  pageNumber?: number;
  pageSize?: number;
}
