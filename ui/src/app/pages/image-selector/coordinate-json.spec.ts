import { ClassificationSource } from '../../models/draws';
import {
  COORDINATE_BOX_BOTTOM_PADDING,
  buildRegionsFromCoordinates,
  isCoordinateJsonFile,
  parseCoordinateJson,
  selectCoordinateImageFiles,
} from './coordinate-json';

function entry(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    filename: 'page_12.webp',
    soru_no: '1',
    ders: { id: '144', ad: 'Matematik' },
    konu: { id: '261', ad: 'Doğal Sayılar' },
    alt_konular: [
      { id: '809', ad: 'Milyonlar' },
      { id: '811', ad: 'Çözümleme' },
    ],
    x: 261,
    y: 407,
    width: 1174,
    height: 142,
    siklar: {
      A: { rel_x: 61, rel_y: 257, width: 734, height: 53 },
      B: { rel_x: 61, rel_y: 334, width: 829, height: 49 },
      C: { rel_x: 61, rel_y: 403, width: 793, height: 53 },
      D: { rel_x: 61, rel_y: 476, width: 750, height: 53 },
    },
    ...overrides,
  };
}

function image(name: string): File {
  return new File(['x'], name, { type: 'image/webp' });
}

describe('coordinate-json', () => {
  describe('isCoordinateJsonFile', () => {
    it('isCoordinateJsonFile_CaseInsensitiveName_ReturnsTrue', () => {
      expect(isCoordinateJsonFile({ name: 'Question-Coordinates.JSON' })).toBeTrue();
      expect(isCoordinateJsonFile({ name: 'other.json' })).toBeFalse();
    });
  });

  describe('parseCoordinateJson', () => {
    it('parseCoordinateJson_InvalidJson_FlagsInvalid', () => {
      const result = parseCoordinateJson('{not json');
      expect(result.invalidJson).toBeTrue();
      expect(result.validCount).toBe(0);
    });

    it('parseCoordinateJson_RootNotArray_FlagsInvalid', () => {
      expect(parseCoordinateJson('{"filename":"a.webp"}').invalidJson).toBeTrue();
    });

    it('parseCoordinateJson_BadEntries_SkipsThemAndKeepsValidOnes', () => {
      const data = [
        entry(),
        entry({ x: -5 }),
        entry({ width: 'abc' }),
        entry({ filename: 42 }),
        entry({ siklar: { A: { rel_x: 1, rel_y: Number.NaN, width: 2, height: 2 } } }),
        entry({ siklar: { A: { rel_x: -300, rel_y: 1, width: 2, height: 2 } } }), // sayfanın soluna düşer
        'not an object',
        null,
      ];
      // NaN JSON'a yazılamaz; null'a dönüşür ve yine geçersiz sayılır.
      const result = parseCoordinateJson(JSON.stringify(data));

      expect(result.invalidJson).toBeFalse();
      expect(result.validCount).toBe(1);
      expect(result.skippedCount).toBe(7);
    });

    it('parseCoordinateJson_StringIds_ConvertedToNumbers', () => {
      const result = parseCoordinateJson(JSON.stringify([entry()]));
      const q = result.pages.get('page_12.webp')![0];

      expect(q.subjectId).toBe(144);
      expect(q.topicId).toBe(261);
      expect(q.subTopicIds).toEqual([809, 811]);
    });

    it('parseCoordinateJson_InvalidClassificationIds_KeepsEntryWithoutThem', () => {
      const result = parseCoordinateJson(
        JSON.stringify([entry({ ders: { id: 'x' }, konu: null, alt_konular: [{ id: '0' }, { id: '12' }] })])
      );
      const q = result.pages.get('page_12.webp')![0];

      expect(result.validCount).toBe(1);
      expect(q.subjectId).toBeNull();
      expect(q.topicId).toBeNull();
      expect(q.subTopicIds).toEqual([12]);
    });

    it('parseCoordinateJson_SoruNo_SortedNaturallyPerPage', () => {
      const result = parseCoordinateJson(
        JSON.stringify([entry({ soru_no: '10' }), entry({ soru_no: '2' }), entry({ soru_no: '1' })])
      );

      expect(result.pages.get('page_12.webp')!.map((q) => q.soruNo)).toEqual(['1', '2', '10']);
    });

    it('parseCoordinateJson_OptionsOutOfOrder_SortedAlphabetically', () => {
      const result = parseCoordinateJson(
        JSON.stringify([
          entry({
            siklar: {
              c: { rel_x: 0, rel_y: 30, width: 10, height: 10 },
              A: { rel_x: 0, rel_y: 10, width: 10, height: 10 },
              B: { rel_x: 0, rel_y: 20, width: 10, height: 10 },
            },
          }),
        ])
      );

      expect(result.pages.get('page_12.webp')![0].options.map((o) => o.label)).toEqual(['A', 'B', 'C']);
    });
  });

  describe('selectCoordinateImageFiles', () => {
    it('selectCoordinateImageFiles_FiltersToJsonFiles_NaturalSortAndMissingList', () => {
      const parsed = parseCoordinateJson(
        JSON.stringify([
          entry({ filename: 'page_10.webp' }),
          entry({ filename: 'page_2.webp' }),
          entry({ filename: 'page_99.webp' }),
          entry({ filename: 'page_5.webp' }),
        ])
      );
      const files = [
        image('page_10.webp'),
        image('page_1.webp'), // JSON'da yok → atlanır
        image('page_2.webp'),
        new File(['{}'], 'question-coordinates.json', { type: 'application/json' }),
      ];

      const selection = selectCoordinateImageFiles(files, parsed.pages);

      expect(selection.files.map((f) => f.name)).toEqual(['page_2.webp', 'page_10.webp']);
      expect(selection.missing).toEqual(['page_5.webp', 'page_99.webp']);
    });
  });

  describe('buildRegionsFromCoordinates', () => {
    it('buildRegions_OptionsOutsideStem_AbsoluteCoordsAndExtendedBox', () => {
      const q = parseCoordinateJson(JSON.stringify([entry()])).pages.get('page_12.webp')!;

      const [region] = buildRegionsFromCoordinates(q);

      expect(region.answers.map((a) => [a.label, a.x, a.y, a.width, a.height])).toEqual([
        ['A', 322, 664, 734, 53],
        ['B', 322, 741, 829, 49],
        ['C', 322, 810, 793, 53],
        ['D', 322, 883, 750, 53],
      ]);
      expect(region.x).toBe(261);
      expect(region.y).toBe(407);
      expect(region.width).toBe(1174); // şıklar soru genişliğini aşmıyor
      expect(region.height).toBe(476 + 53 + COORDINATE_BOX_BOTTOM_PADDING);
      // Her şık soru kutusunun içinde kalır.
      for (const a of region.answers) {
        expect(a.y + a.height).toBeLessThanOrEqual(region.y + region.height);
        expect(a.x + a.width).toBeLessThanOrEqual(region.x + region.width);
      }
    });

    it('buildRegions_OptionsWiderThanStem_ExtendsWidthWithoutPadding', () => {
      const q = parseCoordinateJson(
        JSON.stringify([
          entry({ width: 100, height: 200, siklar: { A: { rel_x: 50, rel_y: 10, width: 120, height: 20 } } }),
        ])
      ).pages.get('page_12.webp')!;

      const [region] = buildRegionsFromCoordinates(q);

      expect(region.width).toBe(170);
      expect(region.height).toBe(200); // şık zaten içeride → uzatma/pay yok
    });

    it('buildRegions_ImageBounds_ClampsBoxUpToTheEdge', () => {
      const q = parseCoordinateJson(JSON.stringify([entry()])).pages.get('page_12.webp')!;

      const [region] = buildRegionsFromCoordinates(q, { width: 1300, height: 900 });

      expect(region.x + region.width).toBe(1300);
      expect(region.y + region.height).toBe(900);
    });

    it('buildRegions_NegativeRelOffsets_ExtendsBoxLeftAndUp', () => {
      const q = parseCoordinateJson(
        JSON.stringify([
          entry({
            x: 100,
            y: 200,
            width: 300,
            height: 100,
            siklar: {
              A: { rel_x: -40, rel_y: -30, width: 50, height: 20 },
              B: { rel_x: 10, rel_y: 40, width: 50, height: 20 },
            },
          }),
        ])
      );
      expect(q.validCount).toBe(1);

      const [region] = buildRegionsFromCoordinates(q.pages.get('page_12.webp')!);

      expect(region.answers[0]).toEqual(jasmine.objectContaining({ label: 'A', x: 60, y: 170 }));
      expect(region.x).toBe(60);
      expect(region.y).toBe(170);
      expect(region.x + region.width).toBe(400); // kök sağ kenarı
      expect(region.y + region.height).toBe(300); // şıklar kökün içinde → aşağı uzatma/pay yok
    });

    it('buildRegions_Defaults_NoCorrectAnswerNamedLikePredictAndAiClassification', () => {
      const q = parseCoordinateJson(
        JSON.stringify([entry({ soru_no: '2' }), entry({ soru_no: '1' })])
      ).pages.get('page_12.webp')!;

      const regions = buildRegionsFromCoordinates(q);

      expect(regions.map((r) => r.name)).toEqual(['Soru 1', 'Soru 2']);
      expect(regions.map((r) => r.id)).toEqual([0, 1]);
      expect(regions.every((r) => r.answers.every((a) => a.isCorrect === false))).toBeTrue();
      expect(regions[0].answers.map((a) => a.id)).toEqual([0, 1, 2, 3]);
      expect(regions[0]).toEqual(
        jasmine.objectContaining({
          isExample: false,
          exampleAnswer: null,
          passageId: '',
          imageId: '',
          imageUrl: '',
          subjectId: 144,
          topicId: 261,
          subtopicId: 809,
          subtopicIds: [809, 811],
          classificationSource: ClassificationSource.AI,
          classification: { subjectId: 144, topicId: 261, subTopicIds: [809, 811], source: 'AI' },
        })
      );
    });
  });
});
