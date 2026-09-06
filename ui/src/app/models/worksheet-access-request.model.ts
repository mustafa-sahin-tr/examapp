export type WorksheetAccessRequestStatus = 'Pending' | 'Approved' | 'Rejected';

/**
 * Sahibin "gelen atama izni talepleri" ekranındaki tek satır (issue #13).
 * Backend: `WorksheetAccessRequestDto`.
 */
export interface WorksheetAccessRequest {
  id: number;
  worksheetId: number;
  worksheetName: string;
  requesterUserId: number;
  requesterName: string;
  note?: string | null;
  status: WorksheetAccessRequestStatus;
  createTime: string;
  decisionAt?: string | null;
}

/** Backend: `CreateWorksheetAccessRequestDto`. */
export interface CreateWorksheetAccessRequest {
  worksheetId: number;
  note?: string | null;
}

/** Backend: `ResponseBaseDto`. */
export interface ResponseBase {
  success: boolean;
  message: string;
  objectId: number;
  notFound: boolean;
  forbidden: boolean;
  conflict: boolean;
}

/** BadgeService SignalR `AccessRequestUpdate` payload'ı. */
export interface AccessRequestUpdate {
  notificationId: number;
  kind: 'requested' | 'approved' | 'rejected';
  requestId: number;
  worksheetId: number;
  worksheetName: string;
  title: string;
  body: string;
}
