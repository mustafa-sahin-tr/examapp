import { currentUserId } from './current-user-id.util';

describe('currentUserId', () => {
  function storageWith(value: string | null): Storage {
    return { getItem: () => value } as unknown as Storage;
  }

  it('validUser_ReturnsNumericId', () => {
    expect(currentUserId(storageWith(JSON.stringify({ id: '16' })))).toBe(16);
  });

  it('missingOrInvalid_ReturnsNull', () => {
    expect(currentUserId(storageWith(null))).toBeNull();
    expect(currentUserId(storageWith('{bozuk'))).toBeNull();
    expect(currentUserId(storageWith(JSON.stringify({ id: 0 })))).toBeNull();
    expect(currentUserId(null)).toBeNull();
  });
});
