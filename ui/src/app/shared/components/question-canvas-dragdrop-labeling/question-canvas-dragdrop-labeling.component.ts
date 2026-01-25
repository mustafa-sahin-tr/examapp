import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output, computed, signal } from '@angular/core';
import { DragDropModule, CdkDragDrop, transferArrayItem } from '@angular/cdk/drag-drop';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { QuestionRegion } from '../../../models/draws';

export interface DragDropLabelingPlanV1 {
  version: 1;
  type: 'dragDropLabeling';
  dropZones: Array<{ id: string; x: number; y: number; width: number; height: number }>;
  draggables: Array<{ id: string; label: string }>;
  solution?: {
    placements: Array<{ draggableId: string; dropZoneId: string }>;
  };
}

export interface DragDropLabelingPlacement {
  draggableId: string;
  dropZoneId: string;
}

@Component({
  selector: 'app-question-canvas-dragdrop-labeling',
  standalone: true,
  imports: [CommonModule, DragDropModule, MatIconModule, MatButtonModule],
  templateUrl: './question-canvas-dragdrop-labeling.component.html',
  styleUrls: ['./question-canvas-dragdrop-labeling.component.scss'],
})
export class QuestionCanvasDragDropLabelingComponent {
  @Input({ required: true }) set questionRegion(value: QuestionRegion) {
    this._region.set(value);
  }

  @Input({ required: true }) set interactionPlanJson(value: string | null | undefined) {
    this._planJson.set(value ?? null);
    this._plan.set(this.parsePlan(value));
    this.resetStateFromPlan();
  }

  // If true, show immediate feedback (IsExample)
  @Input() practiceFeedback = false;

  // Used by solve 'passage first' mode: when true, passage should not be rendered in the question view.
  @Input() hidePassage: boolean = false;

  // Current answer payload (persisted)
  @Input() set answerPayloadJson(value: string | null | undefined) {
    this._answerPayloadJson.set(value ?? null);
    this.restoreFromAnswerPayload();
  }

  @Output() answerPayloadChange = new EventEmitter<string>();
  @Output() answered = new EventEmitter<void>();

  private _region = signal<QuestionRegion | null>(null);
  private _planJson = signal<string | null>(null);
  private _plan = signal<DragDropLabelingPlanV1 | null>(null);
  private _answerPayloadJson = signal<string | null>(null);

  // pool holds draggable ids not placed
  public pool = signal<string[]>([]);

  // per dropZone list holds 0..1 draggable id
  public zoneState = signal<Record<string, string[]>>({});

  public plan = computed(() => this._plan());

  public imageUrl = computed(() => this._region()?.imageUrl ?? null);

  public passageImageUrl = computed(() => this._region()?.passage?.imageUrl ?? null);

  public passageTitle = computed(() => this._region()?.passage?.title ?? null);

  public zones = computed(() => this._plan()?.dropZones ?? []);

  // Angular templates don't support inline arrow functions like `.map(z => ...)`.
  // Precompute connected drop-list ids here.
  public zoneDropListIds = computed(() => this.zones().map((z) => `zone-${z.id}`));

  public allDropListIds = computed(() => ['pool', ...this.zoneDropListIds()]);

  public draggableLabel = computed(() => {
    const plan = this._plan();
    const map = new Map<string, string>();
    plan?.draggables?.forEach((d) => map.set(d.id, d.label));
    return (id: string) => map.get(id) ?? id;
  });

  public zoneStyle = computed(() => {
    const region = this._region();
    const plan = this._plan();
    if (!region || !plan) return () => ({ display: 'none' });

    // Coordinates in plan are in original question image pixels (same space as region.width/height).
    // We map them to overlay container which scales with the rendered image.
    return (z: DragDropLabelingPlanV1['dropZones'][number]) => {
      return {
        left: `${(z.x / region.width) * 100}%`,
        top: `${(z.y / region.height) * 100}%`,
        width: `${(z.width / region.width) * 100}%`,
        height: `${(z.height / region.height) * 100}%`,
      } as const;
    };
  });

