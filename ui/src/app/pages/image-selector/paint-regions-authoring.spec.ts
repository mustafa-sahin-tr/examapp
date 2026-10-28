// Issue #587 dilim b: boyama bölgeleri yazarlığı — bölge ekleme (dikdörtgen / elips / çokgen), fırça ile çözüm, palet, sorunlar,
// plan / çözüm kurma ve plandan geri yükleme.
import { parseRegionPaintRegionsPlan } from '../../models/region-paint';
import {
  ELLIPSE_POINTS,
  addPaintColor,
  addPaintRegion,
  addPendingPoint,
  buildPaintRegionsInspector,
  buildPaintRegionsPlan,
  buildPaintRegionsSolution,
  createPaintRegionsState,
  ellipsePoints,
  paintRegionAt,
  paintRegionsIssues,
  paintRegionsStateFromPlan,
  rectPoints,
  removePaintColor,
  removePaintRegion,
  setPaintBrush,
  setPaintColor,
  togglePaintRegionFill,
} from './paint-regions-authoring';

describe('paint-regions-authoring (#587)', () => {
  const box = { x: 10, y: 20, width: 40, height: 30 };

  it('newRegion_TakesBrushColor_EmptyBrushLeavesItEmpty', () => {
    let state = addPaintRegion(createPaintRegionsState(), rectPoints(box));
    expect(state.regions[0].points).toEqual([[10, 20], [50, 20], [50, 50], [10, 50]]);
    expect(state.fills).toEqual({ r1: 'p1' });
    state = addPaintRegion(setPaintBrush(state, null), ellipsePoints(box));
    expect(state.regions[1].points.length).toBe(ELLIPSE_POINTS);
    expect(state.fills).toEqual({ r1: 'p1' });
    expect(paintRegionsIssues(state)).toEqual([]);
  });

  it('degeneratePolygon_Ignored', () => {
    const state = addPaintRegion(createPaintRegionsState(), [[0, 0], [5, 5], [9, 9]]);
    expect(state.regions).toEqual([]);
    expect(paintRegionsIssues(state)).toEqual(['noRegions']);
  });

  it('toggleFill_UsesBrush_SecondClickClears', () => {
    let state = setPaintBrush(addPaintRegion(setPaintBrush(createPaintRegionsState(), null), rectPoints(box)), 'p1');
    expect(paintRegionsIssues(state)).toEqual(['noTargets']);
    state = togglePaintRegionFill(state, 'r1');
    expect(state.fills).toEqual({ r1: 'p1' });
    state = togglePaintRegionFill(state, 'r1');
    expect(state.fills).toEqual({});
  });

  it('polygon_ClosesOnFirstPoint', () => {
    let state = createPaintRegionsState();
    for (const p of [[0, 0], [40, 0], [40, 40]] as [number, number][]) state = addPendingPoint(state, p);
    expect(state.pending.length).toBe(3);
    expect(paintRegionsIssues(state)).toContain('pendingPolygon');
    state = addPendingPoint(state, [3, 2]);
    expect(state.pending).toEqual([]);
    expect(state.regions.length).toBe(1);
    expect(state.regions[0].points.length).toBe(3);
  });

  it('hitTest_PrefersSmallestRegion', () => {
    let state = addPaintRegion(createPaintRegionsState(), rectPoints({ x: 0, y: 0, width: 100, height: 100 }));
    state = addPaintRegion(state, rectPoints({ x: 10, y: 10, width: 20, height: 20 }));
    expect(paintRegionAt(state, 15, 15)?.id).toBe('r2');
    expect(paintRegionAt(state, 80, 80)?.id).toBe('r1');
    expect(paintRegionAt(state, 200, 200)).toBeNull();
  });

  it('palette_AddRemove_RemovingColorClearsItsTargets', () => {
    let state = addPaintColor(createPaintRegionsState());
    expect(state.palette.map((p) => p.color)).toEqual(['blue', 'red']);
    expect(state.brush).toBe('p2');
    state = addPaintRegion(state, rectPoints(box));
    expect(state.fills).toEqual({ r1: 'p2' });
    expect(setPaintColor(state, 'p2', 'blue')).toBe(state);
    state = removePaintColor(state, 'p2');
    expect(state.fills).toEqual({});
    expect(state.brush).toBe('p1');
    expect(removePaintColor(state, 'p1')).toBe(state);
  });

  it('build_PlanSolution_RoundTrip', () => {
    let state = addPaintRegion(addPaintColor(createPaintRegionsState()), rectPoints(box));
    state = addPaintRegion(setPaintBrush(state, null), rectPoints({ x: 60, y: 20, width: 20, height: 20 }));
    state = { ...state, style: 'circle', regions: state.regions.map((r, i) => (i === 0 ? { ...r, label: ' 12 ' } : r)) };
    const plan = buildPaintRegionsPlan(state);
    expect(plan).toEqual({
      version: 1,
      type: 'regionPaint',
      mode: 'regions',
      style: 'circle',
      palette: [{ id: 'p1', color: 'blue' }, { id: 'p2', color: 'red' }],
      regions: [
        { id: 'r1', points: [[10, 20], [50, 20], [50, 50], [10, 50]], label: '12' },
        { id: 'r2', points: [[60, 20], [80, 20], [80, 40], [60, 40]] },
      ],
    });
    const solution = buildPaintRegionsSolution(state);
    expect(solution).toEqual({ fills: { r1: 'p2' } });
    const restored = paintRegionsStateFromPlan(parseRegionPaintRegionsPlan(JSON.stringify(plan))!, JSON.stringify(solution));
    expect(buildPaintRegionsPlan(restored)).toEqual(plan);
    expect(buildPaintRegionsSolution(restored)).toEqual(solution);
    expect(buildPaintRegionsSolution(removePaintRegion(state, 'r1'))).toBeNull();
  });

  it('inspector_CountsTargetsPerColor', () => {
    const state = addPaintRegion(addPaintRegion(createPaintRegionsState(), rectPoints(box)), rectPoints({ x: 60, y: 0, width: 9, height: 9 }));
    const ins = buildPaintRegionsInspector(state);
    expect(ins.palette[0].targets).toBe(2);
    expect(ins.regions.map((r) => [r.no, r.fill])).toEqual([[1, 'p1'], [2, 'p1']]);
    expect(ins.freeColors).not.toContain('blue');
  });
});
