#!/usr/bin/env python3
"""
extract_outcomes.py çıktısından, Soru Aktarımı ekranının "Katalog dosyası seç" adımıyla yüklenen paketi üretir (CatalogBundleDto).

Kurallar:
- Sınıf koddan (MAT.5.1.1, MARP11.1.1, ENG.PREP.1.L2), program başlığından ya da metindeki sınıf başlığından okunur. Hiçbiri yoksa tahmin yazılmaz, kayıt raporda kalır.
- Aynı kod birden çok programda: yılı yeni olan, eşitse genel program (çoklu yabancı dil modeli değil), eşitse çok kazanımlı olan kazanır.
- Özel eğitim kademeleri sınıf olarak eklenmez: ürün kararı bekliyor, raporda listelenir.

Kullanım: build_catalog.py <programs.json> <catalog.json> <report.json>
"""
import collections
import json
import re
import sys

CURRICULUM = "Maarif"
PREPARATORY = "Hazırlık"
PRESCHOOL = "Okul Öncesi"
TURKISH_FROM = "ÇçĞğİıÖöŞşÜüÂâÎîÛû"
TURKISH_TO = "ccggiioossuuaaiiuu"
SOURCE_REFERENCE_MAX = 300
NAME_MAX = 100
# Türkçe öğrenme çıktısı "-ebilme" gibi bitiş taşır ve kısadır: bundan sapan satır kesilmiş metin ya da beceri tanımıdır, pakete girmez.
FOREIGN_PROGRAMS = re.compile(r"İNGİLİZCE|ALMANCA")
# Ünlü uyumuna uymayan alıntı sözcükler onarılmaz; bilinen ilk hece kayıpları düzeltilir.
NOT_LOST_I = {"İNKILAP", "İNKILÂP"}
KNOWN_SPELLINGS = {"BILIŞIM": "BİLİŞİM", "BILIŞIMDE": "BİLİŞİMDE"}
ROMAN_WORD = re.compile(r"(?:I{1,3}|IV|V|VI|VII|VIII)")
# Program bütünüyle elenince yerine geçen sayılır: yarım program kalmasın.
OVERLAP_DROPS_PROGRAMME = 0.2
FINISHED_SENTENCE = re.compile(r"(?:me|ma|mek|mak)$")
SECTION_LEAK = re.compile(r"BİLEŞEN|ÇIKTILARI|Öğrenme çıktı|Dersin\s+[Kk]odu|kazandırılması|VE\s+SÜREÇ|^[a-zçğıöşü]\)")
TURKISH_DESCRIPTION_MAX = 300
LEVEL_PREFIXES = re.compile(
    r"^(ANADOLU İMAM HATİP LİSESİ|ORTAOKUL VE İMAM HATİP ORTAOKULU|ÇOKLU YABANCI DİL EĞİTİM MODELİ|ORTAÖĞRETİM|ORAÖĞRETİM|ORTAOKUL|İLKOKUL|GSL|SL)\s+"
)
SMALL_WORDS = {"VE": "ve", "İLE": "ile", "DE": "de", "DA": "da"}
# Metindeki sınıf başlığı programın kademesine uymalı: ortaokul programında "1. SINIF" İslam 1 düzeyidir, sınıf değil.
LEVEL_GRADES = (
    (re.compile(r"ORTAOKUL"), range(5, 9)),
    (re.compile(r"ORTAÖĞRETİM|ORAÖĞRETİM|LİSE|^GSL\b|^SL\b"), range(9, 13)),
    (re.compile(r"İLKOKUL"), range(1, 5)),
)
THEME_NOISE = re.compile(r"\s*(?:[A-ZÇĞİÖŞÜa-zçğıöşü]{1,8}(?:\.[A-ZÇĞİÖŞÜ0-9a-z]{1,5}){2,}\.?.*$|\([^)]*[\u0400-\u04FF\u0590-\u06FF][^)]*\)?.*$|[\u0400-\u04FF\u0590-\u06FF].*$)")
SUBTOPIC_NOISE = re.compile(r"^(?:\d+[.)]\s|Tema\b|Alt Tema|[a-zçğıöşü]\)\s)|DERS SAATİ|[:\"“”]\s*\S|[\u0400-\u04FF\u0590-\u06FF]")
# Dil programlarında içerik çerçevesi sözcük ve kalıp listesidir, alt konu değil.
NO_SUBTOPIC_PROGRAMS = re.compile(r"YAŞAYAN DİL|İNGİLİZCE|ALMANCA|ARAPÇA")


