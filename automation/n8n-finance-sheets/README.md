# n8n + Google Sheets → Güncel Fiyat + Kâr/Zarar Maili

Bu doküman, Google Sheets’te **işlem bazlı** (BUY/SELL) tuttuğun portföyü, n8n ile periyodik olarak okuyup güncel fiyatları alarak **unrealized/realized P&L** hesaplayıp mail atmanı sağlar.

Fiyat kaynağı olarak bu repodaki `finance-api` kullanılır:
- `POST /api/AssetPrices/bulk` (çoklu fiyat çekme)
- (opsiyonel) `GET /api/ExchangeRate` (kur dönüşümü)

## 1) Google Sheets şeması

### Sheet: `Transactions`
Kolonlar (header satırı şart):
- `date` (örn: `2025-01-02`)
- `symbol` (örn: `AKBNK`, `AAPL`)
- `market` (örn: `BIST`, `US`, `FUND`, `GOLD`, `SILVER`, `DEPOSIT`)
- `currency` (örn: `TRY`, `USD`)
- `side` (`BUY` / `SELL`)
- `qty` (pozitif sayı)
- `price` (birim fiyat)
- `fee` (opsiyonel; yoksa 0)
- `note` (opsiyonel)

Örnek CSV: `transactions-template.csv`

### (Opsiyonel) Sheet: `Holdings`
Bu sheet’i n8n’in güncel sonuçları yazması için kullanabilirsin.
Header örneği: `holdings-template.csv`

## 2) Maliyet yöntemi

İki hesaplama yöntemi çıkarıldı:
- **Moving Average (AVG)**: satışı mevcut ortalama maliyetten düşer (en pratik)
- **FIFO**: alım “lot”larından sırayla düşer (daha detaylı)

n8n Code node içinde `COST_METHOD` ile seçilir:
- `AVG` veya `FIFO`

## 3) n8n workflow (node’lar)

Aşağıdaki node’ları sırayla kur:

### A) `Cron`
- Örn: her gün 18:10

### B) `Google Sheets` → “Read” (`Transactions`)
- Spreadsheet: senin dosyan
- Sheet: `Transactions`
- Range: `A:Z` (kolon sayına göre)
- Return All: `true`

### C) `Code` (Node.js) → “Normalize + Holdings hesapla + PriceRequests üret”
Bu node, satırları doğrular, holdings çıkarır (AVG + FIFO), sonra fiyat için istek listesi üretir.

> Not: n8n Code node çıktısı **tek item** olsun diye en sonda `return [{ json: ... }]` kullanıyoruz.

