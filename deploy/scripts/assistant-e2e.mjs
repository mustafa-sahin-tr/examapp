#!/usr/bin/env node
// Issue #509: live end-to-end check of the in-app assistant through the gateway (:5678), with the real model.
// Logs in as a student in a headless browser (Keycloak), then talks to /api/assistant/chat over SSE.
//
//   ASSISTANT_E2E_PASSWORD=… node deploy/scripts/assistant-e2e.mjs --user ali.yildiz@example.test [--parent veli@example.test] [--confirm] [--base http://localhost:5678]
//
// Scenario A: "Zorlandığım konular hangileri?" → get_my_weak_topics, an answer.
// Scenario B: "Açılar konusunda günlük seti aç" → search_taxonomy + exactly one confirmation card; with --confirm the
//             card is confirmed (opens today's set: changes data) and must answer 200 with a navigate action.
// Guard:      a chat sent from /testsolve/… is refused with 409 AssistantClosedDuringTest.
// Scenario C (#1336, parent): with --parent (same password) "Çocuğum bu hafta nasıl, geciken ödevi var mı, nerede zorlanıyor?" →
//             list_my_children + the child tools, an answer; skipped without --parent. Parent role must be in ASSISTANT_ROLES.
// Needs Playwright from ui/node_modules (npm ci in ui/) and Chrome. Costs a few model calls. Exit code 1 on any failure.
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const args = process.argv.slice(2);
const opt = (name, fallback) => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : fallback; };
const user = opt('--user');
const parentUser = opt('--parent');
const base = opt('--base', 'http://localhost:5678');
const confirm = args.includes('--confirm');
const password = process.env.ASSISTANT_E2E_PASSWORD;
if (!user || !password) {
  console.error('usage: ASSISTANT_E2E_PASSWORD=… node deploy/scripts/assistant-e2e.mjs --user <student> [--confirm] [--base url]');
  process.exit(2);
}

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const { chromium } = createRequire(path.join(root, 'ui/package.json'))('playwright');
const api = `${base}/api/assistant`;

async function login(user) {
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  try {
    const page = await browser.newPage();
    let auth = null;
    page.on('request', r => { const h = r.headers().authorization; if (h && r.url().includes('/api/')) auth ??= h; });
    await page.goto(`${base}/app/login`);
    await page.fill('#username', user);
    await page.fill('#password', password);
    await page.click('button[type=submit]');
    for (let i = 0; i < 60 && !auth; i++) await page.waitForTimeout(500);
    if (!auth) throw new Error('login failed: no API call with a token within 30 s');
    return auth;
  } finally {
    await browser.close();
  }
}

/** One chat turn; returns { status, events: [{type, data}], text, done, ms }. */
async function turn(headers, message, route = '/dashboard') {
  const t0 = Date.now();
  const r = await fetch(`${api}/chat`, { method: 'POST', headers, body: JSON.stringify({ conversationId: null, message, context: { route } }) });
  if (r.status !== 200) return { status: r.status, body: await r.text(), events: [], text: '', ms: Date.now() - t0 };
  const reader = r.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '', text = '', done = null;
  const events = [];
  for (;;) {
    const { value, done: end } = await reader.read();
    if (end) break;
    buffer += decoder.decode(value, { stream: true });
    let i;
    while ((i = buffer.indexOf('\n\n')) >= 0) {
      const chunk = buffer.slice(0, i);
      buffer = buffer.slice(i + 2);
      if (chunk.startsWith(':')) continue; // keep-alive
      const type = /event: (.*)/.exec(chunk)?.[1];
      const data = JSON.parse(/data: (.*)/.exec(chunk)?.[1] ?? 'null');
      if (type === 'text') text += data.delta;
      else events.push({ type, data });
      if (type === 'done') done = data;
    }
  }
  return { status: 200, events, text, done, ms: Date.now() - t0 };
}

const results = [];
function check(name, ok, detail) {
  results.push({ name, ok });
  console.log(`${ok ? 'PASS' : 'FAIL'} ${name}${detail ? ` — ${detail}` : ''}`);
}
const tools = t => t.events.filter(e => e.type === 'tool_start').map(e => e.data.tool);
const summary = t => `${t.ms} ms, tools [${tools(t).join(', ')}], $${t.done?.usage?.costUsd ?? '?'}: ${t.text.replace(/\s+/g, ' ').slice(0, 300)}`;

const headers = { Authorization: await login(user), 'Content-Type': 'application/json', 'Accept-Language': 'tr-TR' };

const status = await (await fetch(`${api}/status`, { headers })).json();
if (!status.enabled) {
  console.log(`assistant is off for ${user}: ${JSON.stringify(status)}`);
  process.exit(1);
}

const a = await turn(headers, 'Zorlandığım konular hangileri?');
check('A weak topics', a.status === 200 && a.done && tools(a).includes('get_my_weak_topics') && a.text.trim().length > 0,
  a.status === 200 ? summary(a) : `HTTP ${a.status} ${a.body}`);

const b = await turn(headers, 'Açılar konusunda günlük seti aç');
const cards = b.events.filter(e => e.type === 'confirm_required');
check('B one confirmation card', b.status === 200 && b.done && tools(b).includes('start_daily_practice') && cards.length === 1,
  b.status === 200 ? `${cards.length} card(s); ${summary(b)}` : `HTTP ${b.status} ${b.body}`);
if (confirm && cards.length === 1) {
  const r = await fetch(`${api}/actions/${cards[0].data.pendingActionId}/confirm`, { method: 'POST', headers });
  const body = await r.text();
  check('B confirm opens the set', r.status === 200 && body.includes('practiceSession'), `HTTP ${r.status} ${body.slice(0, 300)}`);
} else if (!confirm) {
  console.log('SKIP B confirm (pass --confirm to open the set; it changes data)');
}

const closed = await turn(headers, 'Bu sorunun cevabı ne?', '/testsolve/1');
check('closed during a test', closed.status === 409 && closed.body?.includes('AssistantClosedDuringTest'), `HTTP ${closed.status}`);

if (parentUser) {
  const parentHeaders = { ...headers, Authorization: await login(parentUser) };
  const c = await turn(parentHeaders, 'Çocuğum bu hafta nasıl, geciken ödevi var mı, nerede zorlanıyor?', '/parent');
  const used = c.status === 200 ? tools(c) : [];
  check('C parent week', c.status === 200 && c.done && used.includes('list_my_children') && used.some(t => t.startsWith('get_child_') || t === 'list_child_assignments')
    && c.text.trim().length > 0, c.status === 200 ? summary(c) : `HTTP ${c.status} ${c.body}`);
} else {
  console.log('SKIP C parent week (pass --parent <user>)');
}
const failed = results.filter(r => !r.ok).length;
console.log(`${results.length - failed}/${results.length} passed`);
process.exit(failed ? 1 : 0);
