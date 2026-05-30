import { expect, test, type Page } from '@playwright/test';

const ANALYSIS_PROMPT = 'Analyze quarterly revenue trends';
const ACTION_PROMPT = 'Trigger alert for high CPU usage based on the analysis findings';
const GROUNDED_ACTION_PROMPT =
  'Trigger an alert for the payments service error-rate spike and recommend the runbook remediation';
const KNOWLEDGE_DOC_ID_PATTERN = /(RB|ASP|EM|RT)-\d{3}/;

type ChatTurn = {
  actor: string;
  content: string;
};

async function sendPrompt(page: Page, prompt: string): Promise<ChatTurn> {
  await page.fill('#messageInput', prompt);
  await page.click('#sendBtn');
  await page.waitForFunction(() => !((document.getElementById('sendBtn') as HTMLButtonElement | null)?.disabled ?? true), {
    timeout: 120_000
  });

  const rows = page.locator('.chat-row');
  const rowCount = await rows.count();
  const lastRow = rows.nth(rowCount - 1);
  const actor = (await lastRow.locator('.chat-role').innerText()).trim();
  const content = (await lastRow.locator('.chat-content').innerText()).replace(/\s+/g, ' ').trim();
  return { actor, content };
}

async function sendPromptUntilAgent(
  page: Page,
  prompt: string,
  expectedActorContains: string,
  maxAttempts = 5
): Promise<ChatTurn> {
  for (let attempt = 1; attempt <= maxAttempts; attempt++) {
    const result = await sendPrompt(page, prompt);
    if (result.actor.toLowerCase().includes(expectedActorContains.toLowerCase())) {
      return result;
    }

    if (
      result.actor.toLowerCase().includes('system') &&
      result.content.toLowerCase().includes('warming up') &&
      attempt < maxAttempts
    ) {
      await page.waitForTimeout(1500);
      continue;
    }

    if (attempt < maxAttempts) {
      await page.waitForTimeout(1000);
      continue;
    }

    return result;
  }

  return sendPrompt(page, prompt);
}

test('two-prompt chain routes NeMo then MAF with analysis context', async ({ page }) => {
  await page.goto('/');
  await expect(page.locator('#messageInput')).toBeVisible();

  const analysis = await sendPromptUntilAgent(page, ANALYSIS_PROMPT, 'nemo');

  expect(analysis.actor.toLowerCase()).toContain('nemo');
  expect(analysis.content.length).toBeGreaterThan(20);

  const action = await sendPromptUntilAgent(page, ACTION_PROMPT, 'maf');
  expect(action.actor.toLowerCase()).toContain('maf');
  expect(action.content.toLowerCase()).toContain('used prior nemo analysis context');
});

test('predefined question labels include routing suffixes', async ({ page }) => {
  await page.goto('/');
  await expect(page.locator('#predefinedQuestionSelect')).toBeVisible();

  const comboText = (await page.locator('#predefinedQuestionSelect').innerText()).replace(/\s+/g, ' ');
  expect(comboText).toContain('Analyze quarterly revenue trends (NeMo)');
  expect(comboText).toContain('Trigger alert for high CPU usage (MAF)');
  expect(comboText).toContain('based on the analysis findings (NeMo + MAF)');
  expect(comboText).toContain('Generate an incident-response image');
});

test('image-generation prompt routes to the MAF image agent', async ({ page }) => {
  await page.goto('/');
  await expect(page.locator('#messageInput')).toBeVisible();

  await page.fill('#messageInput', 'Generate an incident-response hero image');
  await page.click('#sendBtn');

  // The image agent path either renders a generated image or gracefully reports it is unavailable.
  // It must NOT fall through to the NeMo/RAG path.
  const generatedImage = page.locator('.chat-content img.chat-generated-image').last();
  const systemNotice = page.locator('.chat-row').last();

  await page.waitForFunction(() => !((document.getElementById('sendBtn') as HTMLButtonElement | null)?.disabled ?? true), {
    timeout: 120_000
  });

  const imageCount = await generatedImage.count();
  if (imageCount > 0) {
    await expect(generatedImage).toBeVisible();
  } else {
    const noticeText = (await systemNotice.locator('.chat-content').innerText()).toLowerCase();
    expect(noticeText).toMatch(/image agent|enable_image_agent|unavailable/);
  }
});

test('grounded MAF action cites a knowledge-base source', async ({ page }) => {
  await page.goto('/');
  await expect(page.locator('#messageInput')).toBeVisible();

  const action = await sendPromptUntilAgent(page, GROUNDED_ACTION_PROMPT, 'maf');
  expect(action.actor.toLowerCase()).toContain('maf');

  // The action narrative must reference a knowledge-base document id (e.g. RB-014).
  expect(action.content).toMatch(KNOWLEDGE_DOC_ID_PATTERN);

  // Deterministic citation chips must be rendered from the retrieval result.
  const sources = page.locator('[data-testid="grounded-sources"]').last();
  await expect(sources).toBeVisible();
  const chip = sources.locator('.source-chip').first();
  await expect(chip).toBeVisible();
  const docId = (await chip.getAttribute('data-doc-id')) ?? '';
  expect(docId).toMatch(KNOWLEDGE_DOC_ID_PATTERN);

  // The chip must be a link that opens the related indexed document in the viewer.
  const href = (await chip.getAttribute('href')) ?? '';
  expect(href).toContain(`/knowledge/${encodeURIComponent(docId)}`);
});

test('citation chip opens the related indexed document', async ({ page }) => {
  await page.goto('/');
  await expect(page.locator('#messageInput')).toBeVisible();

  const action = await sendPromptUntilAgent(page, GROUNDED_ACTION_PROMPT, 'maf');
  expect(action.actor.toLowerCase()).toContain('maf');

  const chip = page.locator('[data-testid="grounded-sources"]').last().locator('.source-chip').first();
  await expect(chip).toBeVisible();
  const docId = (await chip.getAttribute('data-doc-id')) ?? '';

  // Navigate directly to the proxied document viewer page and verify it renders.
  const response = await page.goto(`/knowledge/${encodeURIComponent(docId)}`);
  expect(response?.status()).toBe(200);
  await expect(page.locator('h1')).toContainText(docId);
  await expect(page.locator('.content')).toBeVisible();
});

test('indexed documents page lists the knowledge base', async ({ page }) => {
  const response = await page.goto('/knowledge');
  expect(response?.status()).toBe(200);

  // At least one document card linking to a viewer page must be present.
  const docCards = page.locator('a.doc-card');
  await expect(docCards.first()).toBeVisible();
  const firstHref = (await docCards.first().getAttribute('href')) ?? '';
  expect(firstHref).toMatch(/\/knowledge\/(RB|ASP|EM|RT)-\d{3}/);
});

test('Configuration card links to the indexed documents page', async ({ page }) => {
  await page.goto('/');
  const link = page.locator('#indexedDocsLink');
  await expect(link).toBeVisible();
  expect((await link.getAttribute('href')) ?? '').toBe('/knowledge');
});
