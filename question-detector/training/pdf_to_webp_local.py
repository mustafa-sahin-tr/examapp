"""PDF -> sayfa başına WebP (yerel, paralel). Colab sürümü: pdf_to_webp.ipynb (aynı çıktı).

Girdi klasöründeki her PDF için çıktı klasöründe PDF adıyla bir alt klasör açar ve her sayfayı
`page_1.webp`, `page_2.webp` ... olarak yazar (çalışma sayfası editörünün toplu yüklemede beklediği
`<kitap>/page_<n>.webp` düzeni; --name ile değişir).

Netlik (varsayılan en net): her sayfaya ayrı çözünürlük. Taranmış sayfada gömülü taramanın kendi
çözünürlüğü (--min-dpi..--max-dpi arası; taramanın üstüne çıkmak netlik katmaz), metni seçilebilen
(vektör) sayfada --vector-dpi. Kayıpsız WebP (--lossy ile kayıplı).

Kaldığı yerden devam eder: var olan sayfa dosyaları atlanır (dosyalar .tmp'den yeniden adlandırılarak
yazılır, yarım dosya kalmaz). Yalnız girdi klasörünün kendisindeki PDF'lere bakar.

    pip3 install pymupdf pillow
    python3 training/pdf_to_webp_local.py "/Volumes/Mustafas HDD/kitaplar" "/Volumes/Mustafas HDD/hedefokul-ml/pages"
    python3 training/pdf_to_webp_local.py <girdi> <çıktı> --dry-run      # yalnız say
"""
import argparse
import io
import os
import sys
import time
from concurrent.futures import ProcessPoolExecutor, as_completed
from pathlib import Path

try:
    import pymupdf
    from PIL import Image
except ImportError:
    sys.exit("Eksik paket: pip3 install pymupdf pillow")

MAX_SIDE = 8000  # px; kitap sayfası için fazlasıyla yeter, aşırı büyük sayfada belleği korur

_docs: dict = {}  # işçi süreç başına son açılan PDF'ler (her sayfada yeniden açmamak için)
_MAX_OPEN = 4     # macOS süreç başına açık dosya sınırına (256) takılmamak için


def page_dpi(page, opts) -> float:
    if opts.dpi != "auto":
        return float(opts.dpi)
    scan = 0.0
    for info in page.get_image_info():
        x0, y0, x1, y1 = info["bbox"]
        w_in, h_in = (x1 - x0) / 72, (y1 - y0) / 72
        if w_in < 2 or h_in < 2:  # logo/ikon gibi küçük görseller sayfanın çözünürlüğünü belirlemez
            continue
        scan = max(scan, info["width"] / w_in, info["height"] / h_in)
    if not scan:
        return float(opts.vector_dpi)
    return min(max(scan, opts.min_dpi), opts.max_dpi)


def render_page(pdf: str, index: int, out: str, opts) -> str:
    doc = _docs.pop(pdf, None)
    if doc is None:
        doc = pymupdf.open(pdf)
        while len(_docs) >= _MAX_OPEN:
            _docs.pop(next(iter(_docs))).close()  # en eski açılanı kapat
    _docs[pdf] = doc  # sona taşı (en son kullanılan)
    page = doc[index]
    zoom = page_dpi(page, opts) / 72
    longest = max(page.rect.width, page.rect.height) * zoom
    if longest > MAX_SIDE:
        zoom *= MAX_SIDE / longest
    pix = page.get_pixmap(matrix=pymupdf.Matrix(zoom, zoom), alpha=False, colorspace=pymupdf.csRGB)
    img = Image.frombytes("RGB", (pix.width, pix.height), pix.samples)
    buf = io.BytesIO()
    if opts.lossy:
        img.save(buf, "WEBP", quality=opts.lossy, method=4)
    else:
        img.save(buf, "WEBP", lossless=True, quality=80, method=4)  # quality burada sıkıştırma emeği
    tmp = out + ".tmp"
    with open(tmp, "wb") as f:
        f.write(buf.getvalue())
    os.replace(tmp, out)
    return out