def slug(name):
    """CatalogCode.Slug ile aynı sonuç: kod iki tarafta da aynı kayda işaret etmeli."""
    out = []
    pending = False
    for raw in name:
        char = TURKISH_TO[TURKISH_FROM.index(raw)] if raw in TURKISH_FROM else raw.lower()
        if "a" <= char <= "z" or "0" <= char <= "9":
            if pending and out:
                out.append("-")
            pending = False
            out.append(char)
        else:
            pending = True
    return "".join(out)[:120].strip("-")


def turkish_lower(text):
    return text.replace("I", "ı").replace("İ", "i").lower()


def repair_lost_dotted_i(word):
    """
    PDF'te büyük İ, I olarak kalabilir: "GELİŞTİRIYORUM". Ünlü uyumuna göre ince ünlüden sonra gelen I, İ'dir.
    İlk hecedeki kayıp ("GIDIYORUM") bununla bulunmaz, adda kalır.
    """
    if word in NOT_LOST_I:
        return word
    if word in KNOWN_SPELLINGS:
        return KNOWN_SPELLINGS[word]
    out = []
    previous_vowel = ""
    for char in word:
        if char == "I" and previous_vowel in ("E", "İ", "Ö", "Ü"):
            char = "İ"
        if char in "AEIİOÖUÜ":
            previous_vowel = char
        out.append(char)
    return "".join(out)


def turkish_title(text):
    """SAYILAR VE NİCELİKLER -> Sayılar ve Nicelikler; KUR'AN-I KERİM -> Kur'an-ı Kerim; T.C. korunur."""
    words = []
    pieces = text.replace("’", "'").split()
    for index, word in enumerate(pieces):
        if index and word in SMALL_WORDS:
            words.append(SMALL_WORDS[word])
        elif ROMAN_WORD.fullmatch(word):
            words.append(word)
        elif re.fullmatch(r"(?:[A-ZÇĞİÖŞÜ]\.)+", word):
            words.append(word)
        else:
            # Yalnız sözcüğün ilk harfi büyük kalır: ayraç, tırnak ve kesme işaretinden sonra küçülür.
            # Romen eki yalnız adın sonunda ve sözcük Kur'an değilse: "ÜRETİMİ-II"; "KUR'AN-I KERİM" değil.
            roman = re.search(r"-(I{1,3}|IV|V)$", word) if index == len(pieces) - 1 and not word.startswith("KUR") else None
            lower = turkish_lower(repair_lost_dotted_i(word[: roman.start()] if roman else word))
            for position, char in enumerate(lower):
                if char.isalpha():
                    upper = "İ" if char == "i" else "I" if char == "ı" else char.upper()
                    lower = lower[:position] + upper + lower[position + 1:]
                    break
            # Romen eki büyük kalır: "ÜRETİMİ-II" -> "Üretimi-II"
            words.append(lower + (roman.group(0) if roman else ""))
    return " ".join(words)


def title_grades(title):
    """Başlıktaki sınıflar: (5-8), (Hazırlık, 9-12), (10. Sınıf), (11 ve 12. Sınıflar). Kademe ve Romen düzeyi sınıf değildir."""
    grades = []
    if "YAŞAYAN DİL" in title:
        return list(range(5, 9))
    for group in re.findall(r"\(([^)]*)\)", title):
        # "(İslam 1-2)" sınıf değil düzeydir.
        if re.fullmatch(r"\d{4}", group.strip()) or "İslam" in group:
            continue
        if "Hazırlık" in group:
            grades.append(PREPARATORY)
        for start, end in re.findall(r"(\d{1,2})\s*-\s*(\d{1,2})", group):
            grades += list(range(int(start), int(end) + 1))
        if not re.search(r"\d\s*-\s*\d", group):
            grades += [int(n) for n in re.findall(r"\b(\d{1,2})\b", group)]
    return grades


def subject_name(title):
    name = re.sub(r"\s+", " ", title).strip()
    # Sınıf, kademe ve yıl ayracı atılır; "(ARNAVUTÇA)", "(TÜRK MÜZİĞİ)" dersin adındandır.
    name = re.sub(r"\((?:[^)]*\d[^)]*|[^)]*Kademe[^)]*|[IVX, ]+|Hazırlık[^)]*)\)", "", name)
    name = LEVEL_PREFIXES.sub("", name.strip())
    name = re.sub(r"\s+ÖĞRETİM PROGRAMI\b", "", name)
    name = re.sub(r"\bDERSİ\b", "", name)
    return turkish_title(re.sub(r"\s+", " ", name).strip())


