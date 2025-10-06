import { Injectable, signal, computed, effect } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of, throwError } from 'rxjs';
import { tap, catchError, shareReplay } from 'rxjs/operators';
import { environment } from '../../environments/environment';

export interface CacheEntry<T> {
  data: T;
  timestamp: number;
  expiry: number; // Expiry time in milliseconds
}

/**
 * Service for caching hierarchical education data (grades, subjects, topics, etc.)
 * to minimize API calls for relatively static data
 */
@Injectable({
  providedIn: 'root',
})
export class CacheService {
  // Cache storage using signals
  private gradeCache = signal<CacheEntry<any[]> | null>(null);
  private subjectCache = signal<CacheEntry<any[]> | null>(null);
  private gradeSubjectCache = signal<{ [gradeId: number]: CacheEntry<any[]> }>({});
  private topicCache = signal<{ [subjectId: number]: CacheEntry<any[]> }>({});
  private subtopicCache = signal<{ [topicId: number]: CacheEntry<any[]> }>({});

  // Cache expiry durations (in milliseconds)
  private readonly CACHE_DURATION = {
    GRADES: 24 * 60 * 60 * 1000, // 24 hours
    SUBJECTS: 24 * 60 * 60 * 1000, // 24 hours
    GRADE_SUBJECTS: 24 * 60 * 60 * 1000, // 24 hours
    TOPICS: 12 * 60 * 60 * 1000, // 12 hours
    SUBTOPICS: 12 * 60 * 60 * 1000, // 12 hours
  };

  // Loading state signals
  public gradesLoading = signal<boolean>(false);
  public subjectsLoading = signal<boolean>(false);
  public gradeSubjectsLoading = signal<boolean>(false);
  public topicsLoading = signal<boolean>(false);
  public subtopicsLoading = signal<boolean>(false);

  // Computed values for external access
  public grades = computed(() => this.gradeCache()?.data || []);
  public subjects = computed(() => this.subjectCache()?.data || []);
  public gradeSubjects = computed(() => this.gradeSubjectCache());
  public topics = computed(() => this.topicCache());
  public subtopics = computed(() => this.subtopicCache());

  constructor(private http: HttpClient) {
    // Initialize cache from localStorage on service creation
    this.loadFromLocalStorage();

    // Set up effect to save cache to localStorage when it changes
    effect(() => {
      this.saveToLocalStorage();
    });
  }

  // Get grades with caching
  getGrades(): Observable<any[]> {
    // Check if we have valid cached data
    if (this.hasValidCache(this.gradeCache())) {
      return of(this.gradeCache()!.data);
    }

    // Set loading state
    this.gradesLoading.set(true);

    // Fetch from API
    return this.http.get<any[]>(`/api/grades`).pipe(
      tap((data) => {
        // Update cache with fresh data
        this.gradeCache.set({
          data,
          timestamp: Date.now(),
          expiry: this.CACHE_DURATION.GRADES,
        });
        this.gradesLoading.set(false);
      }),
      catchError((error) => {
        this.gradesLoading.set(false);
        return throwError(() => error);
      }),
      shareReplay(1)
    );
  }

  // Get subjects with caching
  getSubjects(): Observable<any[]> {
    // Check if we have valid cached data
    if (this.hasValidCache(this.subjectCache())) {
      return of(this.subjectCache()!.data);
    }

    // Set loading state
    this.subjectsLoading.set(true);

    // Fetch from API
    return this.http.get<any[]>(`/api/subject`).pipe(
      tap((data) => {
        // Update cache with fresh data
        this.subjectCache.set({
          data,
          timestamp: Date.now(),
          expiry: this.CACHE_DURATION.SUBJECTS,
        });
        this.subjectsLoading.set(false);
      }),
      catchError((error) => {
        this.subjectsLoading.set(false);
        return throwError(() => error);
      }),
      shareReplay(1)
    );
  }

  // Get subjects for a grade with caching
  getSubjectsByGrade(gradeId: number): Observable<any[]> {
    const cacheMap = this.gradeSubjectCache();

    // Check if we have valid cached data for this grade
    if (cacheMap[gradeId] && this.hasValidCache(cacheMap[gradeId])) {
      return of(cacheMap[gradeId].data);
    }

    // Set loading state
    this.gradeSubjectsLoading.set(true);

    // Fetch from API
    return this.http.get<any[]>(`/api/subject/by-grade/${gradeId}`).pipe(
      tap((data) => {
        // Update cache with fresh data
        const updatedCache = { ...cacheMap };
        updatedCache[gradeId] = {
          data,
          timestamp: Date.now(),
          expiry: this.CACHE_DURATION.GRADE_SUBJECTS,
        };
        this.gradeSubjectCache.set(updatedCache);
        this.gradeSubjectsLoading.set(false);
      }),
      catchError((error) => {
        this.gradeSubjectsLoading.set(false);
        return throwError(() => error);
      }),
      shareReplay(1)
    );
  }

