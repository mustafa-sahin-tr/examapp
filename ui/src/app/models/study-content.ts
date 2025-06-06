export interface StudyContent {
  id: number;
  title: string;
  type: 'video' | 'document'; // Type of content
  url: string; // URL to the video or document
  subtopicId: number;
  description?: string;
}
