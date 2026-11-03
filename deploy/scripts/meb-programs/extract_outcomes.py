#!/usr/bin/env python3
"""
MEB öğretim programlarından (pdftotext -layout çıktısı) öğrenme çıktılarını çıkarır.

Kazanım her programda "ÖĞRENME ÇIKTILARI VE SÜREÇ BİLEŞENLERİ" bölümündedir: "KOD. açıklama" ve ardından a), b) maddeleri.
Kod biçimi programa göre değişir (MAT.5.1.1, PSK.2.1, TDE2.1, ARN.5.1.D, ENG.2.1.L1), bu yüzden kod tek başına aranmaz;
bölüm bulunur, satır başındaki kod bölümün içinde okunur. Bölüm dışındaki kodlar (değerlendirme, etkinlik atıfları) sayılmaz.

Kullanım: extract_outcomes.py <text-dizini> <manifest.json> <çıktı.json>
"""
import collections
import json
import re
import sys

LOWER = "a-zçğıöşü"
UPPER = "A-ZÇĞİÖŞÜ"
# Metin katmanında Türkçe harf bozulabilir: "HAYAT BILGISI", "REHBERLİI<".
SECTION_START = re.compile(r"(ÖĞRENME\s+ÇIKTILARI|^\s*ÖĞRENME\s*$|^\s*ÖĞRENME\s{2,}\S|^\s*ÇIKTILARI\s+VE\b|LEARNING\s+OUTCOMES)")
SECTION_END = re.compile(
    r"^(İÇERİK\s+ÇERÇEVESİ|ÖĞRENME\s+KANITLARI|ÖĞRENME\s*-\s*ÖĞRETME|Anahtar\s+Kavramlar|Genellemeler|CONTENT\s+FRAME|LEARNING\s+EVIDENCE|LEARNING\s*-\s*TEACHING|KEY\s+CONCEPTS)"
)
# Sol sütundaki bölüm etiketi aynı satıra düşer.
SECTION_LABEL = re.compile(
    r"^(ÖĞRENME\s+ÇIKTILARI(\s+VE)?(\s+SÜREÇ)?(\s+BİLEŞENLERİ)?|ÖĞRENME(?=\s|$)|ÇIKTILARI(\s+VE)?(?=\s|$)|SÜREÇ(?=\s|$)|VE\s+SÜREÇ\s+BİLEŞENLERİ|VE\s+SÜREÇ|SÜREÇ\s+BİLEŞENLERİ|BİLEŞENLERİ|LEARNING\s+OUTCOMES(\s+and\s+PROCESS\s+COMPONENTS)?|and\s+PROCESS\s+COMPONENTS)\b\s*"
)
# TDE2.1. | PSK.2.1. | MAT.5.1.1. | G.J.9.1.1. | ARN.5.1.D. | ENG.2.1.L1. | EBRU.I.1.1. | TADB.1.
CODE = rf"[{UPPER}]{{1,8}}\d{{0,2}}(?:\.[{UPPER}0-9]{{1,5}}){{1,5}}"
OUTCOME = re.compile(rf"^({CODE})\.\s+(?:[–—]\s+)?(\S.*)$")
# Sonunda nokta olmayan (MARP11.3.1 “İbadetler” ...) ve boşluklu (KAK-I 1.1. ..., TKMT 3.1. ...) kodlar.
OUTCOME_NO_DOT = re.compile(rf"^({CODE})\s+([{UPPER}“\"'].*)$")
# Kod kendi satırında, açıklama alt satırda (Türk İşaret Dili): "TİD1.2.1."
CODE_ONLY = re.compile(rf"^({CODE})\.$")
OUTCOME_SPACED = re.compile(rf"^([{UPPER}]{{2,8}}(?:-[IVX]+)?)\s(\d{{1,2}}(?:\.\d{{1,2}}){{1,3}})\.?\s+(\S.*)$")
# Arapça: "ARP.5.3.1. DİNLEME/İZLEME" başlığı altında "5.3.1.1. Aile üyeleri ... yapabilme".
NUMBERED = re.compile(r"^(\d{1,2}(?:\.\d{1,2}){2,3})\.?\s+(\S.*)$")
BULLET = re.compile(rf"^(?:[{LOWER}]\)|[{LOWER}]\s*\.\s|[•\-–]\s|{CODE}\.[{LOWER}]\.)")
PAGE_NUMBER = re.compile(r"^\d{1,4}$")
TURKISH_ENDING = re.compile(r"(?:ebilme|abilme|bilme|ama|eme|ma|me)(?:si|sı)?$", re.IGNORECASE)
MAX_DESCRIPTION_LINES = 6
GRADE_HEADING = re.compile(r"^(\d{1,2})\.\s*SINIF\b")
PREPARATORY_HEADING = re.compile(r"^HAZIRLIK\s+SINIFI\b")
THEME_HEADING = re.compile(r"(\d{1,2})\s*\.\s*(?:TEMA|ÜNİTE|ÖĞRENME\s+ALANI)\s*:\s*(.+)$")
THEME_HEADING_FOREIGN = re.compile(r"\b(?:THEME|THEMA)\s+(\d{1,2})\s*[:.\-–]\s*(.+)$")
LABEL_LEAK = re.compile(r"Dersin\s+[Kk]odu|kazandırılması|VE\s+SÜREÇ|BİLEŞEN|ÇIKTILARI|Öğrenme\s+çıktı")
# Sonda beceri atfı: "... getirebilme (KB1)"
TRAILING_SKILL_REFERENCE = re.compile(rf"\s*\((?:[{UPPER}]+\d+(?:\.\d+)*(?:,\s*)?)+\)\s*$")
# Süslü ilk harf ayrı okunur: "T anışalım", "“T anışalım”"
SPLIT_INITIAL = re.compile(rf"(^|[“\"‘])([{UPPER.replace('O', '')}]) (?=[{LOWER}]{{2}})")
FRAME_START = re.compile(r"^İÇERİK\s+ÇERÇEVESİ\s*:?\s*")
MAX_SUBTOPIC_LENGTH = 90
# Aynı kod her temada ayrı metinle yazılıyorsa (TDE2.1) kod konuyu ayırmaz: program sınıf ve temayla nitelenir.
REPEATING_PROGRAM_SHARE = 0.5
MIN_DESCRIPTION_WORDS = 3
# Programların ortak beceri çerçevesi (E1.1 Merak, SDB3.1 Uyum, KB2.6 Bilgi Toplama): kazanım değil, kazanımların atıf yaptığı beceri.
FRAMEWORK_HEAD = re.compile(r"^(E|D|V|KB|SDB|OB|SELS|CS|LS|SAB|FBAB|MA|TAB|SBAB|DB|VOB|BOB|GOB|KOB|MOB|FOB|TOB|EOB|ÇOB|DOB)\d*$")
# Program ailesinin kod biçimi: tablo başlığından yakalanan azınlık biçimler (MAT.6.6 "Veriden") kazanım değildir.
MIN_FAMILY_SHARE = 0.15
# Kitap sırtının sayfa düzenine karışan numaraları: "events, and   832   activities".
SPINE_NUMBER = re.compile(r"\s{2,}\d{3}(?:\s*/\s*\d+(?:\s*-\s*\d+)?)?\s{2,}")
# Birden çok sınıfın ortak kaydı: "T.K.5.22. / T.K.6.22. / T.K.7.22. Açıklama"
SHARED_CODES = re.compile(rf"^(?:/\s*{CODE}\.\s*)+")
# "Doğru okuyabilme Doğru okuyabilme becerisi ...": kazanım cümlesi biter, açıklaması aynı satırda başlar.
SENTENCE_END = re.compile(rf"\b([{LOWER}]*(?:ebilme|abilme))\s+(?=[{UPPER}])")