  // Get topics for a subject with caching
  getTopicsBySubject(subjectId: number): Observable<any[]> {
    const cacheMap = this.topicCache();

    // Check if we have valid cached data for this subject
    if (cacheMap[subjectId] && this.hasValidCache(cacheMap[subjectId])) {
      return of(cacheMap[subjectId].data);
    }

    // Set loading state
    this.topicsLoading.set(true);

    // Fetch from API
    return this.http.get<any[]>(`/api/subject/topics/${subjectId}`).pipe(
      tap((data) => {
        // Update cache with fresh data
        const updatedCache = { ...cacheMap };
        updatedCache[subjectId] = {
          data,
          timestamp: Date.now(),
          expiry: this.CACHE_DURATION.TOPICS,
        };
        this.topicCache.set(updatedCache);
        this.topicsLoading.set(false);
      }),
      catchError((error) => {
        this.topicsLoading.set(false);
        return throwError(() => error);
      }),
      shareReplay(1)
    );
  }

  // Get subtopics for a topic with caching
  getSubtopicsByTopic(topicId: number): Observable<any[]> {
    const cacheMap = this.subtopicCache();

    // // Check if we have valid cached data for this topic
    // if (cacheMap[topicId] && this.hasValidCache(cacheMap[topicId])) {
    //   return of(cacheMap[topicId].data);
    // }

    // Set loading state
    this.subtopicsLoading.set(true);

    // Fetch from API
    return this.http.get<any[]>(`/api/subject/subtopics/${topicId}`).pipe(
      tap((data) => {
        // Update cache with fresh data
        const updatedCache = { ...cacheMap };
        updatedCache[topicId] = {
          data,
          timestamp: Date.now(),
          expiry: this.CACHE_DURATION.SUBTOPICS,
        };
        this.subtopicCache.set(updatedCache);
        this.subtopicsLoading.set(false);
      }),
      catchError((error) => {
        this.subtopicsLoading.set(false);
        return throwError(() => error);
      }),
      shareReplay(1)
    );
  }

  // Clear all caches (useful for logout)
  clearCache(): void {
    this.gradeCache.set(null);
    this.subjectCache.set(null);
    this.gradeSubjectCache.set({});
    this.topicCache.set({});
    this.subtopicCache.set({});
    localStorage.removeItem('ms_education_cache');
  }

  // Manually refresh specific data
  refreshGrades(): Observable<any[]> {
    this.gradeCache.set(null);
    return this.getGrades();
  }

  refreshSubjects(): Observable<any[]> {
    this.subjectCache.set(null);
    return this.getSubjects();
  }

  refreshGradeSubjects(gradeId: number): Observable<any[]> {
    const updatedCache = { ...this.gradeSubjectCache() };
    delete updatedCache[gradeId];
    this.gradeSubjectCache.set(updatedCache);
    return this.getSubjectsByGrade(gradeId);
  }

  refreshTopics(subjectId: number): Observable<any[]> {
    const updatedCache = { ...this.topicCache() };
    delete updatedCache[subjectId];
    this.topicCache.set(updatedCache);
    return this.getTopicsBySubject(subjectId);
  }

  refreshSubtopics(topicId: number): Observable<any[]> {
    const updatedCache = { ...this.subtopicCache() };
    delete updatedCache[topicId];
    this.subtopicCache.set(updatedCache);
    return this.getSubtopicsByTopic(topicId);
  }

  // Helper to check if cache entry is valid
  private hasValidCache<T>(entry: CacheEntry<T> | null): boolean {
    if (!entry) return false;

    const now = Date.now();
    const expiryTime = entry.timestamp + entry.expiry;

    return now < expiryTime;
  }

  // Save cache to localStorage
  private saveToLocalStorage(): void {
    const cacheData = {
      grades: this.gradeCache(),
      subjects: this.subjectCache(),
      gradeSubjects: this.gradeSubjectCache(),
      topics: this.topicCache(),
      subtopics: this.subtopicCache(),
    };

    try {
      localStorage.setItem('ms_education_cache', JSON.stringify(cacheData));
    } catch (e) {
      console.error('Error saving cache to localStorage:', e);
    }
  }

  // Load cache from localStorage
  private loadFromLocalStorage(): void {
    try {
      const cacheData = localStorage.getItem('ms_education_cache');
      if (cacheData) {
        const parsedCache = JSON.parse(cacheData);

        if (parsedCache.grades) this.gradeCache.set(parsedCache.grades);
        if (parsedCache.subjects) this.subjectCache.set(parsedCache.subjects);
        if (parsedCache.gradeSubjects) this.gradeSubjectCache.set(parsedCache.gradeSubjects);
        if (parsedCache.topics) this.topicCache.set(parsedCache.topics);
        if (parsedCache.subtopics) this.subtopicCache.set(parsedCache.subtopics);
      }
    } catch (e) {
      console.error('Error loading cache from localStorage:', e);
    }
  }
}
