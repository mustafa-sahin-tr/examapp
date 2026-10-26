import { ActiveTimeTracker } from './active-time-tracker';

describe('ActiveTimeTracker (#572)', () => {
  let now = 0;
  const tracker = (capSeconds = 1800) => new ActiveTimeTracker({ now: () => now, capSeconds });
  const advance = (seconds: number) => (now += seconds * 1000);

  beforeEach(() => (now = 1_000_000));

  it('countsTimeBetweenInteractions_WithinIdleWindow', () => {
    const t = tracker();
    t.start();
    advance(30);
    t.activity();
    advance(90);
    t.activity();

    expect(t.seconds()).toBe(120);
  });

  it('gapLongerThanTwoMinutes_IsNotCounted', () => {
    const t = tracker();
    t.start();
    advance(20);
    t.activity();
    advance(5 * 60); // hareketsiz
    t.activity();
    advance(10);
    t.activity();

    expect(t.seconds()).toBe(30);
  });

  it('openGapLongerThanTwoMinutes_NotCountedWhenReading', () => {
    const t = tracker();
    t.start();
    advance(60);
    expect(t.seconds()).toBe(60);
    advance(70); // son etkileşimden 130 sn → aralık sayılmaz

    expect(t.seconds()).toBe(0);
  });

  it('hiddenTab_IsNotCounted', () => {
    const t = tracker();
    t.start();
    advance(10);
    t.setVisible(false);
    advance(60);
    t.activity(); // gizliyken etkileşim yok sayılır
    advance(60);
    t.setVisible(true);
    advance(15);
    t.activity();

    expect(t.seconds()).toBe(25);
  });

  it('startHidden_CountsOnlyAfterVisible', () => {
    const t = tracker();
    t.start(0, false);
    advance(100);
    t.setVisible(true);
    advance(5);
    t.activity();

    expect(t.seconds()).toBe(5);
  });

  it('capsAtMaximum_IncludingSeed', () => {
    const t = tracker(100);
    t.start(90);
    advance(60);
    t.activity();

    expect(t.seconds()).toBe(100);
  });

  it('defaultCap_Is1800Seconds', () => {
    const t = new ActiveTimeTracker({ now: () => now });
    t.start(1790);
    advance(60);
    t.activity();

    expect(t.seconds()).toBe(1800);
  });

  it('stop_FreezesTotal', () => {
    const t = tracker();
    t.start(5);
    advance(10);
    t.stop();
    advance(10);
    t.activity();

    expect(t.seconds()).toBe(15);
  });
});
