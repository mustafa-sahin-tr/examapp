import { describe, expect, it } from 'vitest';
import {
  PAGE_IMAGE_MAX_EDGE,
  PAGE_IMAGE_MAX_PIXELS,
  downscaleOversizedPage,
  fitWithinPageLimits,
} from './page-image-limits';

function loadImage(src: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const img = new Image();
    img.onload = () => resolve(img);
    img.onerror = reject;
    img.src = src;
  });
}

function pngDataUrl(width: number, height: number): string {
  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;
  canvas.getContext('2d')!.fillRect(0, 0, width, height);
  return canvas.toDataURL('image/png');
}

describe('page-image-limits (#597)', () => {
  it('fitWithinPageLimits_WithinLimits_ReturnsNull', () => {
    expect(fitWithinPageLimits(4000, 5000)).toBeNull();
    expect(fitWithinPageLimits(PAGE_IMAGE_MAX_EDGE, 2)).toBeNull();
  });

  it('fitWithinPageLimits_BookPage51MP_ShrinksUnderPixelLimitKeepingAspect', () => {
    const size = fitWithinPageLimits(6405, 8000)!;

    expect(size.width * size.height).toBeLessThanOrEqual(PAGE_IMAGE_MAX_PIXELS);
    expect(size.width * size.height).toBeGreaterThan(PAGE_IMAGE_MAX_PIXELS * 0.99);
    expect(size.height / size.width).toBeCloseTo(8000 / 6405, 2);
  });

  it('fitWithinPageLimits_LongEdgeOverLimit_ShrinksToMaxEdge', () => {
    expect(fitWithinPageLimits(PAGE_IMAGE_MAX_EDGE * 2, 100)).toEqual({ width: PAGE_IMAGE_MAX_EDGE, height: 50 });
  });

  it('downscaleOversizedPage_WithinLimits_ReturnsNull', async () => {
    const src = pngDataUrl(40, 30);
    expect(downscaleOversizedPage(await loadImage(src), src)).toBeNull();
  });

  it('downscaleOversizedPage_LongEdgeOverLimit_ReturnsSmallerPngWithinLimits', async () => {
    const src = pngDataUrl(PAGE_IMAGE_MAX_EDGE + 2, 4);

    const scaled = downscaleOversizedPage(await loadImage(src), src)!;

    expect(scaled.startsWith('data:image/png')).toBe(true);
    const img = await loadImage(scaled);
    expect(img.naturalWidth).toBe(PAGE_IMAGE_MAX_EDGE);
    expect(img.naturalHeight).toBe(3);
  });
});