  public zoneClass = computed(() => {
    return (zoneId: string) => {
      if (!this.practiceFeedback) return '';
      const plan = this._plan();
      const solution = plan?.solution?.placements ?? [];
      const current = this.buildPlacements();
      const expected = solution.find((p) => p.dropZoneId === zoneId)?.draggableId ?? null;
      const actual = current.find((p) => p.dropZoneId === zoneId)?.draggableId ?? null;
      if (!expected || !actual) return '';
      return expected === actual ? 'is-correct' : 'is-incorrect';
    };
  });

  dropToPool(event: CdkDragDrop<string[]>) {
    if (event.previousContainer === event.container) return;
    transferArrayItem(event.previousContainer.data, event.container.data, event.previousIndex, event.currentIndex);
    this.emitAnswerPayload();
  }

  dropToZone(zoneId: string, event: CdkDragDrop<string[]>) {
    const zones = this.zoneState();
    const zoneList = zones[zoneId] ?? [];

    // Enforce single item per zone; if occupied, swap back to pool.
    if (zoneList.length >= 1 && event.previousContainer !== event.container) {
      const removed = zoneList.splice(0, 1);
      this.pool.set([...this.pool(), ...removed]);
    }

    if (event.previousContainer === event.container) return;
    transferArrayItem(event.previousContainer.data, zoneList, event.previousIndex, 0);

    this.zoneState.set({ ...zones, [zoneId]: zoneList });
    this.emitAnswerPayload();
    this.answered.emit();
  }

  clearAll() {
    this.resetStateFromPlan();
    this.emitAnswerPayload();
  }

  private parsePlan(json: string | null | undefined): DragDropLabelingPlanV1 | null {
    if (!json) return null;
    try {
      const parsed = JSON.parse(json) as Partial<DragDropLabelingPlanV1>;
      if (parsed?.type !== 'dragDropLabeling') return null;
      if (!Array.isArray(parsed.dropZones) || !Array.isArray(parsed.draggables)) return null;
      return {
        version: 1,
        type: 'dragDropLabeling',
        dropZones: parsed.dropZones as DragDropLabelingPlanV1['dropZones'],
        draggables: parsed.draggables as DragDropLabelingPlanV1['draggables'],
        solution: parsed.solution as any,
      };
    } catch {
      return null;
    }
  }

  private resetStateFromPlan() {
    const plan = this._plan();
    if (!plan) {
      this.pool.set([]);
      this.zoneState.set({});
      return;
    }

    this.pool.set(plan.draggables.map((d) => d.id));
    const zones: Record<string, string[]> = {};
    plan.dropZones.forEach((z) => (zones[z.id] = []));
    this.zoneState.set(zones);

    // If an answer payload is already provided, restore after initializing.
    this.restoreFromAnswerPayload();
  }

  private restoreFromAnswerPayload() {
    const payload = this._answerPayloadJson();
    const plan = this._plan();
    if (!payload || !plan) return;

    try {
      const parsed = JSON.parse(payload) as { type?: string; placements?: DragDropLabelingPlacement[] };
      if (parsed?.type !== 'dragDropLabeling' || !Array.isArray(parsed.placements)) return;

      const zones: Record<string, string[]> = {};
      plan.dropZones.forEach((z) => (zones[z.id] = []));

      const used = new Set<string>();
      parsed.placements.forEach((p) => {
        if (!p?.dropZoneId || !p?.draggableId) return;
        if (!zones[p.dropZoneId]) return;
        if (used.has(p.draggableId)) return;
        zones[p.dropZoneId] = [p.draggableId];
        used.add(p.draggableId);
      });

      const remaining = plan.draggables.map((d) => d.id).filter((id) => !used.has(id));
      this.zoneState.set(zones);
      this.pool.set(remaining);
    } catch {
      // ignore
    }
  }

  private buildPlacements(): DragDropLabelingPlacement[] {
    const zones = this.zoneState();
    return Object.entries(zones)
      .map(([dropZoneId, list]) => {
        const draggableId = list?.[0];
        return draggableId ? { dropZoneId, draggableId } : null;
      })
      .filter((p): p is DragDropLabelingPlacement => !!p);
  }

  private emitAnswerPayload() {
    const payload = {
      version: 1,
      type: 'dragDropLabeling' as const,
      placements: this.buildPlacements(),
    };
    this.answerPayloadChange.emit(JSON.stringify(payload));
  }
}
