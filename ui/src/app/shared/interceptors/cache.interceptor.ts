import { HttpInterceptorFn, HttpResponse } from '@angular/common/http';
import { tap } from 'rxjs/operators';
import { Observable, of } from 'rxjs';

/**
 * Cache interceptor function for Angular's new HttpInterceptorFn approach
 */
export const cacheInterceptor: HttpInterceptorFn = (req, next) => {
  // Only cache GET requests
  if (req.method !== 'GET') {
    return next(req);
  }

  // API endpoints to cache
  const cacheableUrls = [
    '/api/exam/worksheet/grades',
    '/api/exam/subject',
    '/api/exam/subject/by-grade/',
    '/api/exam/subject/topics/',
    '/api/exam/subject/subtopics/',
  ];

  // Check if the URL is cacheable
  if (!cacheableUrls.some((cacheableUrl) => req.url.includes(cacheableUrl))) {
    return next(req);
  }

  // Use a module-level variable for the cache (similar approach as in a class-based interceptor)
  // Note: This cache won't persist across page reloads, but it's suitable for in-session caching
  const cache = cacheStorage;

  // Cache duration in milliseconds (24 hours)
  const CACHE_DURATION = 24 * 60 * 60 * 1000;

  // Check if we have a cached response
  const cachedResponse = getFromCache(req.url, CACHE_DURATION);
  if (cachedResponse) {
    return of(cachedResponse);
  }

  // If not cached, make the request and cache the response
  return next(req).pipe(
    tap((event) => {
      if (event instanceof HttpResponse) {
        addToCache(req.url, event);
      }
    })
  );
};

// Module-level cache storage
const cacheStorage = new Map<string, { timestamp: number; response: HttpResponse<any> }>();

// Helper function to add response to cache
function addToCache(url: string, response: HttpResponse<any>): void {
  cacheStorage.set(url, {
    timestamp: Date.now(),
    response: response.clone(),
  });
}

// Helper function to get response from cache
function getFromCache(url: string, cacheDuration: number): HttpResponse<any> | undefined {
  const cached = cacheStorage.get(url);

  if (!cached) {
    return undefined;
  }

  // Check if the cached response has expired
  const now = Date.now();
  if (now - cached.timestamp > cacheDuration) {
    cacheStorage.delete(url);
    return undefined;
  }

  return cached.response.clone();
}
