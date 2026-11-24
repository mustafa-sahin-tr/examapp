using ExamApp.Api.Services.BookImport;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// 5. sınıf Sosyal Bilgiler soru bankası (Üçgen Akademi, 224 sayfa): kitap boyunca numaralı iki sayfalık testler, ünite yalnız sağ
/// sayfanın altında, ünite sonlarında deneme sınavları. Fikstür bu kitabın gerçek taraması (demo log) ve sayfa başına soru sayıları.
/// Eski planlayıcı sol sayfayı ünite 1 sayıp ~22 testi ikiye bölüyor, üçüncü sayfaları düşürüyor, Test 3'ün ikinci sayfasını
/// etkinlik bölümü yapıyor ve denemeleri hiç planlamıyordu.
/// </summary>
public class BookSetupPlannerSpreadTests
{
    // "n:tür [u ünite] [t test] [q en büyük soru no] [e deneme]"
    private const string Survey =
        """
        1:cover | 2:other | 3:other | 4:toc | 5:toc | 6:other | 7:other | 8:other
        9:cover | 10:test u1 t1 q6 | 11:test u1 t1 q8 | 12:test u1 t2 q6 | 13:test u1 t2 q8 | 14:test u1 t3 q5 | 15:activity u1 t3 q6 | 16:test u1 t4 q5
        17:test t4 q7 | 18:test t5 q4 | 19:test t5 q6 | 20:test t6 q2 | 21:test t6 q4 | 22:test t6 q6 | 23:test t6 q8 | 24:test t6 q10
        25:test t6 q11 | 26:test t7 q2 | 27:test t7 q4 | 28:test t7 q6 | 29:test t7 q8 | 30:test t7 q10 | 31:test t7 q12 | 32:test t7 q14
        33:cover | 34:test t8 q6 | 35:test t8 q8 | 36:test t9 q5 | 37:test t9 q7 | 38:test t10 q5 | 39:test t10 q7 | 40:test t11 q5
        41:test u2 t11 q7 | 42:test t12 q6 | 43:test u2 t12 q8 | 44:test t13 q5 | 45:test u2 t13 q7 | 46:test t14 q6 | 47:test u2 t14 q8 | 48:test t15 q6
        49:test u2 t15 q8 | 50:test t16 q5 | 51:test u2 t16 q7 | 52:test t17 q2 | 53:test u2 t17 q4 | 54:test t17 q6 | 55:test u2 t17 q8 | 56:test t17 q10
        57:test u2 t17 q12 | 58:test u2 t18 q2 | 59:test u2 t18 q4 | 60:test u2 t18 q6 | 61:test u2 t18 q7 | 62:test u2 t18 q9 | 63:test u2 t18 q11 | 64:test u2 t18 q13
        65:cover | 66:cover | 67:test q3 | 68:test q5 | 69:test q7 | 70:test q9 | 71:test q10 | 72:answerkey
        73:cover | 74:test u3 t19 q5 | 75:test u3 t19 q7 | 76:test u3 t20 q5 | 77:test u3 t20 q7 | 78:test u3 t21 q5 | 79:test u3 t21 q7 | 80:test u3 t22 q4
        81:test u3 t22 q6 | 82:test u3 t23 q5 | 83:test u3 t23 q7 | 84:test u3 t24 q4 | 85:test u3 t24 q6 | 86:test u3 t25 q5 | 87:test u3 t25 q6 | 88:test u3 t26 q5
        89:test t26 q6 | 90:test t27 q5 | 91:test t27 q7 | 92:test t27 q12 | 93:test t27 q14 | 94:test t27 q19 | 95:test t27 q20 | 96:test t28 q6
        97:test u3 t28 q8 | 98:test u3 t28 q14 | 99:test u3 t28 q16 | 100:test u3 t28 q21 | 101:test u3 t28 q23 | 102:test u3 t29 q6 | 103:test u3 t29 q8 | 104:test u3 t29 q13
        105:test u3 t29 q15 | 106:test u3 t29 q20 | 107:test u3 t29 q22 | 108:test u3 t29 q28 | 109:cover | 110:cover | 111:test q2 | 112:test q4
        113:test t2 q6 | 114:test t2 q8 | 115:test t2 q10 | 116:other | 117:cover | 118:test t30 q5 | 119:test u4 t30 q7 | 120:test t31 q6
        121:test u4 t31 q8 | 122:test u4 t32 q6 | 123:test u4 t32 q7 | 124:test u4 t33 q5 | 125:test u4 t33 q7 | 126:test u4 t34 q5 | 127:test u4 t34 q7 | 128:test u4 t35 q5
        129:test u4 t35 q7 | 130:test u4 t35 q13 | 131:test u4 t35 q15 | 132:test u4 t35 q20 | 133:test u4 t35 q21 | 134:test u4 t35 q27 | 135:cover | 136:test t36 q5
        137:test u5 t36 q7 | 138:test u5 t37 q4 | 139:test u5 t37 q6 | 140:test u5 t38 q5 | 141:test u5 t38 q7 | 142:test u5 t39 q5 | 143:test u5 t39 q7 | 144:test u5 t40 q5
        145:test u5 t40 q7 | 146:test u5 t41 q6 | 147:test u5 t41 q8 | 148:test u5 t42 q5 | 149:test u5 t42 q7 | 150:test u5 t43 q6 | 151:test u5 t43 q7 | 152:test u5 t44 q5
        153:test u5 t44 q7 | 154:test u5 t44 q12 | 155:test u5 t44 q14 | 156:test u5 t44 q19 | 157:test u5 t44 q20 | 158:test u5 t45 q5 | 159:test u5 t45 q7 | 160:test u5 t45 q13
        161:test u5 t45 q15 | 162:test t45 q21 | 163:test u5 t45 q23 | 164:test t45 q27 | 165:cover | 166:cover | 167:test q2 | 168:test q4
        169:test q6 | 170:test q8 | 171:test q10 | 172:other | 173:cover | 174:test u6 t46 q6 | 175:test u6 t46 q8 | 176:test t47 q5
        177:test u6 t47 q7 | 178:test t48 q5 | 179:test u6 t48 q7 | 180:test t49 q5 | 181:test u6 t49 q7 | 182:test t50 q6 | 183:test u6 t50 q8 | 184:test t50 q13
        185:test u6 t50 q15 | 186:test t50 q21 | 187:test u6 t50 q23 | 188:test t50 q29 | 189:cover | 190:test t51 q4 | 191:test u7 t51 q6 | 192:test t52 q4
        193:test u7 t52 q5 | 194:test u7 t53 q5 | 195:test u7 t53 q7 | 196:test u7 t54 q5 | 197:test u7 t54 q5 | 198:test u7 t55 q4 | 199:test u7 t55 q6 | 200:test u7 t56 q6
        201:test u7 t56 q7 | 202:test u7 t57 q5 | 203:test u7 t57 q7 | 204:test u7 t57 q11 | 205:test u7 t57 q12 | 206:test u7 t57 q17 | 207:test u7 t57 q19 | 208:test t58 q5
        209:test u7 t58 q7 | 210:test u7 t58 q12 | 211:test u7 t58 q14 | 212:test u7 t58 q19 | 213:test u7 t58 q21 | 214:test u7 t58 q26 | 215:cover | 216:other
        217:test q2 | 218:test q4 | 219:test q6 | 220:test q8 | 221:test q10 | 222:other | 223:answerkey | 224:answerkey
        """;