```javascript
// n8n Code node (Node.js)

function toNumber(value, fallback = 0) {
  const n = typeof value === 'number' ? value : Number(String(value ?? '').replace(',', '.'));
  return Number.isFinite(n) ? n : fallback;
}

function normStr(v) {
  return String(v ?? '').trim();
}

function upper(v) {
  return normStr(v).toUpperCase();
}

function parseISODate(v) {
  const s = normStr(v);
  // YYYY-MM-DD bekleniyor
  const d = new Date(s + 'T00:00:00Z');
  if (Number.isNaN(d.getTime())) return null;
  return d;
}

function marketToAssetTypeEnum(market) {
  // finance-api AssetType enum:
  // Stock=0 (BIST), USStock=1, Gold=2, Silver=3, Fund=4, FixedDeposit=5
  switch (upper(market)) {
    case 'BIST':
    case 'STOCK':
      return 0;
    case 'US':
    case 'USSTOCK':
      return 1;
    case 'GOLD':
      return 2;
    case 'SILVER':
      return 3;
    case 'FUND':
      return 4;
    case 'DEPOSIT':
    case 'FIXEDDEPOSIT':
      return 5;
    default:
      return null;
  }
}

function computeHoldingsAVG(transactions) {
  const stateByKey = new Map();
  const errors = [];

  for (const t of transactions) {
    const key = t.key;
    const st = stateByKey.get(key) ?? {
      symbol: t.symbol,
      market: t.market,
      currency: t.currency,
      qty: 0,
      costBasis: 0,
      realizedPnl: 0,
    };

    if (t.side === 'BUY') {
      st.costBasis += t.qty * t.price + t.fee;
      st.qty += t.qty;
    } else if (t.side === 'SELL') {
      if (st.qty + 1e-12 < t.qty) {
        errors.push({ key, message: `SELL qty(${t.qty}) > holdings qty(${st.qty})`, row: t._row });
        continue;
      }
      const avgCost = st.qty > 0 ? st.costBasis / st.qty : 0;
      const proceeds = t.qty * t.price - t.fee;
      const costRemoved = t.qty * avgCost;
      st.realizedPnl += proceeds - costRemoved;
      st.qty -= t.qty;
      st.costBasis -= costRemoved;
      if (Math.abs(st.qty) < 1e-12) st.qty = 0;
      if (Math.abs(st.costBasis) < 1e-8) st.costBasis = 0;
    }

    stateByKey.set(key, st);
  }

  const holdings = Array.from(stateByKey.values()).map(h => ({
    ...h,
    avgCost: h.qty > 0 ? h.costBasis / h.qty : 0,
  }));

  return { holdings, errors };
}

function computeHoldingsFIFO(transactions) {
  const stateByKey = new Map();
  const errors = [];

  for (const t of transactions) {
    const key = t.key;
    const st = stateByKey.get(key) ?? {
      symbol: t.symbol,
      market: t.market,
      currency: t.currency,
      lots: [], // { qty, unitCost }
      qty: 0,
      costBasis: 0,
      realizedPnl: 0,
    };

    if (t.side === 'BUY') {
      const unitFee = t.qty > 0 ? (t.fee / t.qty) : 0;
      const unitCost = t.price + unitFee;
      st.lots.push({ qty: t.qty, unitCost });
      st.qty += t.qty;
      st.costBasis += t.qty * unitCost;
    } else if (t.side === 'SELL') {
      if (st.qty + 1e-12 < t.qty) {
        errors.push({ key, message: `SELL qty(${t.qty}) > holdings qty(${st.qty})`, row: t._row });
        continue;
      }

      let remaining = t.qty;
      let costRemoved = 0;
      while (remaining > 1e-12) {
        const lot = st.lots[0];
        if (!lot) {
          errors.push({ key, message: `FIFO lots empty while selling`, row: t._row });
          break;
        }
        const take = Math.min(remaining, lot.qty);
        costRemoved += take * lot.unitCost;
        lot.qty -= take;
        remaining -= take;
        if (lot.qty <= 1e-12) st.lots.shift();
      }

      const proceeds = t.qty * t.price - t.fee;
      st.realizedPnl += proceeds - costRemoved;
      st.qty -= t.qty;
      st.costBasis -= costRemoved;
      if (Math.abs(st.qty) < 1e-12) st.qty = 0;
      if (Math.abs(st.costBasis) < 1e-8) st.costBasis = 0;
    }

    stateByKey.set(key, st);
  }

  const holdings = Array.from(stateByKey.values()).map(h => ({
    symbol: h.symbol,
    market: h.market,
    currency: h.currency,
    qty: h.qty,
    costBasis: h.costBasis,
    realizedPnl: h.realizedPnl,
    avgCost: h.qty > 0 ? h.costBasis / h.qty : 0,
  }));

  return { holdings, errors };
}

const COST_METHOD = upper(process.env.COST_METHOD || 'AVG');

// Google Sheets node çoğu zaman her satırı ayrı item verir.
// Bu yüzden burada tüm input item’ları tek listede topluyoruz.
const rows = $input.all().map((item, idx) => ({ ...item.json, _row: idx + 2 })); // 1 header varsayımıyla +2

const normalized = [];
const validationErrors = [];

for (const r of rows) {
  const date = parseISODate(r.date);
  const symbol = upper(r.symbol);
  const market = upper(r.market);
  const currency = upper(r.currency);
  const side = upper(r.side);
  const qty = toNumber(r.qty);
  const price = toNumber(r.price);
  const fee = toNumber(r.fee);

  if (!date) validationErrors.push({ row: r._row, message: `Invalid date: ${r.date}` });
  if (!symbol) validationErrors.push({ row: r._row, message: `Missing symbol` });
  if (!market) validationErrors.push({ row: r._row, message: `Missing market` });
  if (!currency) validationErrors.push({ row: r._row, message: `Missing currency` });
  if (side !== 'BUY' && side !== 'SELL') validationErrors.push({ row: r._row, message: `Invalid side: ${r.side}` });
  if (!(qty > 0)) validationErrors.push({ row: r._row, message: `qty must be > 0` });
  if (!(price > 0)) validationErrors.push({ row: r._row, message: `price must be > 0` });

  const assetType = marketToAssetTypeEnum(market);
  if (assetType === null) validationErrors.push({ row: r._row, message: `Unknown market -> AssetType: ${r.market}` });

  const key = `${market}:${currency}:${symbol}`;
  normalized.push({
    _row: r._row,
    date,
    symbol,
    market,
    currency,
    side,
    qty,
    price,
    fee,
    assetType,
    key,
  });
}

normalized.sort((a, b) => a.date.getTime() - b.date.getTime() || a._row - b._row);

const avg = computeHoldingsAVG(normalized);
const fifo = computeHoldingsFIFO(normalized);

const selected = COST_METHOD === 'FIFO' ? fifo : avg;

// Sadece qty>0 olanlar için fiyat iste
const priceRequests = selected.holdings
  .filter(h => h.qty > 0)
  .map(h => ({ Type: marketToAssetTypeEnum(h.market), Symbol: h.symbol }));

// Unique yap (Type+Symbol)
const uniq = new Map();
for (const pr of priceRequests) {
  const k = `${pr.Type}:${pr.Symbol}`;
  if (!uniq.has(k)) uniq.set(k, pr);
}

return [
  {
    json: {
      costMethod: COST_METHOD,
      transactionsCount: normalized.length,
      validationErrors,
      avg,
      fifo,
      holdings: selected.holdings,
      holdingsErrors: selected.errors,
      priceRequests: Array.from(uniq.values()),
      generatedAt: new Date().toISOString(),
    },
  },
];
```

