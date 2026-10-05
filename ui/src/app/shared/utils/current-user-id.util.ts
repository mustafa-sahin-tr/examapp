/**
 * localStorage `user` kaydındaki sayısal kullanıcı id'si; yoksa/bozuksa null.
 * Dashboard ve practice-solve (günün soruları seri notu, issue #99) rozet ilerlemesi için kullanır.
 */
export function currentUserId(storage: Storage | null = typeof window !== 'undefined' ? window.localStorage : null): number | null {
  if (!storage) return null;
  try {
    const stored = storage.getItem('user');
    if (!stored) return null;
    const id = Number((JSON.parse(stored) as { id?: unknown } | null)?.id);
    return Number.isFinite(id) && id > 0 ? id : null;
  } catch {
    return null;
  }
}
