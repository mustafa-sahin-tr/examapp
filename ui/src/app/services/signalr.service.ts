import { inject, Injectable } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router } from '@angular/router';
import { Subject } from 'rxjs';
import { AccessRequestUpdate } from '../models/worksheet-access-request.model';
import { BadgeEarnedPushPayload, toCoalescedCount } from '../models/notification.model';
import {
  TeacherApplicationSubmittedPayload,
  TeacherApplicationDecidedPayload,
  TeacherSchoolRequestSubmittedPayload,
} from '../models/teacher-application.model';
import { TranslocoService } from '@jsverse/transloco';
import {
  WORKSHEET_COMMENT_CREATED_TYPE,
  WORKSHEET_COMMENT_REPLIED_TYPE,
  parseWorksheetCommentRef,
  worksheetCommentLink,
} from '../models/worksheet-comment.model';
import {
  DIRECT_MESSAGE_RECEIVED_TYPE,
  DIRECT_MESSAGE_REPORTED_TYPE,
  DirectMessageNotificationRef,
  toDirectMessageNotificationRef,
} from '../models/direct-message.model';
import { AuthService } from './auth.service';
import { stripInvisibleControls } from '../shared/utils/display-text.util';
import {
  BADGE_TOAST_DURATION_MS,
  BADGE_TOAST_PANEL_CLASS,
  BADGE_TOAST_POLITENESS,
  BadgeEarnedToastComponent,
} from '../shared/components/badge-earned-toast/badge-earned-toast.component';

export interface ReminderDuePayload {
  notificationId: number;
  worksheetId: number;
  worksheetName: string;
  scheduledFor: string;
  title: string;
  body: string;
}

/**
 * Issue #105: `WorksheetCommentCreated` (öğretmene) / `WorksheetCommentReplied` (öğrenciye) push payload'ı.
 * Güvenilmeyen veri: handler alanları tek tek doğrular (`parseWorksheetCommentRef`, metin uzunluk sınırı).
 */
export interface WorksheetCommentPushPayload {
  notificationId: number;
  worksheetId: number;
  questionId: number | null;
  commentId: number;
  rootCommentId: number | null;
  /** Issue #309: sorunun 1 tabanlı sırası; worksheet seviyesinde null. Snackbar başlığında kullanılmaz. */
  questionOrder: number | null;
  worksheetTitle: string;
  title: string;
  body: string;
  /**
   * Issue #305 dilim B: birleştirilmiş okunmamış bildirimin olay sayısı. > 1 ise aynı thread'in bildirimi güncellendi —
   * toast gösterilmez, yalnız zil sayacı tazelenir. Eski sunucuda yok → 1. Doğrulama {@link toCoalescedCount}.
   */
  coalescedCount?: number;
}

/** Snackbar'da gösterilecek push başlığının üst sınırı (backend zaten 200'e kırpar). */
const MAX_PUSH_TITLE_LENGTH = 200;
/** Rozet toast'unda açıklama üst sınırı (görselde zaten 2 satıra kırpılır). */
const MAX_BADGE_DESCRIPTION_LENGTH = 300;
/** İkon alanları için kaba uzunluk sınırı; biçim `resolveBadgeIcon`'da doğrulanır. */
const MAX_BADGE_ICON_FIELD_LENGTH = 256;

@Injectable({ providedIn: 'root' })
export class SignalRService {
  private hubConnection!: signalR.HubConnection;
  private readonly snackBar = inject(MatSnackBar);
  private readonly router = inject(Router);
  private readonly authService = inject(AuthService);
  private readonly transloco = inject(TranslocoService);

  /** BadgeService `AccessRequestUpdate` event akışı (atama izni request/approve — issue #13). */
  private readonly accessRequestUpdatesSubject = new Subject<AccessRequestUpdate>();
  public readonly accessRequestUpdates$ = this.accessRequestUpdatesSubject.asObservable();

  /** BadgeService `TeacherApplicationSubmitted` event akışı (bağımsız öğretmen başvurusu — issue #94, yalnızca Admin). */
  private readonly teacherApplicationSubmittedSubject = new Subject<TeacherApplicationSubmittedPayload>();
  public readonly teacherApplicationSubmitted$ = this.teacherApplicationSubmittedSubject.asObservable();

  /** BadgeService `TeacherSchoolRequestSubmitted` event akışı (öğretmen okul bağlantısı talebi — issue #277, yalnızca Admin). */
  private readonly teacherSchoolRequestSubmittedSubject = new Subject<TeacherSchoolRequestSubmittedPayload>();
  public readonly teacherSchoolRequestSubmitted$ = this.teacherSchoolRequestSubmittedSubject.asObservable();

  /**
   * BadgeService `TeacherApplicationDecided` event akışı (öğretmen başvuru kararı — issue #157).
   * Backend zaten Clients.User(sub) ile yalnızca başvuru sahibine gönderir.
   */
  private readonly teacherApplicationDecidedSubject = new Subject<TeacherApplicationDecidedPayload>();
  public readonly teacherApplicationDecided$ = this.teacherApplicationDecidedSubject.asObservable();