### D) `HTTP Request` → “Bulk fiyat çek” (`finance-api`)
- Method: `POST`
- URL: `{{ $env.FINANCE_API_BASE_URL }}/api/AssetPrices/bulk`
  - örn: `http://localhost:5055/api/AssetPrices/bulk`
- Send Body as JSON: `true`
- Body:
  - Expression: `{{ $json.priceRequests }}`

Beklenen response: `[{ Type, Symbol, Price, Unit, LastUpdated, IsSuccess, ErrorMessage }]`

### E) `Code` → “Fiyatları merge et + P&L + Email HTML üret”
Bu node, holdings + fiyatları birleştirir ve mail içeriği üretir.

```javascript
const payload = $input.first().json; // önceki Code node çıktısı (tek item)

// HTTP Request node ayrı bir branch ise, n8n'de Merge node kullanıp buraya iki kaynaktan getir.
// Bu snippet, ikinci input olarak fiyat listesini bekler:
const prices = $input.all()[1]?.json; // 2. input item: HTTP response

const priceList = Array.isArray(prices) ? prices : (Array.isArray(prices?.body) ? prices.body : prices);
const priceMap = new Map();
for (const p of (priceList ?? [])) {
  const key = `${p.Type}:${String(p.Symbol).toUpperCase()}`;
  priceMap.set(key, p);
}

const holdings = payload.holdings.map(h => {
  const type = (h.market === 'BIST') ? 0
    : (h.market === 'US') ? 1
    : (h.market === 'GOLD') ? 2
    : (h.market === 'SILVER') ? 3
    : (h.market === 'FUND') ? 4
    : (h.market === 'DEPOSIT') ? 5
    : null;

  const p = type === null ? null : priceMap.get(`${type}:${h.symbol}`);
  const lastPrice = p?.IsSuccess ? Number(p.Price) : null;
  const marketValue = lastPrice != null ? h.qty * lastPrice : null;
  const unrealizedPnl = marketValue != null ? (marketValue - h.costBasis) : null;
  const unrealizedPnlPct = unrealizedPnl != null && h.costBasis !== 0 ? (unrealizedPnl / h.costBasis) * 100 : null;

  return {
    ...h,
    assetType: type,
    lastPrice,
    priceUnit: p?.Unit ?? h.currency,
    priceOk: !!p?.IsSuccess,
    priceError: p?.IsSuccess ? null : (p?.ErrorMessage ?? 'Price not found'),
    marketValue,
    unrealizedPnl,
    unrealizedPnlPct,
    updatedAt: p?.LastUpdated ?? payload.generatedAt,
  };
});

// Totaller: currency bazlı
const totalsByCurrency = new Map();
for (const h of holdings) {
  const c = h.currency;
  const t = totalsByCurrency.get(c) ?? { currency: c, cost: 0, value: 0, pnl: 0, realized: 0, missingPrices: 0 };
  t.realized += h.realizedPnl ?? 0;
  if (h.qty > 0) {
    t.cost += h.costBasis;
    if (h.marketValue == null) t.missingPrices += 1;
    else {
      t.value += h.marketValue;
      t.pnl += (h.unrealizedPnl ?? 0);
    }
  }
  totalsByCurrency.set(c, t);
}

function fmt(n) {
  if (n == null) return '-';
  return new Intl.NumberFormat('tr-TR', { maximumFractionDigits: 2 }).format(n);
}

function fmtMoney(n, cur) {
  if (n == null) return '-';
  return new Intl.NumberFormat('tr-TR', { style: 'currency', currency: cur, maximumFractionDigits: 2 }).format(n);
}

const rowsHtml = holdings
  .filter(h => h.qty !== 0 || (h.realizedPnl ?? 0) !== 0)
  .sort((a, b) => (b.marketValue ?? 0) - (a.marketValue ?? 0))
  .map(h => {
    const pnlColor = (h.unrealizedPnl ?? 0) >= 0 ? '#0f766e' : '#b91c1c';
    return `
      <tr style="border-bottom:1px solid #e2e8f0;">
        <td style="padding:10px;font-weight:600;">${h.symbol}</td>
        <td style="padding:10px;">${h.market}</td>
        <td style="padding:10px; text-align:right;">${fmt(h.qty)}</td>
        <td style="padding:10px; text-align:right;">${fmtMoney(h.avgCost, h.currency)}</td>
        <td style="padding:10px; text-align:right;">${h.lastPrice == null ? `<span style=\"color:#b91c1c\">${h.priceError}</span>` : fmtMoney(h.lastPrice, h.currency)}</td>
        <td style="padding:10px; text-align:right;">${h.marketValue == null ? '-' : fmtMoney(h.marketValue, h.currency)}</td>
        <td style="padding:10px; text-align:right; color:${pnlColor}; font-weight:600;">${h.unrealizedPnl == null ? '-' : `${fmtMoney(h.unrealizedPnl, h.currency)} (${fmt(h.unrealizedPnlPct)}%)`}</td>
        <td style="padding:10px; text-align:right; color:${(h.realizedPnl ?? 0) >= 0 ? '#0f766e' : '#b91c1c'};">${fmtMoney(h.realizedPnl ?? 0, h.currency)}</td>
      </tr>
    `;
  }).join('');

const totalsHtml = Array.from(totalsByCurrency.values())
  .map(t => {
    const pnlColor = t.pnl >= 0 ? '#0f766e' : '#b91c1c';
    const pnlPct = t.cost !== 0 ? (t.pnl / t.cost) * 100 : 0;
    return `
      <tr style="border-bottom:1px solid #e2e8f0;">
        <td style="padding:10px;font-weight:600;">${t.currency}</td>
        <td style="padding:10px;text-align:right;">${fmtMoney(t.cost, t.currency)}</td>
        <td style="padding:10px;text-align:right;">${fmtMoney(t.value, t.currency)}</td>
        <td style="padding:10px;text-align:right;color:${pnlColor};font-weight:700;">${fmtMoney(t.pnl, t.currency)} (${fmt(pnlPct)}%)</td>
        <td style="padding:10px;text-align:right;color:${(t.realized ?? 0) >= 0 ? '#0f766e' : '#b91c1c'};">${fmtMoney(t.realized ?? 0, t.currency)}</td>
        <td style="padding:10px;text-align:right;color:#64748b;">${t.missingPrices}</td>
      </tr>
    `;
  }).join('');

const errors = [...(payload.validationErrors ?? []), ...(payload.holdingsErrors ?? [])];
const errorsHtml = errors.length
  ? `<div style="margin-top:16px;padding:12px;border:1px solid #fecaca;background:#fff1f2;border-radius:10px;">
      <div style="font-weight:700;color:#991b1b;">Uyarılar</div>
      <ul style="margin:8px 0 0 18px;">
        ${errors.map(e => `<li>Satır ${e.row ?? '-'}: ${e.message}</li>`).join('')}
      </ul>
     </div>`
  : '';

const subject = `Portföy Özeti (${payload.costMethod}) - ${new Date().toISOString().slice(0,10)}`;

const html = `
<div style="font-family:Arial,sans-serif;color:#0f172a;">
  <h2 style="margin:0 0 12px 0;">Portföy Özeti</h2>
  <div style="color:#475569;margin-bottom:14px;">Yöntem: <b>${payload.costMethod}</b> | Üretilme: ${payload.generatedAt}</div>

  <h3 style="margin:16px 0 8px 0;">Toplamlar (para birimi bazlı)</h3>
  <table style="width:100%;border-collapse:collapse;border:1px solid #e2e8f0;border-radius:10px;overflow:hidden;">
    <tr style="background:#0b5cad;color:#fff;">
      <th style="padding:10px;text-align:left;">Currency</th>
      <th style="padding:10px;text-align:right;">Cost</th>
      <th style="padding:10px;text-align:right;">Value</th>
      <th style="padding:10px;text-align:right;">Unrealized P&L</th>
      <th style="padding:10px;text-align:right;">Realized P&L</th>
      <th style="padding:10px;text-align:right;">Missing Prices</th>
    </tr>
    ${totalsHtml}
  </table>

  <h3 style="margin:16px 0 8px 0;">Varlıklar</h3>
  <table style="width:100%;border-collapse:collapse;border:1px solid #e2e8f0;border-radius:10px;overflow:hidden;">
    <tr style="background:#f1f5f9;color:#0f172a;">
      <th style="padding:10px;text-align:left;">Symbol</th>
      <th style="padding:10px;text-align:left;">Market</th>
      <th style="padding:10px;text-align:right;">Qty</th>
      <th style="padding:10px;text-align:right;">Avg Cost</th>
      <th style="padding:10px;text-align:right;">Last Price</th>
      <th style="padding:10px;text-align:right;">Market Value</th>
      <th style="padding:10px;text-align:right;">Unrealized P&L</th>
      <th style="padding:10px;text-align:right;">Realized P&L</th>
    </tr>
    ${rowsHtml}
  </table>

  ${errorsHtml}
