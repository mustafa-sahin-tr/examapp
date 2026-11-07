import { describe, expect, it } from 'vitest';
import {
  RECENT_FOLDERS_LIMIT,
  RecentLabelingFolder,
  isRecentFolderDone,
  parseRecentFolders,
  rootFolderName,
  upsertRecentFolder,
} from './labeling-recent';

const folder = (name: string, overrides: Partial<RecentLabelingFolder> = {}): RecentLabelingFolder => ({
  name,
  total: 10,
  position: 3,
  page: 3,
  updatedAt: '2026-11-07T10:00:00Z',
  ...overrides,
});

describe('labeling-recent (#867)', () => {
  it('rootFolderName takes the first path segment', () => {
    expect(rootFolderName([{ webkitRelativePath: 'TT MatLab 5/fasikul1/page_001.webp' }])).toBe('TT MatLab 5');
    expect(rootFolderName([{ webkitRelativePath: '' }])).toBeNull();
    expect(rootFolderName([])).toBeNull();
  });

  it('upsert moves the folder to the front, replaces the old entry and keeps the limit', () => {
    let list: RecentLabelingFolder[] = [];
    for (let i = 0; i < RECENT_FOLDERS_LIMIT + 2; i++) {
      list = upsertRecentFolder(list, folder(`f${i}`));
    }
    expect(list).toHaveLength(RECENT_FOLDERS_LIMIT);
    expect(list[0].name).toBe(`f${RECENT_FOLDERS_LIMIT + 1}`);

    list = upsertRecentFolder(list, folder('f3', { position: 9 }));
    expect(list[0]).toMatchObject({ name: 'f3', position: 9 });
    expect(list.filter((f) => f.name === 'f3')).toHaveLength(1);
  });

  it('a folder is done when the position reaches the total', () => {
    expect(isRecentFolderDone(folder('a', { position: 10 }))).toBe(true);
    expect(isRecentFolderDone(folder('a', { position: 9 }))).toBe(false);
  });

  it('parse ignores broken storage', () => {
    expect(parseRecentFolders(null)).toEqual([]);
    expect(parseRecentFolders('not json')).toEqual([]);
    expect(parseRecentFolders('{"a":1}')).toEqual([]);
    expect(parseRecentFolders(JSON.stringify([folder('ok'), { name: 'bad' }]))).toEqual([folder('ok')]);
  });
});