CONTROL = re.compile(r"[\x00-\x08\x0b-\x1f]")
GLUED_SPINE_START = re.compile(r"^\d{3,4}(?:\s*-\s*\d)?(?=\s*[^\d.\s])\s*")
GLUED_SPINE_END = re.compile(r"(?<=[^\d\s.])\d{3,4}(?:\s*-\s*\d)?$")


def clean_line(raw):
    """Sütun boşluğunu teke indirir, sol sütun etiketini atar."""
    line = CONTROL.sub("", raw.replace("\u00ad", ""))
    line = SPINE_NUMBER.sub("  ", line)
    line = re.sub(r"\s{2,}", "  ", line).strip()
    line = GLUED_SPINE_START.sub("", line)
    line = GLUED_SPINE_END.sub("", line)
    # Etiket ve yazım artığı koda yapışır: "VEMARP11.1.1", "EENG.5.1.L1".
    line = re.sub(r"^VE(?=MARP)|^E(?=ENG\.)", "", line)
    # Etiket metne yapışabilir: "ÖĞRENME ÇIKTILARIEBRU.I.3.1.", "VE SÜREÇ BİLEŞENLERİa)".
    line = re.sub(r"(ÇIKTILARI|BİLEŞENLERİ)(?=\S)", r"\1 ", line)
    # Etiket parçaları ardışık gelebilir: "SÜREÇ BİLEŞENLERİ Öğretmeni ..."
    while True:
        stripped = SECTION_LABEL.sub("", line).strip()
        if stripped == line:
            return line
        line = stripped


