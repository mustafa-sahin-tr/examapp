import { Injectable, signal, computed, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { CacheService } from './cache.service';
import { Subject } from '../models/subject';
import { Topic } from '../models/topic';
import { SubTopic } from '../models/subtopic';
import { Observable, tap, map, switchMap, of } from 'rxjs';
import { Grade } from '../models/student';

/**
 * Data service that provides easy access to educational entities with automatic caching
 */
@Injectable({
  providedIn: 'root',
})
export class EducationDataService {
  private http = inject(HttpClient);
  private cacheService = inject(CacheService);

  // Signal stores for entities
  private _grades = signal<Grade[]>([]);
  private _subjects = signal<Subject[]>([]);
  private _gradeSubjects = signal<{ [gradeId: number]: Subject[] }>({});
  private _topics = signal<{ [subjectId: number]: Topic[] }>({});
  private _subtopics = signal<{ [topicId: number]: SubTopic[] }>({});

  // Loading states
  private _gradesLoading = signal<boolean>(false);
  private _subjectsLoading = signal<boolean>(false);
  private _topicsLoading = signal<{ [subjectId: number]: boolean }>({});
  private _subtopicsLoading = signal<{ [topicId: number]: boolean }>({});

  // Public computed values
  public grades = computed(() => this._grades());
  public subjects = computed(() => this._subjects());
  public gradesLoading = computed(() => this._gradesLoading());
  public subjectsLoading = computed(() => this._subjectsLoading());

  constructor() {
    // Initialize from localStorage if available
    this.loadFromCache();
  }

  /**
   * Get a specific grade by ID - returns undefined if not found or not loaded
   */
  getGrade(id: number): Grade | undefined {
    return this._grades().find((g) => g.id === id);
  }

  /**
   * Get a specific subject by ID - returns undefined if not found or not loaded
   */
  getSubject(id: number): Subject | undefined {
    return this._subjects().find((s) => s.id === id);
  }

  /**
   * Get topics for a specific subject - returns empty array if not found or not loaded
   */
  getTopics(subjectId: number): Topic[] {
    return this._topics()[subjectId] || [];
  }

  /**
   * Check if topics are loading for a subject
   */
  isTopicsLoading(subjectId: number): boolean {
    return !!this._topicsLoading()[subjectId];
  }

  /**
   * Get subtopics for a specific topic - returns empty array if not found or not loaded
   */
  getSubtopics(topicId: number): SubTopic[] {
    return this._subtopics()[topicId] || [];
  }

  /**
   * Check if subtopics are loading for a topic
   */
  isSubtopicsLoading(topicId: number): boolean {
    return !!this._subtopicsLoading()[topicId];
  }

  /**
   * Get subjects for a specific grade - returns empty array if not found or not loaded
   */
  getSubjectsForGrade(gradeId: number): Subject[] {
    return this._gradeSubjects()[gradeId] || [];
  }

  /**
   * Load all grades - uses cache if available
   */
  loadGrades(): Observable<Grade[]> {
    // If we already have grades loaded, return them
    if (this._grades().length > 0) {
      return of(this._grades());
    }

    this._gradesLoading.set(true);

    return this.cacheService.getGrades().pipe(
      tap((grades: Grade[]) => {
        this._grades.set(grades);
        this._gradesLoading.set(false);
      })
    );
  }

  /**
   * Load all subjects - uses cache if available
   */
  loadSubjects(): Observable<Subject[]> {
    // If we already have subjects loaded, return them
    if (this._subjects().length > 0) {
      return of(this._subjects());
    }

    this._subjectsLoading.set(true);

    return this.cacheService.getSubjects().pipe(
      tap((subjects: Subject[]) => {
        this._subjects.set(subjects);
        this._subjectsLoading.set(false);
      })
    );
  }

  /**
   * Load subjects for a specific grade - uses cache if available
   */
  loadSubjectsForGrade(gradeId: number): Observable<Subject[]> {
    // If we already have subjects for this grade loaded, return them
    if (this._gradeSubjects()[gradeId]?.length > 0) {
      return of(this._gradeSubjects()[gradeId]);
    }

    return this.cacheService.getSubjectsByGrade(gradeId).pipe(
      tap((subjects: Subject[]) => {
        const currentGradeSubjects = { ...this._gradeSubjects() };
        currentGradeSubjects[gradeId] = subjects;
        this._gradeSubjects.set(currentGradeSubjects);
      })
    );
  }

  /**
   * Load topics for a specific subject - uses cache if available
   */
  loadTopics(subjectId: number): Observable<Topic[]> {
    // If we already have topics for this subject loaded, return them
    if (this._topics()[subjectId]?.length > 0) {
      return of(this._topics()[subjectId]);
    }

    // Set loading state for this subject
    const currentTopicsLoading = { ...this._topicsLoading() };
    currentTopicsLoading[subjectId] = true;
    this._topicsLoading.set(currentTopicsLoading);

    return this.cacheService.getTopicsBySubject(subjectId).pipe(
      tap((topics: Topic[]) => {
        const currentTopics = { ...this._topics() };
        currentTopics[subjectId] = topics;
        this._topics.set(currentTopics);

        // Reset loading state
        const updatedTopicsLoading = { ...this._topicsLoading() };
        updatedTopicsLoading[subjectId] = false;
        this._topicsLoading.set(updatedTopicsLoading);
      })
    );
  }

  /**
   * Load subtopics for a specific topic - uses cache if available
   */
  loadSubtopics(topicId: number): Observable<SubTopic[]> {
    // If we already have subtopics for this topic loaded, return them
    if (this._subtopics()[topicId]?.length > 0) {
      return of(this._subtopics()[topicId]);
    }

    // Set loading state for this topic
    const currentSubtopicsLoading = { ...this._subtopicsLoading() };
    currentSubtopicsLoading[topicId] = true;
    this._subtopicsLoading.set(currentSubtopicsLoading);

    return this.cacheService.getSubtopicsByTopic(topicId).pipe(
      tap((subtopics: SubTopic[]) => {
        const currentSubtopics = { ...this._subtopics() };
        currentSubtopics[topicId] = subtopics;
        this._subtopics.set(currentSubtopics);

        // Reset loading state
        const updatedSubtopicsLoading = { ...this._subtopicsLoading() };
        updatedSubtopicsLoading[topicId] = false;
        this._subtopicsLoading.set(updatedSubtopicsLoading);
      })
    );
  }

  /**
   * Load necessary data based on the context
   * This is a convenience method that helps load data for typical navigation flows
   */
  loadEducationData(params: { gradeId?: number; subjectId?: number; topicId?: number }): Observable<any> {
    // Start by loading grades and subjects
    let chain$ = this.loadGrades().pipe(switchMap(() => this.loadSubjects()));

    // If grade ID is provided, load subjects for that grade
    if (params.gradeId) {
      chain$ = chain$.pipe(switchMap(() => this.loadSubjectsForGrade(params.gradeId!)));
    }

    // If subject ID is provided, load topics for that subject
    if (params.subjectId) {
      chain$ = chain$.pipe(switchMap(() => this.loadTopics(params.subjectId!)));
    }

    // If topic ID is provided, load subtopics for that topic
    if (params.topicId) {
      chain$ = chain$.pipe(switchMap(() => this.loadSubtopics(params.topicId!)));
    }

    return chain$;
  }

  /**
   * Force refresh all data (clears cache and reloads from server)
   */
  refreshAll(): Observable<any> {
    this._grades.set([]);
    this._subjects.set([]);
    this._gradeSubjects.set({});
    this._topics.set({});
    this._subtopics.set({});

    this.cacheService.clearCache();

    return this.loadGrades().pipe(switchMap(() => this.loadSubjects()));
  }

  private loadFromCache(): void {
    // Access cache service data if available
    const grades = this.cacheService.grades();
    const subjects = this.cacheService.subjects();

    if (grades.length > 0) {
      this._grades.set(grades);
    }

    if (subjects.length > 0) {
      this._subjects.set(subjects);
    }
  }
}