    // Anahtarın ilk okuması: "p sayfa u ünite t test n=soru sayısı" (u2 t1 = "1. Sınav", u3 t1 = "2. Sınav": eski okuma).
    private const string KeyRows =
        """
        p223 u1 t1 n=8 | p223 u1 t2 n=8 | p223 u1 t3 n=6 | p223 u1 t4 n=7 | p223 u1 t5 n=6 | p223 u1 t6 n=11
        p223 u1 t7 n=14 | p223 u2 t8 n=8 | p223 u2 t9 n=7 | p223 u2 t10 n=7 | p223 u2 t11 n=7 | p223 u2 t12 n=8
        p223 u2 t13 n=7 | p223 u2 t14 n=8 | p223 u2 t15 n=8 | p223 u2 t16 n=7 | p223 u2 t17 n=12 | p223 u2 t18 n=13
        p223 u2 t1 n=10 | p223 u3 t19 n=7 | p223 u3 t20 n=7 | p223 u3 t21 n=7 | p223 u3 t22 n=6 | p223 u3 t23 n=7
        p223 u3 t24 n=6 | p223 u3 t25 n=6 | p223 u3 t26 n=6 | p223 u3 t27 n=20 | p223 u3 t28 n=23 | p223 u3 t29 n=28
        p223 u3 t1 n=10 | p223 u4 t30 n=7 | p223 u4 t31 n=8 | p223 u4 t32 n=7 | p223 u4 t33 n=7 | p223 u4 t34 n=7
        p223 u4 t35 n=27 | p224 u5 t36 n=7 | p224 u5 t37 n=6 | p224 u5 t38 n=7 | p224 u5 t39 n=7 | p224 u5 t40 n=7
        p224 u5 t41 n=8 | p224 u5 t42 n=7 | p224 u5 t43 n=7 | p224 u5 t44 n=20 | p224 u5 t45 n=27 | p224 u6 t46 n=8
        p224 u6 t47 n=7 | p224 u6 t48 n=7 | p224 u6 t49 n=7 | p224 u6 t50 n=29 | p224 u7 t51 n=6 | p224 u7 t52 n=5
        p224 u7 t53 n=7 | p224 u7 t54 n=5 | p224 u7 t55 n=6 | p224 u7 t56 n=7 | p224 u7 t57 n=19 | p224 u7 t58 n=26
        """;