def match_outcome(line):
    """(kod, açıklama) ya da None. Boşluklu kod noktayla yazılır: KAK-I 1.1 -> KAK-I.1.1."""
    hit = OUTCOME.match(line) or OUTCOME_NO_DOT.match(line)
    if hit:
        return hit.group(1), hit.group(2)
    hit = OUTCOME_SPACED.match(line)
    return (f"{hit.group(1)}.{hit.group(2)}", hit.group(3)) if hit else None


def right_column(line):
    """İki sütunlu satırda kod sağda kalır: 'Sınıf düzeyi  ARN.5.1.D. Ben ve Sen'."""
    parts = line.split("  ")
    for index, part in enumerate(parts):
        if match_outcome(part.strip()):
            return "  ".join(parts[index:]).strip()
    return line


def join_wrapped(parts):
    """Satır sonu tirelerini birleştirir: 'geliş-' + 'imin' -> 'gelişimin'."""
    text = ""
    for part in parts:
        part = re.sub(r"\s+", " ", part).strip()
        if text.endswith("-") and part[:1].islower():
            text = text[:-1] + part
        elif text:
            text = f"{text} {part}"
        else:
            text = part
    return text.strip()


def tidy_description(description):
    """Kazanım cümlesinden sonra aynı satırda başlayan açıklamayı atar."""
    hit = SENTENCE_END.search(description)
    description = (description[: hit.end(1)] if hit else description).rstrip(" .;:")
    description = TRAILING_SKILL_REFERENCE.sub("", description).rstrip(" .;:")
    return SPLIT_INITIAL.sub(r"\1\2", description)


def is_well_formed(description):
    return bool(TURKISH_ENDING.search(description.rstrip(" .;:")))


def is_clean(description):
    return is_well_formed(description) and not LABEL_LEAK.search(description)


def extract(text, preschool=False):
    """Bölüm başlığı olmayan program (Almanca) tüm metinde okunur; kod biçimi süzgeci atıfları eler."""
    return read_outcomes(text, False, preschool) or read_outcomes(text, True, preschool)


