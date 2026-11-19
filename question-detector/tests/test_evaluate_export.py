import json
import os
import sys
import zipfile

sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(__file__)), "training"))
from evaluate_export import evaluate, iou, is_val_page, match, to_markdown, to_questions  # noqa: E402
from train import is_val  # noqa: E402


def test_iou_of_identical_disjoint_and_half_overlapping_boxes():
    assert iou((0, 0, 10, 10), (0, 0, 10, 10)) == 1.0
    assert iou((0, 0, 10, 10), (20, 20, 10, 10)) == 0.0
    assert round(iou((0, 0, 10, 10), (5, 0, 10, 10)), 4) == round(50 / 150, 4)


def test_match_pairs_each_box_once_and_prefers_the_best_overlap():
    truth = [(0, 0, 100, 100), (200, 0, 100, 100)]
    predicted = [(205, 0, 100, 100), (0, 0, 100, 100), (2, 2, 100, 100)]

    pairs = match(truth, predicted, 0.5)

    assert sorted((t, p) for t, p, _ in pairs) == [(0, 1), (1, 0)]
    assert match(truth, [(60, 0, 100, 100)], 0.5) == []


def test_to_questions_keeps_only_the_question_class():
    predictions = [
        {"class_id": 0, "x": 1, "y": 2, "width": 3, "height": 4, "subpredictions": [{"class_id": 0, "x": 1, "y": 3, "width": 1, "height": 1}]},
        {"class_id": 1, "x": 9, "y": 9, "width": 9, "height": 9},
    ]

    assert to_questions(predictions) == [{"box": (1, 2, 3, 4), "answers": [(1, 3, 1, 1)]}]


def _export(tmp_path, pages):
    path = tmp_path / "export.zip"
    with zipfile.ZipFile(path, "w") as zf:
        for page in pages:
            zf.writestr(page["file"], b"image")
        zf.writestr("manifest.json", json.dumps({"pages": pages}))
    return path


def test_evaluate_counts_matches_missed_and_extra_boxes_per_group(tmp_path):
    question = {"box": [0, 0, 100, 100], "answers": [[0, 50, 20, 10], [30, 50, 20, 10]]}
    other = {"box": [0, 200, 100, 100], "answers": []}
    path = _export(tmp_path, [
        {"pageId": "a", "file": "pages/a.webp", "source": "Manual", "bookName": "Kitap", "complete": True, "questions": [question, other]},
        {"pageId": "b", "file": "pages/b.webp", "source": "Ai", "bookName": "Kitap", "complete": True, "questions": [question]},
        {"pageId": "c", "file": "pages/c.webp", "source": "Ai", "bookName": "Kitap", "complete": False, "questions": [question]},
    ])
    calls = []

    def predict(_image):
        calls.append(1)
        # İlk sayfa: bir soru tam, biri kaçtı. İkinci sayfa: soru tam + fazladan bir kutu, şıklardan biri eksik.
        if len(calls) == 1:
            return [{"box": (0, 0, 100, 100), "answers": [(0, 50, 20, 10), (30, 50, 20, 10)]}]
        return [{"box": (0, 0, 100, 100), "answers": [(0, 50, 20, 10)]}, {"box": (500, 500, 50, 50), "answers": []}]

    result = evaluate(path, predict)
    everything = result["groups"]["all"]

    assert len(calls) == 2  # complete=false sayfa ölçülmez
    assert everything["pages"] == 2 and everything["questions"] == 3
    assert everything["missedQuestions"] == 1 and everything["extraQuestions"] == 1
    assert everything["questionRecall"]["0.75"] == round(2 / 3, 4)
    assert everything["countMatchPages"] == 0.0 and everything["perfectPages"] == 0.0
    assert everything["choiceRecall"]["0.5"] == 0.75 and everything["choiceCountMatch"] == 0.5
    assert result["groups"]["source:Manual"]["pages"] == 1
    assert "| all | 2 | 3 |" in to_markdown(result)


def test_acceptance_rule_passes_pages_with_the_right_counts_and_reports_the_wrong_ones(tmp_path):
    question = {"box": [0, 0, 100, 100], "answers": [[0, 50, 20, 10], [30, 50, 20, 10]]}
    pages = [
        {"pageId": name, "file": f"pages/{name}.webp", "complete": True, "questions": [question]} for name in ("a", "b", "c")
    ]
    path = _export(tmp_path, pages)
    answers = [
        # Kusursuz: kabul edilir, doğru.
        [{"box": (0, 0, 100, 100), "answers": [(0, 50, 20, 10), (30, 50, 20, 10)]}],
        # Sayılar tutuyor ama kutu kaymış: kabul edilir, yanlış.
        [{"box": (40, 0, 100, 100), "answers": [(0, 50, 20, 10), (30, 50, 20, 10)]}],
        # Şık sayısı tutmuyor: kabul edilmez (Claude'a gider).
        [{"box": (0, 0, 100, 100), "answers": [(0, 50, 20, 10)]}],
    ]

    result = evaluate(path, lambda _image: answers.pop(0), expected_choices=2)["groups"]["all"]

    assert result["acceptedPages"] == round(2 / 3, 4)
    assert result["acceptedWrongPages"] == 0.5


def test_val_only_uses_the_same_split_as_training(tmp_path):
    ids = [f"{n:08x}00000000" for n in range(40)]
    assert [is_val_page(i) for i in ids] == [is_val(i) for i in ids]
    path = _export(tmp_path, [{"pageId": i, "file": f"pages/{i}.webp", "complete": True, "questions": []} for i in ids])

    result = evaluate(path, lambda _image: [], only_pages={i for i in ids if is_val_page(i)})

    assert result["groups"]["all"]["pages"] == 4


def test_a_failing_page_is_counted_and_does_not_stop_the_run(tmp_path):
    path = _export(tmp_path, [{"pageId": "a", "file": "pages/a.webp", "complete": True, "questions": []}])

    def predict(_image):
        raise RuntimeError("down")

    result = evaluate(path, predict)

    assert result["failedPages"] == 1 and result["groups"] == {}