  /**
   * Issue #146: kalıcı bildirim üretebilen herhangi bir hub push'u geldiğinde tetiklenir (zil sayacını tazelemek için).
   * Payload taşımaz; tüketici kendi debounce'unu uygular.
   */
  private readonly notificationsChangedSubject = new Subject<void>();
  public readonly notificationsChanged$ = this.notificationsChangedSubject.asObservable();

  /**
   * Issue #106 dilim b: `DirectMessageReceived` push'u (alıcıya, Clients.User(sub)). Payload'dan yalnız doğrulanmış
   * `{conversationId, senderRole}` yayılır (mesaj gövdesi taşınmaz). Tüketici sidenav rozetini tazeler; açık konuşma
   * paneli isteyen sayfa aynı akışa abone olup mesajları yeniden yükleyebilir.
   */
  private readonly directMessageReceivedSubject = new Subject<DirectMessageNotificationRef>();
  public readonly directMessageReceived$ = this.directMessageReceivedSubject.asObservable();

  public startConnection() {
    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl('/hub/badges', {
        accessTokenFactory: () => {
          const token = localStorage.getItem('auth_token');
          return token ? token : '';
        },
        transport: signalR.HttpTransportType.WebSockets,
        skipNegotiation: true,
      }) // BadgeService adresi
      .build();

    this.hubConnection
      .start()
      .then(() => console.log('SignalR bağlantısı kuruldu'))
      .catch((err) => console.error('SignalR bağlantı hatası:', err));

    // Issue #149: emoji'li düz metin yerine medalyonlu toast; "Rozetlerim" eylemi /certificates.
    this.hubConnection.on('BadgeEarned', (data: unknown) => this.onBadgeEarnedPush(data));

    this.hubConnection.on('AccessRequestUpdate', (data: AccessRequestUpdate) => {
      this.accessRequestUpdatesSubject.next(data);
      this.notificationsChangedSubject.next();
      if (data.kind === 'approved' || data.kind === 'rejected') {
        this.snackBar.open(data.title || data.body, this.t('common.ok'), { duration: 6000 });
      }
    });

    this.hubConnection.on('ReminderDue', (data: ReminderDuePayload) => {
      this.notificationsChangedSubject.next();
      const ref = this.snackBar.open(`⏰ ${data.title}`, this.t('common.notifications.goToExam'), {
        duration: 8000,
      });
      ref.onAction().subscribe(() => {
        this.router.navigate(['/test', data.worksheetId]);
      });
    });

    this.hubConnection.on('TeacherApplicationSubmitted', (data: TeacherApplicationSubmittedPayload) => {
      // Backend zaten role:Admin grubuna gönderir; istemci tarafı kontrol savunma amaçlı.
      if (!this.authService.hasRole('Admin')) {
        return;
      }
      this.teacherApplicationSubmittedSubject.next(data);
      this.notificationsChangedSubject.next();
      const ref = this.snackBar.open(
        this.t('common.notifications.teacherApplication', { name: data.applicantName }),
        this.t('common.notifications.goToApplications'),
        { duration: 8000 },
      );
      ref.onAction().subscribe(() => {
        this.router.navigate(['/admin/teacher-approvals']);
      });
    });

    this.hubConnection.on('TeacherSchoolRequestSubmitted', (data: TeacherSchoolRequestSubmittedPayload) => {
      // Backend zaten role:Admin grubuna gönderir; istemci tarafı kontrol savunma amaçlı (TeacherApplicationSubmitted ile aynı).
      if (!this.authService.hasRole('Admin')) {
        return;
      }
      this.teacherSchoolRequestSubmittedSubject.next(data);
      this.notificationsChangedSubject.next();
      const ref = this.snackBar.open(
        this.t('common.notifications.teacherSchoolRequest', { name: data.applicantName, school: data.schoolName }),
        this.t('common.notifications.goToApplications'),
        { duration: 8000 },
      );
      ref.onAction().subscribe(() => {
        this.router.navigate(['/admin/teacher-approvals']);
      });
    });

    this.hubConnection.on('TeacherApplicationDecided', (data: TeacherApplicationDecidedPayload) => {
      this.teacherApplicationDecidedSubject.next(data);
      this.notificationsChangedSubject.next();
      // Güvenlik kararı (issue #157): gerekçe/admin kimliği taşınmaz, backend'in ürettiği sabit metin gösterilir.
      // issue #157 review: yalnızca bağımsız öğretmen başvurusunda (/tutor-profile) bir hedef sayfa var;
      // okul bağlantısı talebinde (isIndependentTutor=false) öğretmenin özel bir profil sayfası yok —
      // bu durumda aksiyon BUTONU gösterilmez, sadece bilgilendirme mesajı.
      if (data.isIndependentTutor) {
        const ref = this.snackBar.open(data.body || data.title, this.t('common.notifications.goToProfile'), {
          duration: 8000,
        });
        ref.onAction().subscribe(() => {
          this.router.navigate(['/tutor-profile']);
        });
      } else {
        this.snackBar.open(data.body || data.title, this.t('common.close'), { duration: 8000 });
      }
    });

    // Issue #105: yorum bildirimleri kalıcı yazılır (zil sayacı) + "Görüntüle" ile worksheet-detail derin linki.
    for (const event of [WORKSHEET_COMMENT_CREATED_TYPE, WORKSHEET_COMMENT_REPLIED_TYPE]) {
      this.hubConnection.on(event, (data: unknown) => this.onWorksheetCommentPush(data));
    }

    // Issue #106 b: DM bildirimi kalıcı yazılır (zil sayacı) + sidenav DM rozeti tazelenir; toast yok (rozet/zil yeterli).
    this.hubConnection.on(DIRECT_MESSAGE_RECEIVED_TYPE, (data: unknown) => {
      this.notificationsChangedSubject.next();
      const ref = toDirectMessageNotificationRef(data);
      if (ref) {
        this.directMessageReceivedSubject.next(ref);
      }
    });

    // Issue #106 b: şikayet bildirimi yalnız Admin grubuna gider; istemci kontrolü savunma amaçlı. Yalnız zil sayacı.
    this.hubConnection.on(DIRECT_MESSAGE_REPORTED_TYPE, () => {
      if (this.authService.hasRole('Admin')) {
        this.notificationsChangedSubject.next();
      }
    });

    // Issue #146: randevu bildirimleri (BookingRequestCreated vb.) kalıcı yazılır; burada yalnızca zil sayacı tazelenir,
    // toast gösterilmez (randevu ekranları kendi durumunu yükler).
    this.hubConnection.on('BookingUpdate', () => {
      this.notificationsChangedSubject.next();
    });

    // Issue #423: veli bildirimleri (bağlantı, gecikmiş ödev, test tamamlandı) kalıcı yazılır; yalnız zil sayacı tazelenir.
    this.hubConnection.on('ParentNotification', () => {
      this.notificationsChangedSubject.next();
    });
  }

