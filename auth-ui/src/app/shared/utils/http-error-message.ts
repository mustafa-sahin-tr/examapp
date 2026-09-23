import { HttpErrorResponse } from '@angular/common/http';

/**
 * Backend'in `{ message }` gövdesindeki lokalize hata metnini döndürür (örn. auth-api
 * 401/503, issue #231). Gövde yoksa veya `message` boş/string değilse `fallback` döner.
 */
export function httpErrorMessage(error: unknown, fallback: string): string {
  if (error instanceof HttpErrorResponse) {
    const body: unknown = error.error;
    if (body && typeof body === 'object' && 'message' in body) {
      const message = (body as { message: unknown }).message;
      if (typeof message === 'string' && message.trim().length > 0) {
        return message;
      }
    }
  }
  return fallback;
}
