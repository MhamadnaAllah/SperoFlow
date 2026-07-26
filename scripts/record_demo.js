const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const ARTIFACT_DIR = 'C:\\Users\\fal\\.gemini\\antigravity\\brain\\09f3baea-8939-42b8-9df3-659aa7f54657';
const SNAPSHOTS_DIR = path.join(ARTIFACT_DIR, 'demo_snapshots');
const VIDEO_DIR = path.join(ARTIFACT_DIR, 'demo_video');

if (!fs.existsSync(SNAPSHOTS_DIR)) fs.mkdirSync(SNAPSHOTS_DIR, { recursive: true });
if (!fs.existsSync(VIDEO_DIR)) fs.mkdirSync(VIDEO_DIR, { recursive: true });

(async () => {
  console.log('[1/4] Launching Edge browser for demo recording...');
  let browser;
  try {
    browser = await chromium.launch({ channel: 'msedge', headless: true });
    console.log('Successfully launched Edge browser.');
  } catch (e) {
    console.log('Edge launch error:', e.message);
    browser = await chromium.launch({ headless: true });
  }

  console.log('[2/4] Creating video recording context with ffmpeg...');
  const context = await browser.newContext({
    viewport: { width: 1920, height: 1080 },
    recordVideo: {
      dir: VIDEO_DIR,
      size: { width: 1920, height: 1080 }
    }
  });

  const page = await context.newPage();
  const baseUrl = 'http://localhost:3000';
  const delay = (ms) => new Promise(res => setTimeout(res, ms));

  async function visitAndSnap(pathName, snapName) {
    const url = `${baseUrl}${pathName}`;
    console.log(`Navigating to ${url}...`);
    try {
      await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 10000 });
    } catch (e) {
      console.log(`Notice for ${pathName}: ${e.message}`);
    }
    await delay(1200);
    const snapPath = path.join(SNAPSHOTS_DIR, `${snapName}.png`);
    await page.screenshot({ path: snapPath, fullPage: false }).catch(() => {});
    console.log(`Saved snapshot: ${snapName}.png`);
  }

  try {
    console.log('[3/4] Executing application tour...');
    await visitAndSnap('/', '01_landing_page');
    
    // Scroll Landing
    await page.evaluate(() => window.scrollBy(0, 600)).catch(() => {});
    await delay(800);
    await page.screenshot({ path: path.join(SNAPSHOTS_DIR, '01_landing_page_scrolled.png') }).catch(() => {});

    await visitAndSnap('/matrix', '02_eisenhower_matrix');
    await visitAndSnap('/roles', '03_life_roles');
    await visitAndSnap('/tasks', '04_tasks_proposals');
    await visitAndSnap('/journaling', '05_journaling_insights');
    await visitAndSnap('/goals', '06_goals_milestones');
    await visitAndSnap('/habits', '07_habits_tracker');
    await visitAndSnap('/calendar', '08_calendar_scheduling');
    await visitAndSnap('/agentic-hub', '09_agentic_hub');
    await visitAndSnap('/matrix', '10_matrix_final');

    console.log('[4/4] Tour finished! Finalizing video recording...');
  } catch (err) {
    console.error('Error during tour:', err);
  } finally {
    const videoObj = page.video();
    const rawVideoPath = videoObj ? await videoObj.path().catch(() => null) : null;

    await page.close();
    await context.close();
    await browser.close();

    console.log('Recorded raw video path:', rawVideoPath);
    if (rawVideoPath && fs.existsSync(rawVideoPath)) {
      const destVideoPath = path.join(ARTIFACT_DIR, 'speroflow_demo.webm');
      fs.copyFileSync(rawVideoPath, destVideoPath);
      console.log('SUCCESS! Copied video demo to artifact:', destVideoPath);
    }
  }
})();