    private static List<SurveyedPage> Pages(Func<SurveyedPage, SurveyedPage>? map = null) =>
        Survey.Split(['|', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(item =>
        {
            var parts = item.Split(' ');
            var head = parts[0].Split(':');
            var kind = Enum.Parse<BookPageKind>(head[1], ignoreCase: true);
            int? Field(char c) => parts.Skip(1).FirstOrDefault(p => p[0] == c) is { } p ? int.Parse(p[1..]) : null;
            var page = new SurveyedPage(int.Parse(head[0]), kind, null, Field('u'), Field('t'), null, null, Field('q'));
            return map is null ? page : map(page);
        }).ToList();

    private static List<AnswerKeyRow> Keys() =>
        KeyRows.Split(['|', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(row =>
        {
            var c = row.Split(' ');
            var n = int.Parse(c[3][2..]);
            var answers = Enumerable.Range(1, n).ToDictionary(i => i, _ => 'A');
            return new AnswerKeyRow(int.Parse(c[1][1..]), int.Parse(c[2][1..]), answers, int.Parse(c[0][1..]));
        }).ToList();

    private static PlannedTest Test(BookSetupPlan plan, int testNo) =>
        plan.Tests.Single(t => t.TestNo == testNo && t.Flags.Count(f => f is BookSetupPlanner.FlagActivity or BookSetupPlanner.FlagExam) == 0);

    [Fact]
    public void Two_page_tests_stay_whole_and_take_the_unit_of_the_page_that_prints_it()
    {
        var keys = Keys();
        var plan = BookSetupPlanner.Plan(Pages(), [], keys, keys);

        var tests = plan.Tests.Where(t => t.Flags.Count(f => f is BookSetupPlanner.FlagActivity or BookSetupPlanner.FlagExam) == 0).ToList();
        tests.Select(t => t.TestNo).ShouldBe(Enumerable.Range(1, 58));
        tests.ShouldAllBe(t => t.AnswerKey != null && !t.Flags.Contains(BookSetupPlanner.FlagKeyMismatch));

        // Sol sayfa (ünitesiz) + sağ sayfa ("Ünite 2") tek test; üçüncü ve sonraki sayfalar kaybolmaz.
        Test(plan, 11).ShouldSatisfyAllConditions(t => t.Unit.ShouldBe(2), t => t.FileFrom.ShouldBe(40), t => t.FileTo.ShouldBe(41));
        Test(plan, 17).ShouldSatisfyAllConditions(t => t.FileFrom.ShouldBe(52), t => t.FileTo.ShouldBe(57), t => t.AnswerKey!.Length.ShouldBe(12));
        Test(plan, 45).ShouldSatisfyAllConditions(t => t.FileFrom.ShouldBe(158), t => t.FileTo.ShouldBe(164));
        Test(plan, 50).ShouldSatisfyAllConditions(t => t.FileFrom.ShouldBe(182), t => t.FileTo.ShouldBe(188));
        // Test 3'ün ikinci sayfası (bulmacalı ama şıklı 6. soru) etkinlik bölümü değil.
        Test(plan, 3).FileTo.ShouldBe(15);
        plan.Tests.ShouldNotContain(t => t.Flags.Contains(BookSetupPlanner.FlagActivity));
        // Hiçbir sayfasında ünite basılı olmayan Test 8–10: anahtarın ünitesi ("2. Ünite").
        new[] { 8, 9, 10 }.Select(n => Test(plan, n).Unit).ShouldBe([2, 2, 2]);
        plan.Units.Single(u => u.Unit == 1).FileTo.ShouldBe(32);
    }

    [Fact]
    public void Exam_sections_are_planned_with_their_keys_and_stay_out_of_unit_ranges()
    {
        int? Exam(int page) => page switch { >= 67 and <= 71 => 1, >= 111 and <= 115 => 2, >= 167 and <= 171 => 3, >= 217 and <= 221 => 4, _ => null };
        var pages = Pages(p => Exam(p.FilePage) is { } n ? p with { TestNo = null, ExamNo = n } : p);
        var ten = Enumerable.Range(1, 10).ToDictionary(i => i, _ => 'B');
        var keys = Keys().Where(k => k.TestNo != 1 || k.Unit == 1)
            .Concat(Enumerable.Range(1, 4).Select(n => new AnswerKeyRow(0, 0, ten, n <= 2 ? 223 : 224, ExamNo: n)))
            .ToList();

        var plan = BookSetupPlanner.Plan(pages, [], keys, keys);

        var exams = plan.Tests.Where(t => t.Flags.Contains(BookSetupPlanner.FlagExam)).ToList();
        exams.Select(t => (t.Unit, t.TestNo, t.FileFrom, t.FileTo)).ShouldBe([(2, 1, 67, 71), (3, 2, 111, 115), (5, 3, 167, 171), (7, 4, 217, 221)]);
        exams.ShouldAllBe(t => t.AnswerKey == "BBBBBBBBBB");
        plan.OrphanKeys.ShouldBeEmpty();
        plan.Tests.Count.ShouldBe(62);
        // Deneme ünite aralığını uzatmaz (2. ünite 2. Sınav'a, 7. ünite 4. Sınav'a uzanmaz).
        plan.Units.Single(u => u.Unit == 2).FileTo.ShouldBe(64);
        plan.Units.Single(u => u.Unit == 7).FileTo.ShouldBe(214);
    }

    [Fact]
    public void Exam_key_without_a_planned_exam_is_an_orphan_labelled_by_exam_number()
    {
        var ten = Enumerable.Range(1, 10).ToDictionary(i => i, _ => 'C');
        var keys = new List<AnswerKeyRow> { new(0, 0, ten, 223, ExamNo: 1) };

        var plan = BookSetupPlanner.Plan([new SurveyedPage(1, BookPageKind.Test, null, 1, 1, null, null, 4)], [], keys, keys);

        plan.OrphanKeys.ShouldBe(["S01"]);
        plan.OrphanRows!.Single().AnswerKey.ShouldBe("CCCCCCCCCC");
    }
}