</div>
`;

return [{
  json: {
    subject,
    html,
    holdings,
    totals: Array.from(totalsByCurrency.values()),
  }
}];
```

> n8n’de iki farklı kaynaktan veri kullanacaksan (holdings payload + HTTP fiyat sonucu) araya **Merge** node koy:
> - Mode: “Merge by Position”
> - Input 1: holdings payload
> - Input 2: HTTP fiyat sonucu

### F) `Email`
- Subject: `{{ $json.subject }}`
- HTML: `{{ $json.html }}`
- SMTP/Gmail credential’larını n8n’de tanımla

## 4) ENV ayarları (öneri)

n8n container’ında env kullan:
- `FINANCE_API_BASE_URL=http://<finance-api-host>:<port>`
- `COST_METHOD=AVG` (veya `FIFO`)

## 5) Sık hatalar

- `market` mapping’i yanlışsa (`BIST` yerine `bist` vs) uyarı listesine düşer.
- `SELL > BUY` olursa uyarı üretir ve o satır işlenmez.
- Fiyat bulunamazsa tabloda hata mesajı görünür (mail yine gider).

---

İstersen bir sonraki adımda: senin `finance-api`’nin gerçek base URL/port’unu (docker-compose) baz alıp, n8n tarafındaki HTTP node’larını **tam net ayarlarla** (auth vs) uyarlayabilirim.