def theme_name(raw, foreign=False):
    """Başlığa taşan kod ve Arapça / Kiril yazımı atılır; karışık yazılmış ad olduğu gibi kalır."""
    if foreign:
        return THEME_NOISE.sub("", raw).strip(" .:-").title()[:NAME_MAX]
    name = re.sub(r"\d{3,4}(?:\s*-\s*\d)?$", "", THEME_NOISE.sub("", raw)).strip(" .:-")
    all_caps = re.sub(r"\([^)]*\)", "", name)
    if all_caps.upper() != all_caps:
        return re.sub(r"\s+İle\s+", " ile ", name.replace("’", "'"))[:NAME_MAX]
    return turkish_title(name)[:NAME_MAX]


def program_year(title):
    years = re.findall(r"\((\d{4})\)", title)
    return int(years[-1]) if years else 0


def code_grade(code, allowed):
    """
    Koddaki sınıf: ilk sayısal parça ya da başa yapışık sayı, başlıktaki aralıktaysa. Romen düzey (SMTHMK.IV.5.1) sınıf değildir.
    İki sayılı kod (KSE.5.1) tema + sıradır, sınıf söylemez; T.K.5.21 ve ABZ.5.1.K harfli parça taşıdığı için söyler.
    """
    parts = code.split(".")
    if any(ROMAN_WORD.fullmatch(part) for part in parts[1:]):
        return None
    if any(part in ("PREP", "HAZ", "H") for part in parts[1:3]) and PREPARATORY in allowed:
        return PREPARATORY
    glued = re.search(r"(\d+)$", parts[0])
    if glued and int(glued.group(1)) in allowed:
        return int(glued.group(1))
    numbers = [part for part in parts[1:] if part.isdigit()]
    has_letters = any(not part.isdigit() for part in parts[1:])
    if not numbers or (len(numbers) < 3 and not has_letters):
        return None
    return int(numbers[0]) if int(numbers[0]) in allowed else None


def resolve_grade(program, row, allowed):
    if "Okul Öncesi" in program["title"] or "OKUL ÖNCESİ" in program["title"]:
        return PRESCHOOL
    level = next((list(grades) for pattern, grades in LEVEL_GRADES if pattern.search(program["title"])), [])
    grade = code_grade(row["code"], allowed or level)
    if grade is not None:
        return grade
    if len(allowed) == 1:
        return allowed[0]
    if any(ROMAN_WORD.fullmatch(part) for part in row["code"].split(".")[1:]):
        return None
    context = row.get("grade")
    if context is None:
        return None
    # Çok sınıflı programda bütün kayıtlar tek başlığın altındaysa o başlık "kod nasıl okunur" sayfasından kalmıştır.
    contexts = {r.get("grade") for r in program["outcomes"]} - {None}
    if len(allowed) > 1 and len(contexts) == 1:
        return None
    if allowed:
        return context if context in allowed else None
    for pattern, grades in LEVEL_GRADES:
        if pattern.search(program["title"]):
            return context if context in grades else None
    return context


def is_suspect(title, description):
    if SECTION_LEAK.search(description):
        return True
    if FOREIGN_PROGRAMS.search(title):
        return False
    return not FINISHED_SENTENCE.search(description) or len(description) > TURKISH_DESCRIPTION_MAX


def grade_label(grade):
    return grade if isinstance(grade, str) else f"{grade}. Sınıf"


def priority(program):
    """Büyük olan kazanır."""
    title = program["title"]
    general = "ÇOKLU" not in title and "İMAM HATİP" not in title
    return (program_year(title), general, len(program["outcomes"]), program["pid"])