def read_outcomes(text, always_in_section, preschool):
    """
    Bir programın kayıtları. Aynı kod birkaç kez geçer: "kod nasıl okunur" sayfasında metin komşu sütuna taşar,
    bu yüzden ilk geçiş değil, biçimi sağlam ilk geçiş esas alınır.
    """
    outcomes = {}
    occurrences = collections.defaultdict(list)
    in_section = always_in_section
    current = None
    parent_head = None
    grade = None
    theme = None
    theme_wraps = False
    in_frame = False
    frames = collections.defaultdict(list)
    lines = text.splitlines()

    def flush():
        nonlocal current
        if current:
            description = join_wrapped(current["parts"])
            shared = SHARED_CODES.match(description)
            codes = [current["code"]]
            if shared:
                codes += re.findall(CODE, shared.group(0))
                description = description[shared.end():]
            description = tidy_description(description)
            for code in codes:
                row = {"code": code, "description": description, "line": current["line"], "grade": current["grade"], "theme": current["theme"]}
                if description:
                    occurrences[code].append(row)
                known = outcomes.get(code)
                if description and (known is None or (not is_clean(known["description"]) and is_clean(description))):
                    outcomes[code] = row
        current = None

    for number, raw in enumerate(lines, 1):
        stripped = re.sub(r"\s+", " ", raw).strip()
        if SECTION_START.search(raw):
            in_section = True
            in_frame = False
        line = right_column(clean_line(raw))

        heading = GRADE_HEADING.match(stripped)
        if heading:
            grade = int(heading.group(1))
        elif PREPARATORY_HEADING.match(stripped):
            grade = "Hazırlık"
        hit_theme = THEME_HEADING.search(stripped) or THEME_HEADING_FOREIGN.search(stripped)
        if hit_theme:
            theme = {"number": int(hit_theme.group(1)), "name": clean_theme(hit_theme.group(2))}
            theme_wraps = True
        elif theme_wraps:
            # Tema adı alt satıra taşabilir: "1. ÜNİTE: GÖKYÜZÜNDEKİ" + "KOMŞUMUZ: AY"
            if stripped and stripped.upper() == stripped and len(stripped) < 60 and not re.search(r"\d|SINIF|PROGRAM", stripped):
                theme = {**theme, "name": clean_theme(f"{theme['name']} {stripped}")}
            theme_wraps = False

        frame = FRAME_START.match(line)
        if frame:
            flush()
            in_section = always_in_section
            in_frame = True
            line = line[frame.end():]
        if in_frame:
            if SECTION_END.match(line) or match_outcome(line):
                in_frame = False
            else:
                add_frame_line(frames, grade, theme, line)
                continue
        if not in_section:
            continue
        if SECTION_END.match(line):
            flush()
            in_section = always_in_section
            continue

        hit = match_outcome(line) if not BULLET.match(line) else None
        if hit:
            flush()
            code, description = hit
            parent_head = code.split(".")[0]
            # Tamamı büyük harfli metin kazanım değil, altındaki numaralı kazanımların başlığıdır.
            if description.upper() != description:
                current = {"code": code, "parts": [description], "line": number, "grade": grade, "theme": theme}
            continue
        alone = CODE_ONLY.match(line)
        if alone and not always_in_section:
            flush()
            parent_head = alone.group(1).split(".")[0]
            current = {"code": alone.group(1), "parts": [], "line": number, "grade": grade, "theme": theme}
            continue
        numbered = NUMBERED.match(line)
        if numbered and parent_head:
            flush()
            current = {"code": f"{parent_head}.{numbered.group(1)}", "parts": [numbered.group(2)], "line": number, "grade": grade, "theme": theme}
            continue

        if current is None:
            continue
        # Açıklama ilk madde, boş satır, sayfa numarası ya da sınırda biter. Yalnız kod okunduysa boş satır atlanır.
        if not line and not current["parts"]:
            continue
        # pdftotext satır kaymasında araya boş satır koyar: cümle bitmediyse (gelişi- / min) açıklama sürer.
        unfinished = current["parts"] and not is_well_formed(join_wrapped(current["parts"])) and not re.search(r"[.!?]$", current["parts"][-1])
        if (not line or PAGE_NUMBER.match(line)) and unfinished and len(current["parts"]) < MAX_DESCRIPTION_LINES:
            continue
        if not line or PAGE_NUMBER.match(line) or BULLET.match(line) or len(current["parts"]) >= MAX_DESCRIPTION_LINES:
            flush()
            continue
        current["parts"].append(line)
    flush()
    rows = filter_noise(list(outcomes.values()), preschool)
    rows = expand_repeating_codes(rows, occurrences)
    return {"rows": rows, "frames": [{"grade": g, "themeNumber": t[0], "theme": t[1], "items": items} for (g, t), items in frames.items()]} if rows else None


def expand_repeating_codes(rows, occurrences):
    """Kodu temadan bağımsız programda her tema ayrı kayıttır; resmî kod sınıf ve tema ile nitelenir: TDE2.1 -> TDE.9.1.2.1"""
    if not rows:
        return rows
    def contexts(code):
        # Yalnız metin farkı sayılır: metni aynı kalan tekrar (kopyalanmış tema sayfası) ayrı kayıt değildir.
        return {o["description"] for o in occurrences[code] if is_clean(o["description"])}
    repeating = [r for r in rows if len(contexts(r["code"])) > 1]
    if len(repeating) < REPEATING_PROGRAM_SHARE * len(rows):
        return rows
    expanded = {}
    for row in rows:
        head, *rest = row["code"].split(".")
        letters = re.sub(r"\d+$", "", head)
        digits = head[len(letters):]
        for item in occurrences[row["code"]]:
            if not item["grade"] or not item["theme"]:
                continue
            code = ".".join([letters, str(item["grade"]), str(item["theme"]["number"]), *([digits] if digits else []), *rest])
            expanded.setdefault(code, {**item, "code": code, "officialCode": row["code"]})
    return list(expanded.values()) or rows