def plan(src: Path, dst: Path, opts):
    """(iş listesi, PDF başına özet, hatalı PDF'ler)."""
    jobs, summary, failed = [], [], []
    pdfs = sorted((p for p in src.iterdir() if p.is_file() and p.suffix.lower() == ".pdf"),
                  key=lambda p: p.name.lower())
    for pdf in pdfs:
        try:
            with pymupdf.open(pdf) as doc:
                if doc.needs_pass:
                    failed.append((pdf.name, "şifreli"))
                    continue
                n = doc.page_count
        except Exception as e:  # bozuk PDF: atla, sonda listele
            failed.append((pdf.name, str(e).splitlines()[0]))
            continue
        out_dir = dst / pdf.stem.strip()
        todo = []
        for i in range(n):
            out = out_dir / f"{opts.name.format(n=i + 1, pdf=pdf.stem.strip())}.webp"
            if not out.exists():
                todo.append((str(pdf), i, str(out)))
        summary.append((pdf.name, n, len(todo)))
        jobs += todo
    return jobs, summary, failed


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("src", type=Path, help="PDF'lerin olduğu klasör")
    p.add_argument("dst", type=Path, nargs="?", help="Çıktı kökü (verilmezse girdi klasörü)")
    p.add_argument("--dpi", default="auto", help="'auto' (varsayılan) ya da sabit sayı, örn. 300")
    p.add_argument("--vector-dpi", type=float, default=600, help="Vektör sayfa dpi (auto; iPad'de tuval yavaşsa 400)")
    p.add_argument("--min-dpi", type=float, default=300)
    p.add_argument("--max-dpi", type=float, default=600)
    p.add_argument("--lossy", type=int, metavar="KALITE", help="Kayıplı WebP (örn. 95); verilmezse kayıpsız")
    p.add_argument("--name", default="page_{n}", help="Sayfa adı; {n}: sayfa no, {pdf}: PDF adı (örn. '{pdf}-{n:03d}')")
    p.add_argument("-j", "--jobs", type=int, default=max(1, (os.cpu_count() or 2) - 2), help="Paralel işlem sayısı")
    p.add_argument("--dry-run", action="store_true", help="Yalnız ne yapılacağını yazar")
    opts = p.parse_args()
    if opts.dpi != "auto":
        float(opts.dpi)  # sayı değilse burada hata versin
    src, dst = opts.src, opts.dst or opts.src
    if not src.is_dir():
        sys.exit(f"Klasör yok: {src}")

    jobs, summary, failed = plan(src, dst, opts)
    for name, n, todo in summary:
        print(f"{name}: {n} sayfa, {todo} üretilecek" if todo else f"{name}: {n} sayfa, hepsi hazır")
    print(f"\n{len(summary)} PDF, {len(jobs)} sayfa üretilecek -> {dst}  ({opts.jobs} paralel işlem)")
    if opts.dry_run or not jobs:
        report_failed(failed)
        return

    for out_dir in {Path(out).parent for _, _, out in jobs}:
        out_dir.mkdir(parents=True, exist_ok=True)
    start, done, errors = time.time(), 0, []
    with ProcessPoolExecutor(max_workers=opts.jobs) as pool:
        futures = {pool.submit(render_page, pdf, i, out, opts): (pdf, i) for pdf, i, out in jobs}
        for fut in as_completed(futures):
            pdf, i = futures[fut]
            try:
                fut.result()
            except Exception as e:
                errors.append((f"{Path(pdf).name} sayfa {i + 1}", str(e).splitlines()[0]))
            done += 1
            if done % 20 == 0 or done == len(jobs):
                elapsed = time.time() - start
                eta = elapsed / done * (len(jobs) - done)
                print(f"  {done}/{len(jobs)}  {elapsed / 60:.1f} dk geçti, ~{eta / 60:.1f} dk kaldı", flush=True)

    print(f"\nBitti: {done - len(errors)} sayfa yazıldı, {len(errors)} sayfa hatalı, "
          f"{(time.time() - start) / 60:.1f} dk")
    report_failed(failed + errors)


def report_failed(failed) -> None:
    if failed:
        print("Hatalı:")
        for name, why in failed:
            print(f"  {name}: {why}")


if __name__ == "__main__":
    main()