def main(programs_path, catalog_path, report_path):
    programs = json.load(open(programs_path, encoding="utf-8"))
    grades, subjects = {}, {}
    pairs = set()
    topics = {}
    outcomes = {}
    report = {"specialEducation": [], "noGrade": collections.defaultdict(lambda: {"title": "", "count": 0, "sample": ""}), "replaced": collections.Counter(), "replacedAcrossSubjects": [], "suspect": [], "droppedProgrammes": []}
    newest_by_subject = {}

    for program in sorted(programs, key=priority, reverse=True):
        title = program["title"]
        if re.search(r"Kademe", title, re.IGNORECASE):
            report["specialEducation"].append({"pid": program["pid"], "title": title, "outcomes": len(program["outcomes"])})
            continue
        allowed = title_grades(title)
        subject = subject_name(title)[:NAME_MAX]
        # Aynı ders ve aynı sınıflar için eski program yenisinin yerini tutmaz.
        newer = newest_by_subject.setdefault((subject, tuple(allowed)), program)
        if newer is not program and program_year(title) < program_year(newer["title"]):
            report["droppedProgrammes"].append({"pid": program["pid"], "title": title, "outcomes": len(program["outcomes"]), "reason": f"older than {newer['pid']}"})
            continue
        # Kodları büyük ölçüde başka programda olan program paralel sürümdür: kod tek kayıt tutar, program yarım kalmasın.
        clashes = [r for r in program["outcomes"] if r["code"] in outcomes and outcomes[r["code"]]["description"] != r["description"]]
        if program["outcomes"] and len(clashes) >= OVERLAP_DROPS_PROGRAMME * len(program["outcomes"]):
            winner = outcomes[clashes[0]["code"]]["_pid"]
            report["droppedProgrammes"].append({"pid": program["pid"], "title": title, "outcomes": len(program["outcomes"]), "reason": f"{len(clashes)} codes belong to {winner} with other text"})
            continue
        subject_code = slug(subject)
        reference = f"T.C. MEB TTKB, {re.sub(r'\s+', ' ', title)} Öğretim Programı, Türkiye Yüzyılı Maarif Modeli. mufredat.meb.gov.tr PID={program['pid']}"[:SOURCE_REFERENCE_MAX]
        for row in program["outcomes"]:
            if is_suspect(title, row["description"]):
                report["suspect"].append({"pid": program["pid"], "code": row["code"], "description": row["description"][:160]})
                continue
            grade = resolve_grade(program, row, allowed)
            if grade is None:
                entry = report["noGrade"][program["pid"]]
                entry.update(title=title, sample=f"{row['code']} {row['description'][:70]}")
                entry["count"] += 1
                continue
            label = grade_label(grade)
            grade_code = slug(label)
            known = outcomes.get(row["code"])
            if known:
                if known["subject"] == subject_code:
                    report["replaced"][f"{known['_pid']} > {program['pid']}"] += 1
                else:
                    report["replacedAcrossSubjects"].append({"code": row["code"], "kept": known["_pid"], "dropped": program["pid"]})
                continue
            grades[grade_code] = label
            subjects[subject_code] = subject
            pairs.add((grade_code, subject_code))
            outcomes[row["code"]] = {
                "curriculumVersion": CURRICULUM,
                "code": row["code"],
                "description": row["description"],
                "grade": grade_code,
                "subject": subject_code,
                "subTopic": None,
                "sourceReference": reference,
                "_pid": program["pid"],
            }
            theme = row.get("theme")
            if theme:
                name = theme_name(theme["name"], bool(FOREIGN_PROGRAMS.search(title)))
                code = f"{grade_code}.{subject_code}.{slug(name)}"
                if slug(name) and not name.endswith("/"):
                    topics.setdefault(code, {"code": code, "name": name, "grade": grade_code, "subject": subject_code, "subTopics": {}})
        if NO_SUBTOPIC_PROGRAMS.search(title):
            continue
        for frame in program.get("frames", []):
            grade = frame["grade"] if frame["grade"] in allowed or not allowed else (allowed[0] if len(allowed) == 1 else None)
            if grade is None:
                continue
            grade_code = slug(grade_label(grade))
            topic = topics.get(f"{grade_code}.{subject_code}.{slug(theme_name(frame['theme']))}")
            if not topic:
                continue
            for item in frame["items"]:
                name = re.sub(r"\s+İle\s+", " ile ", item.rstrip(":").strip().replace("’", "'"))
                if 3 <= len(name) <= 90 and name.upper() != name and not name.endswith(",") and not name[:1].islower() and not SUBTOPIC_NOISE.search(name):
                    topic["subTopics"].setdefault(f"{topic['code']}.{slug(name)}", name)

    bundle = {
        "formatVersion": 1,
        "grades": [{"code": code, "name": name} for code, name in sorted(grades.items())],
        "subjects": [{"code": code, "name": name} for code, name in sorted(subjects.items())],
        "gradeSubjects": [{"grade": grade, "subject": subject} for grade, subject in sorted(pairs)],
        "topics": [
            {"code": t["code"], "name": t["name"], "grade": t["grade"], "subject": t["subject"], "subTopics": [{"code": c, "name": n} for c, n in t["subTopics"].items()]}
            for t in sorted(topics.values(), key=lambda t: t["code"])
        ],
        "learningOutcomes": [{k: v for k, v in o.items() if not k.startswith("_")} for o in sorted(outcomes.values(), key=lambda o: o["code"])],
    }
    json.dump(bundle, open(catalog_path, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    report["noGrade"] = {str(pid): entry for pid, entry in sorted(report["noGrade"].items())}
    report["replaced"] = dict(report["replaced"].most_common())
    report["replacedAcrossSubjects"] = report["replacedAcrossSubjects"]
    json.dump(report, open(report_path, "w", encoding="utf-8"), ensure_ascii=False, indent=1)


if __name__ == "__main__":
    main(*sys.argv[1:4])