def clean_theme(name):
    name = re.sub(r"\s*\(\d+\)\s*$", "", name)
    name = re.sub(r"\s+\d{1,3}$", "", name)
    return re.sub(r"\s+", " ", name).strip(" .:-")


def add_frame_line(frames, grade, theme, line):
    """İçerik çerçevesi konunun alt konularıdır. Alt satıra taşan metin küçük harfle başlar; program başlığı ve sayfa numarası atılır."""
    if not theme:
        return
    text = re.sub(r"\s+", " ", line).strip(" •–-\t")
    if not text or PAGE_NUMBER.match(text) or "ÖĞRETİM PROGRAMI" in text or text.upper() == text and len(text) > 30:
        return
    items = frames[(grade, (theme["number"], theme["name"]))]
    if text[:1].islower() and items:
        items[-1] = join_wrapped([items[-1], text])
    else:
        items.append(text)


def filter_noise(rows, preschool=False):
    """Beceri çerçevesi kodlarını, tek kelimelik satırları ve programın kod biçimine uymayanları eler."""
    # "Sözlü sunum yapabilme" gibi kısa kazanım gerçektir; tablo başlığı ("Oyunun") ise cümle değildir.
    rows = [r for r in rows if len(r["description"].split()) >= MIN_DESCRIPTION_WORDS or (len(r["description"].split()) == 2 and is_well_formed(r["description"]))]
    if preschool:
        # Okul öncesi kazanımı TADB.1, SAB.3 biçimindedir (SAB burada beceri çerçevesi değil); FAB.4.4.1 beceri başlığıdır.
        return [r for r in rows if re.fullmatch(rf"[{UPPER}]+\.\d+", r["code"])]
    # Okuma Becerileri dersinin kodu da OB: yalnız beceri koduna benzeyen program kendi koduyla kalır.
    own = [r for r in rows if not FRAMEWORK_HEAD.match(r["code"].split(".")[0])]
    if own:
        rows = own
    else:
        heads = collections.Counter(r["code"].split(".")[0] for r in rows)
        rows = [r for r in rows if heads and r["code"].split(".")[0] == heads.most_common(1)[0][0]]
    families = collections.Counter(code_family(r["code"]) for r in rows)
    # Azınlık biçim gerçek olabilir (Hazırlık: MAT.H.1.1): programın kendi önekini taşıyan ve cümlesi tam olan kalır.
    heads = collections.Counter(r["code"].split(".")[0] for r in rows)
    main_head = heads.most_common(1)[0][0] if heads else None
    return [r for r in rows if families[code_family(r["code"])] >= MIN_FAMILY_SHARE * len(rows) or (r["code"].split(".")[0] == main_head and is_clean(r["description"]))]


def code_family(code):
    """Kodun iskeleti: harfler X, sayılar 9, Romen rakamı R. Program ailesini ve sınıfın kodda olup olmadığını gösterir."""
    head, *rest = code.split(".")
    shape = ["X" + ("9" if re.search(r"\d", head) else "")]
    for part in rest:
        if part.isdigit():
            shape.append("9")
        elif re.fullmatch(r"[IVX]+", part):
            shape.append("R")
        else:
            shape.append("A")
    return ".".join(shape)


def main(text_dir, manifest_path, out_path):
    manifest = json.load(open(manifest_path, encoding="utf-8"))
    programs = []
    for entry in manifest:
        pid = str(entry["pid"])
        text = open(f"{text_dir}/{pid}.txt", encoding="utf-8", errors="replace").read()
        result = extract(text, preschool="OKUL ÖNCESİ" in entry["title"]) or {"rows": [], "frames": []}
        rows = result["rows"]
        programs.append({
            "pid": entry["pid"],
            "title": entry["title"],
            "pages": entry["pages"],
            "families": collections.Counter(code_family(r["code"]) for r in rows).most_common(),
            "wellFormed": sum(1 for r in rows if is_well_formed(r["description"])),
            "outcomes": rows,
            "frames": result["frames"],
        })
    json.dump(programs, open(out_path, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    return programs


if __name__ == "__main__":
    main(*sys.argv[1:4])
