// One-off media capture for README/docs: high-res screenshots + short videos
// (converted to GIF afterwards with ffmpeg). Run with: node scripts/capture-media.cjs
const { chromium } = require('@playwright/test');
const fs = require('fs');
const path = require('path');

const PORT = process.env.WEB_PORT || '49969';
const BASE = `http://127.0.0.1:${PORT}`;
const videoDir = path.resolve('artifacts/media');
const shotDir = path.resolve('images');
fs.mkdirSync(videoDir, { recursive: true });
fs.mkdirSync(shotDir, { recursive: true });

const VP = { width: 1366, height: 900 };

function newVideoCtx(browser, sub) {
  return browser.newContext({
    viewport: VP,
    deviceScaleFactor: 2,
    recordVideo: { dir: path.join(videoDir, sub), size: VP },
  });
}

(async () => {
  const browser = await chromium.launch();

  // ---------- 1) Chat interaction (RAG grounded action) ----------
  const ctx1 = await newVideoCtx(browser, 'chat');
  const page = await ctx1.newPage();
  await page.goto(BASE, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('#messageInput', { timeout: 30000 });
  await page.waitForTimeout(1800);
  await page.screenshot({ path: path.join(shotDir, 'screenshot-chat-home.png') });
  console.log('shot: screenshot-chat-home.png');

  const prompt =
    'Trigger the appropriate alert and recommend the runbook remediation for the payments ' +
    'error_rate spike to 7.2% sustained above the 5% threshold after the 14:32 deployment, ' +
    'citing the runbook and severity policy.';

  const input = page.locator('#messageInput');
  await input.click();
  await input.type(prompt, { delay: 10 });
  await page.waitForTimeout(400);
  await page.locator('#sendBtn').click();

  // Wait for the agent bubble first (always present), then give the grounded
  // source chips a short window to render.
  await page.waitForSelector('.chat-row-other .chat-bubble-other', { timeout: 120000 });
  await page.waitForSelector('[data-testid="grounded-sources"]', { timeout: 15000 }).catch(() => {});
  await page.waitForTimeout(2000);
  await page.screenshot({ path: path.join(shotDir, 'screenshot-chat-grounded.png'), fullPage: true });
  console.log('shot: screenshot-chat-grounded.png');
  await page.waitForTimeout(1200);
  await ctx1.close();

  // ---------- 2) Knowledge base navigation ----------
  const ctx2 = await newVideoCtx(browser, 'kb');
  const kb = await ctx2.newPage();
  await kb.goto(`${BASE}/knowledge`, { waitUntil: 'domcontentloaded' });
  await kb.waitForSelector('a[href="/knowledge/RB-014"]', { timeout: 30000 });
  await kb.waitForTimeout(1500);
  await kb.screenshot({ path: path.join(shotDir, 'screenshot-knowledge-base.png') });
  console.log('shot: screenshot-knowledge-base.png');

  await kb.click('a[href="/knowledge/RB-014"]');
  await kb.waitForLoadState('domcontentloaded');
  await kb.waitForTimeout(1600);
  await kb.screenshot({ path: path.join(shotDir, 'screenshot-knowledge-doc.png'), fullPage: true });
  console.log('shot: screenshot-knowledge-doc.png');
  await kb.waitForTimeout(1000);
  await ctx2.close();

  await browser.close();

  const findWebm = (sub) => {
    const dir = path.join(videoDir, sub);
    const f = fs.readdirSync(dir).find((x) => x.endsWith('.webm'));
    return f ? path.join(dir, f) : null;
  };
  console.log('CHAT_VIDEO=' + findWebm('chat'));
  console.log('KB_VIDEO=' + findWebm('kb'));
})().catch((err) => {
  console.error(err);
  process.exit(1);
});
