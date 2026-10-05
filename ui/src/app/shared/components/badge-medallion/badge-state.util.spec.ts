import {
  BADGE_FALLBACK_ICON,
  BadgeTranslate,
  badgeAriaLabel,
  badgeStateText,
  deriveBadgeState,
  formatBadgeEarnedDate,
  resolveBadgeIcon,
} from './badge-state.util';
import trTranslations from '../../../../../public/i18n/tr.json';
import enTranslations from '../../../../../public/i18n/en.json';

/** Gerçek sözlükten basit `{{param}}` çevirisi — anahtar silinirse test kırılır. */
function translatorFor(dictionary: unknown): BadgeTranslate {
  return (key, params) => {
    const value = key.split('.').reduce<unknown>((node, part) => (node as Record<string, unknown>)?.[part], dictionary);
    if (typeof value !== 'string') {
      throw new Error(`missing i18n key ${key}`);
    }
    return value.replace(/\{\{\s*(\w+)\s*\}\}/g, (_, name: string) => String(params?.[name] ?? ''));
  };
}

describe('badge-state.util (issue #149)', () => {
  const DAY = 24 * 60 * 60 * 1000;
  const now = Date.UTC(2026, 9, 5, 12, 0, 0);
  const iso = (time: number) => new Date(time).toISOString();

  describe('deriveBadgeState', () => {
    it('Completed_EarnedMoreThan7DaysAgo_IsEarned', () => {
      expect(deriveBadgeState({ isCompleted: true, earnedDateUtc: iso(now - 8 * DAY), currentValue: 5 }, now)).toBe('earned');
    });

    it('Completed_EarnedWithin7Days_IsNew', () => {
      expect(deriveBadgeState({ isCompleted: true, earnedDateUtc: iso(now - 2 * DAY), currentValue: 5 }, now)).toBe('new');
    });

    it('Completed_ExactlySevenDaysBoundary_IsNewAndOneMsLater_IsEarned', () => {
      expect(deriveBadgeState({ isCompleted: true, earnedDateUtc: iso(now - 7 * DAY), currentValue: 1 }, now)).toBe('new');
      expect(deriveBadgeState({ isCompleted: true, earnedDateUtc: iso(now - 7 * DAY - 1), currentValue: 1 }, now)).toBe(
        'earned'
      );
    });

    it('Completed_ServerIsoWithoutZone_TreatedAsUtc', () => {
      // 6 gün 23 saat önce, "Z" eki yok: yerel saat sayılsaydı sınır kayardı.
      const noZone = iso(now - 7 * DAY + 60 * 60 * 1000).replace('Z', '');
      expect(deriveBadgeState({ isCompleted: true, earnedDateUtc: noZone, currentValue: 1 }, now)).toBe('new');
    });

    it('Completed_FutureDateFromClockSkew_IsNew', () => {
      expect(deriveBadgeState({ isCompleted: true, earnedDateUtc: iso(now + 60_000), currentValue: 1 }, now)).toBe('new');
    });

    it('Completed_MissingOrInvalidDate_IsEarned', () => {
      expect(deriveBadgeState({ isCompleted: true, earnedDateUtc: null, currentValue: 1 }, now)).toBe('earned');
      expect(deriveBadgeState({ isCompleted: true, earnedDateUtc: 'nope', currentValue: 1 }, now)).toBe('earned');
    });

    it('NotCompleted_ZeroProgress_IsLocked', () => {
      expect(deriveBadgeState({ isCompleted: false, earnedDateUtc: null, currentValue: 0 }, now)).toBe('locked');
    });

    it('NotCompleted_SomeProgress_IsInProgress', () => {
      expect(deriveBadgeState({ isCompleted: false, earnedDateUtc: null, currentValue: 1 }, now)).toBe('in-progress');
    });

    it('NotCompleted_RecentEarnedDateIgnored_StaysLocked', () => {
      expect(deriveBadgeState({ isCompleted: false, earnedDateUtc: iso(now), currentValue: 0 }, now)).toBe('locked');
    });
  });

  describe('resolveBadgeIcon', () => {
    it('ValidSymbolName_WinsOverIconUrl', () => {
      expect(resolveBadgeIcon('gps_fixed', 'achievements/a.svg')).toEqual({ kind: 'symbol', name: 'gps_fixed' });
    });

    it('InvalidSymbolName_FallsBackToIconUrl', () => {
      for (const bad of ['GPS', 'a', '1abc', 'has space', 'x'.repeat(65), '<b>x</b>']) {
        expect(resolveBadgeIcon(bad, 'achievements/a.svg')).withContext(bad).toEqual({ kind: 'image', src: '/achievements/a.svg' });
      }
    });

    it('IconUrl_WithOrWithoutLeadingSlash_NormalizedToRootRelative', () => {
      expect(resolveBadgeIcon(null, 'achievements/a.svg')).toEqual({ kind: 'image', src: '/achievements/a.svg' });
      expect(resolveBadgeIcon(null, '/achievements/a.svg')).toEqual({ kind: 'image', src: '/achievements/a.svg' });
    });

    it('IconUrl_OutsideAchievements_FallsBackToDefaultGlyph', () => {
      for (const bad of ['https://x.org/achievements/a.svg', '//x.org/a.svg', 'achievements/../a.svg', 'assets/a.svg', 'achievements/a.png']) {
        expect(resolveBadgeIcon(undefined, bad)).withContext(bad).toEqual({ kind: 'symbol', name: BADGE_FALLBACK_ICON });
      }
    });

    it('NothingGiven_FallsBackToMilitaryTech', () => {
      expect(resolveBadgeIcon(null, null)).toEqual({ kind: 'symbol', name: 'military_tech' });
      expect(resolveBadgeIcon('  ', '  ')).toEqual({ kind: 'symbol', name: 'military_tech' });
    });
  });

  describe('badgeAriaLabel', () => {
    const tr = translatorFor(trTranslations);
    const en = translatorFor(enTranslations);
    const earnedIso = '2026-09-12T10:00:00Z';

    it('Tr_AllStates_MatchDesignNote', () => {
      const base = { name: 'Soru Avcısı', locale: 'tr' };
      expect(badgeAriaLabel(tr, { ...base, state: 'locked', current: 0, target: 500 })).toBe('Kilitli: Soru Avcısı, 0/500');
      expect(badgeAriaLabel(tr, { ...base, state: 'in-progress', current: 180, target: 250 })).toBe(
        'İlerlemede: Soru Avcısı, 180/250'
      );
      expect(badgeAriaLabel(tr, { ...base, state: 'earned', current: 5, target: 5, earnedDateUtc: earnedIso })).toBe(
        'Kazanıldı: Soru Avcısı, 12 Eyl'
      );
      expect(badgeAriaLabel(tr, { ...base, state: 'new', current: 5, target: 5, earnedDateUtc: earnedIso })).toBe(
        'Yeni kazanıldı: Soru Avcısı'
      );
    });

    it('Earned_WithoutDate_OmitsDate', () => {
      expect(badgeAriaLabel(tr, { name: 'X', locale: 'tr', state: 'earned', current: 1, target: 1, earnedDateUtc: null })).toBe(
        'Kazanıldı: X'
      );
    });

    it('En_UsesEnglishDictionary', () => {
      expect(badgeAriaLabel(en, { name: 'Hunter', locale: 'en-US', state: 'locked', current: 0, target: 10 })).toBe(
        'Locked: Hunter, 0/10'
      );
      expect(badgeAriaLabel(en, { name: 'Hunter', locale: 'en-US', state: 'earned', current: 1, target: 1, earnedDateUtc: earnedIso })).toBe(
        'Earned: Hunter, Sep 12'
      );
    });
  });

  describe('badgeStateText', () => {
    const tr = translatorFor(trTranslations);

    it('Tr_AllStates_AlwaysVisibleText', () => {
      const base = { locale: 'tr', current: '0', target: '500' };
      expect(badgeStateText(tr, { ...base, state: 'locked' })).toBe('Kilitli · 0 / 500');
      expect(badgeStateText(tr, { ...base, state: 'in-progress', current: '180' })).toBe('180 / 500');
      expect(badgeStateText(tr, { ...base, state: 'new' })).toBe('Yeni kazanıldı');
      expect(badgeStateText(tr, { ...base, state: 'earned', earnedDateUtc: '2026-09-12T10:00:00Z' })).toBe('Kazanıldı · 12 Eyl');
    });
  });

  it('formatBadgeEarnedDate_InvalidDate_ReturnsEmpty', () => {
    expect(formatBadgeEarnedDate('not a date', 'tr')).toBe('');
    expect(formatBadgeEarnedDate(null, 'tr')).toBe('');
  });
});
