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
SECTION_START = re.compile(r"(ÖĞRENME\s+ÇIKTILARI|LEARNING\s+OUTCOMES|LERNERGEBNISSE|Lernergebnisse)")
SECTION_END = re.compile(
    r"^(İÇERİK\s+ÇERÇEVESİ|ÖĞRENME\s+KANITLARI|ÖĞRENME\s*-\s*ÖĞRETME|Anahtar\s+Kavramlar|Genellemeler|CONTENT\s+FRAME|LEARNING\s+EVIDENCE|LEARNING\s*-\s*TEACHING|KEY\s+CONCEPTS)"
)
# Sol sütundaki bölüm etiketi aynı satıra düşer.
SECTION_LABEL = re.compile(
    r"^(ÖĞRENME\s+ÇIKTILARI(\s+VE)?(\s+SÜREÇ)?(\s+BİLEŞENLERİ)?|VE\s+SÜREÇ\s+BİLEŞENLERİ|VE\s+SÜREÇ|SÜREÇ\s+BİLEŞENLERİ|BİLEŞENLERİ|LEARNING\s+OUTCOMES(\s+and\s+PROCESS\s+COMPONENTS)?|and\s+PROCESS\s+COMPONENTS)\b\s*"
)
# TDE2.1. | PSK.2.1. | MAT.5.1.1. | G.J.9.1.1. | ARN.5.1.D. | ENG.2.1.L1. | EBRU.I.1.1. | TADB.1.
CODE = rf"[{UPPER}]{{1,8}}\d{{0,2}}(?:\.[{UPPER}0-9]{{1,5}}){{1,5}}"
OUTCOME = re.compile(rf"^({CODE})\.\s+(\S.*)$")
# Sonunda nokta olmayan (MARP11.3.1 “İbadetler” ...) ve boşluklu (KAK-I 1.1. ..., TKMT 3.1. ...) kodlar.
OUTCOME_NO_DOT = re.compile(rf"^({CODE})\s+([{UPPER}“\"'].*)$")
OUTCOME_SPACED = re.compile(rf"^([{UPPER}]{{2,8}}(?:-[IVX]+)?)\s(\d{{1,2}}(?:\.\d{{1,2}}){{1,3}})\.?\s+(\S.*)$")
# Arapça: "ARP.5.3.1. DİNLEME/İZLEME" başlığı altında "5.3.1.1. Aile üyeleri ... yapabilme".
NUMBERED = re.compile(r"^(\d{1,2}(?:\.\d{1,2}){2,3})\.?\s+(\S.*)$")
BULLET = re.compile(rf"^(?:[{LOWER}]\)|[{LOWER}]\s*\.\s|[•\-–]\s|{CODE}\.[{LOWER}]\.)")
PAGE_NUMBER = re.compile(r"^\d{1,4}$")
TURKISH_ENDING = re.compile(r"(?:ebilme|abilme|bilme|ama|eme|ma|me)(?:si|sı)?$", re.IGNORECASE)
MAX_DESCRIPTION_LINES = 6
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
    # Etiket metne yapışabilir: "ÖĞRENME ÇIKTILARIEBRU.I.3.1.", "VE SÜREÇ BİLEŞENLERİa)".
    line = re.sub(r"(ÇIKTILARI|BİLEŞENLERİ)(?=\S)", r"\1 ", line)
    return SECTION_LABEL.sub("", line).strip()


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
    return description[: hit.end(1)] if hit else description


def is_well_formed(description):
    return bool(TURKISH_ENDING.search(description.rstrip(" .;:")))


def extract(text):
    """
    Bir programın kayıtları. Aynı kod birkaç kez geçer: "kod nasıl okunur" sayfasında metin komşu sütuna taşar,
    bu yüzden ilk geçiş değil, biçimi sağlam ilk geçiş esas alınır.
    """
    outcomes = {}
    in_section = False
    current = None
    parent_head = None
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
                known = outcomes.get(code)
                if description and (known is None or (not is_well_formed(known["description"]) and is_well_formed(description))):
                    outcomes[code] = {"code": code, "description": description, "line": current["line"]}
        current = None

    for number, raw in enumerate(lines, 1):
        stripped = raw.strip()
        if SECTION_START.search(stripped):
            in_section = True
        line = right_column(clean_line(raw))
        if not in_section:
            continue
        if SECTION_END.match(line):
            flush()
            in_section = False
            continue

        hit = match_outcome(line) if not BULLET.match(line) else None
        if hit:
            flush()
            code, description = hit
            parent_head = code.split(".")[0]
            # Tamamı büyük harfli metin kazanım değil, altındaki numaralı kazanımların başlığıdır.
            if description.upper() != description:
                current = {"code": code, "parts": [description], "line": number}
            continue
        numbered = NUMBERED.match(line)
        if numbered and parent_head:
            flush()
            current = {"code": f"{parent_head}.{numbered.group(1)}", "parts": [numbered.group(2)], "line": number}
            continue

        if current is None:
            continue
        # Açıklama ilk madde, boş satır, sayfa numarası ya da sınırda biter.
        if not line or PAGE_NUMBER.match(line) or BULLET.match(line) or len(current["parts"]) >= MAX_DESCRIPTION_LINES:
            flush()
            continue
        current["parts"].append(line)
    flush()
    return filter_noise(list(outcomes.values()))


def filter_noise(rows):
    """Beceri çerçevesi kodlarını, tek kelimelik satırları ve programın kod biçimine uymayanları eler."""
    rows = [r for r in rows if len(r["description"].split()) >= MIN_DESCRIPTION_WORDS]
    # Okuma Becerileri dersinin kodu da OB: yalnız beceri koduna benzeyen program kendi koduyla kalır.
    rows = [r for r in rows if not FRAMEWORK_HEAD.match(r["code"].split(".")[0])] or rows
    families = collections.Counter(code_family(r["code"]) for r in rows)
    return [r for r in rows if families[code_family(r["code"])] >= MIN_FAMILY_SHARE * len(rows)]


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
        rows = extract(text)
        programs.append({
            "pid": entry["pid"],
            "title": entry["title"],
            "pages": entry["pages"],
            "families": collections.Counter(code_family(r["code"]) for r in rows).most_common(),
            "wellFormed": sum(1 for r in rows if is_well_formed(r["description"])),
            "outcomes": rows,
        })
    json.dump(programs, open(out_path, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    return programs


if __name__ == "__main__":
    main(*sys.argv[1:4])
