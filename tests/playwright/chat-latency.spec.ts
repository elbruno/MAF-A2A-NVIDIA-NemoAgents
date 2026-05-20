import { expect, test, type Page } from '@playwright/test';

const PROMPT = 'Analyze quarterly revenue trends';

type ChatResult = {
  elapsedMs: number;
  actor: string;
  content: string;
};

async function sendPrompt(page: Page, prompt: string): Promise<ChatResult> {
  await page.fill('#messageInput', prompt);
  const start = Date.now();
  await page.click('#sendBtn');
  await page.waitForFunction(() => !((document.getElementById('sendBtn') as HTMLButtonElement | null)?.disabled ?? true), {
    timeout: 120_000
  });

  const rows = page.locator('.chat-row');
  const rowCount = await rows.count();
  const lastRow = rows.nth(rowCount - 1);
  const actor = (await lastRow.locator('.chat-role').innerText()).trim();
  const content = (await lastRow.locator('.chat-content').innerText()).replace(/\s+/g, ' ').trim();

  return {
    elapsedMs: Date.now() - start,
    actor,
    content
  };
}

test('quarterly revenue prompt has acceptable response latency', async ({ page }) => {
  await page.goto('/');
  await expect(page.locator('#messageInput')).toBeVisible();

  const samples: number[] = [];

  for (let i = 0; i < 3; i++) {
    let result = await sendPrompt(page, PROMPT);

    if (
      result.actor.toLowerCase().includes('system') &&
      result.content.toLowerCase().includes('warming up')
    ) {
      await page.waitForTimeout(1500);
      result = await sendPrompt(page, PROMPT);
    }

    expect(result.actor.toLowerCase()).toContain('nemo');
    expect(result.content.length).toBeGreaterThan(20);
    samples.push(result.elapsedMs);
    await page.waitForTimeout(500);
  }

  const averageMs = Math.round(samples.reduce((sum, item) => sum + item, 0) / samples.length);
  const maxMs = Math.max(...samples);

  console.log(`Chat latency samples (ms): ${samples.join(', ')}`);
  console.log(`Chat latency average (ms): ${averageMs}`);
  console.log(`Chat latency max (ms): ${maxMs}`);

  expect(averageMs).toBeLessThan(5000);
  expect(maxMs).toBeLessThan(10000);
});