  /** Rozet push'u: payload güvenilmez — alanlar tek tek doğrulanır, metin kırpılır ve görünmez kontroller silinir. */
  private onBadgeEarnedPush(data: unknown): void {
    this.notificationsChangedSubject.next();
    this.snackBar.openFromComponent(BadgeEarnedToastComponent, {
      data: parseBadgeEarnedPush(data),
      panelClass: BADGE_TOAST_PANEL_CLASS,
      duration: BADGE_TOAST_DURATION_MS,
      politeness: BADGE_TOAST_POLITENESS,
    });
  }

  /** Yorum push'u: payload güvenilmez — kimlikler doğrulanmadan link kurulmaz, başlık düz metin ve kırpılır. */
  private onWorksheetCommentPush(data: unknown): void {
    this.notificationsChangedSubject.next();
    const record = data && typeof data === 'object' ? (data as Partial<Record<keyof WorksheetCommentPushPayload, unknown>>) : {};
    // Issue #305 B: birleştirilmiş bildirim (aynı thread'e ardışık yorum) — toast seli olmasın, yalnız zil sayacı.
    if (toCoalescedCount(record.coalescedCount) > 1) {
      return;
    }
    const rawTitle =
      typeof record.title === 'string' ? stripInvisibleControls(record.title).trim().slice(0, MAX_PUSH_TITLE_LENGTH) : '';
    const message = rawTitle || this.t('common.notifications.newComment');
    const ref = parseWorksheetCommentRef(data);
    if (!ref) {
      this.snackBar.open(message, this.t('common.close'), { duration: 8000 });
      return;
    }
    const link = worksheetCommentLink(ref);
    const snack = this.snackBar.open(message, this.t('common.notifications.view'), { duration: 8000 });
    snack.onAction().subscribe(() => {
      void this.router.navigate(link.commands, { queryParams: link.queryParams });
    });
  }

  private t(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(key, params) ?? '';
  }
}

/** Issue #149: `BadgeEarned` hub yükünü toast verisine çevirir; bozuk/eksik alan boş değere düşer (toast yine açılır). */
export function parseBadgeEarnedPush(data: unknown): BadgeEarnedPushPayload {
  const record = data && typeof data === 'object' ? (data as Record<string, unknown>) : {};
  return {
    badgeName: cleanText(record['badgeName'], MAX_PUSH_TITLE_LENGTH),
    description: cleanText(record['description'], MAX_BADGE_DESCRIPTION_LENGTH),
    icon: cleanText(record['icon'], MAX_BADGE_ICON_FIELD_LENGTH) || null,
    iconUrl: cleanText(record['iconUrl'], MAX_BADGE_ICON_FIELD_LENGTH) || null,
  };
}

function cleanText(value: unknown, maxLength: number): string {
  return typeof value === 'string' ? stripInvisibleControls(value).trim().slice(0, maxLength) : '';
}
