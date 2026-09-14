import { chromium } from 'playwright';
import { readFile, mkdir } from 'node:fs/promises';
import path from 'node:path';

const outDir = path.resolve(process.argv[2] || 'visual-output');
await mkdir(outDir, { recursive: true });

const source = await readFile(path.resolve('Core/WebDashboardPage.cs'), 'utf8');
const marker = 'public const string Html = """';
const markerAt = source.indexOf(marker);
if (markerAt < 0) throw new Error('WebDashboardPage.Html raw string not found');
const htmlStart = source.indexOf('"""', markerAt) + 3;
const htmlEnd = source.lastIndexOf('""";');
if (htmlEnd <= htmlStart) throw new Error('WebDashboardPage.Html closing raw string not found');
const dashboardHtml = source.slice(htmlStart, htmlEnd).trimStart();

function makeFrames() {
  const rows = [];
  for (let i = 1; i <= 36; i++) {
    let status = i <= 4 ? 'LEARNING' : 'ACCEPTED';
    if ([10, 17, 25].includes(i)) status = 'WARNING';
    if ([12, 13, 21, 22, 29].includes(i)) status = 'REJECTED';
    if (i === 30) status = 'ERROR';
    let rms = 0.42 + ((i * 17) % 13) / 100;
    let stars = ((i * 11) % 13 - 6) * 0.7;
    let bg = ((i * 7) % 11 - 5) * 0.55;
    let cause = 'NORMAL';
    if ([10, 12, 13].includes(i)) { rms = i === 13 ? 2.56 : 1.78 + (i % 2) * 0.14; cause = 'WIND / GUIDING DISTURBANCE'; }
    if ([17, 21, 22].includes(i)) { stars = i === 17 ? -27 : -44 - (i % 2) * 4; cause = 'CLOUD / TRANSPARENCY LOSS'; }
    if ([25, 29].includes(i)) { bg = i === 25 ? 24 : 42; cause = 'BACKGROUND / HAZE EVENT'; }
    if (i === 30) { rms = null; stars = null; bg = null; cause = 'ANALYSIS DATA UNAVAILABLE'; }
    const quality = status === 'LEARNING' || status === 'ERROR' ? null : status === 'REJECTED' ? 42 + (i % 9) : status === 'WARNING' ? 72 + (i % 5) : 92 + (i % 8);
    const confidence = status === 'LEARNING' ? 25 + i * 13 : status === 'ERROR' ? 0 : 86 + (i % 11);
    rows.push({
      frameIndex: i,
      status,
      quality,
      confidence,
      guideRmsArcsec: rms,
      starDeltaPercent: i <= 4 ? null : stars,
      backgroundDeltaPercent: i <= 4 ? null : bg,
      target: 'M31',
      filter: 'L',
      fileName: `frame_${String(i).padStart(3, '0')}_M31_L_180s.fits`,
      finalFileName: status === 'REJECTED' ? `BAD_frame_${String(i).padStart(3, '0')}_M31_L_180s.fits` : `frame_${String(i).padStart(3, '0')}_M31_L_180s.fits`,
      probableCause: cause,
      reason: status === 'REJECTED' ? 'Configured hard limit exceeded' : status === 'ERROR' ? 'Required analysis data unavailable' : ''
    });
  }
  return rows;
}

function makeGuide() {
  const series = [];
  const end = Date.parse('2026-09-08T22:45:00Z');
  for (let i = 0; i < 64; i++) {
    const t = end - (63 - i) * 320;
    let ra = Math.sin(i / 4.1) * 0.34 + Math.sin(i / 1.9) * 0.07;
    let dec = Math.cos(i / 5.3) * 0.27 - Math.sin(i / 2.8) * 0.06;
    if (i === 47) { ra = 2.35; dec = -1.72; }
    if (i > 48 && i < 54) { ra += 0.8; dec -= 0.45; }
    series.push({ timestampUtc: new Date(t).toISOString(), raArcsec: ra, decArcsec: dec, totalArcsec: Math.sqrt(ra * ra + dec * dec) });
  }
  const sq = key => Math.sqrt(series.reduce((a, s) => a + s[key] * s[key], 0) / series.length);
  return {
    rmsRaArcsec: sq('raArcsec'),
    rmsDecArcsec: sq('decArcsec'),
    rmsTotalArcsec: Math.sqrt(series.reduce((a, s) => a + s.totalArcsec * s.totalArcsec, 0) / series.length),
    maxExcursionArcsec: Math.max(...series.map(s => s.totalArcsec)),
    series
  };
}

let frames = makeFrames();
const proof = JSON.parse(await readFile(path.join(outDir, 'star-evidence.json'), 'utf8'));
function shapeFields(s) { return { imageEvidenceAvailable:s.Available, imageEvidenceDetail:s.Detail, starProofPng:s.PreviewPngBase64, starProofCaption:s.PreviewCaption, secondPassText:s.Summary }; }
Object.assign(frames[12], shapeFields(proof), { decisionSummary:'Rejected: guide failure confirmed by measured star shapes.' });
if (process.argv[3]) {
  const replay = JSON.parse(await readFile(path.resolve(process.argv[3]), 'utf8'));
  frames = replay.map(f=>({frameIndex:f.FrameIndex,status:f.StatusText,quality:f.OverallQuality,confidence:f.ConfidenceScore,guideRmsArcsec:f.GuideRmsArcsec,starDeltaPercent:f.StarDeviationPercent,backgroundDeltaPercent:f.BackgroundDeviationPercent,target:f.Target,filter:f.Filter,fileName:f.FileName,finalFileName:f.FileName,probableCause:f.ProbableCause,reason:f.ReasonText,decisionSummary:f.DecisionSummary,...shapeFields(f.ImageEvidence)}));
}
const current = frames.at(-1);
const usable=frames.filter(f=>['ACCEPTED','WARNING'].includes(f.status));
const rejected=frames.filter(f=>f.status.includes('REJECT')).length;
const snapshot = {
  mode: { enabled: true, monitorOnly: false },
  summary: { captured: frames.length, usable: usable.length, rejected, acceptanceRate: usable.length/(usable.length+rejected)*100, sessionQuality: usable.reduce((s,f)=>s+f.quality,0)/usable.length, sessionConfidence: usable.reduce((s,f)=>s+f.confidence,0)/usable.length },
  currentFrame: current,
  frames,
  guidingLive: makeGuide(),
  settings: {
    enableGuideRms: true,
    maxGuideRms: 1.5,
    enableStarCount: true,
    maxStarLossPercent: 35,
    enableBackground: true,
    maxBackgroundIncreasePercent: 30,
    maxBackgroundDecreasePercent: 30,
    enableSustainedExcursion: true,
    excursionThreshold: 2.0,
    enableHardExcursion: true,
    hardExcursionThreshold: 5.0
  },
  preview: { available: true, capturedUtc: '2026-09-08T22:44:31Z', imageId: 'visual-harness-light-036', generation: 36, generationError: false }
};

const previewBytes = await readFile(path.join(outDir, 'sample-light-preview.png'));
const browser = await chromium.launch({ headless: true });

async function capture(name, viewport) {
  const context = await browser.newContext({ viewport, deviceScaleFactor: 1 });
  const page = await context.newPage();
  const errors=[]; page.on('pageerror',e=>errors.push(e.message));
  await page.route('http://qsm.local/**', async route => {
    const u = new URL(route.request().url());
    if (u.pathname === '/' || u.pathname === '/index.html') {
      await route.fulfill({ status: 200, contentType: 'text/html; charset=utf-8', body: dashboardHtml });
      return;
    }
    if (u.pathname === '/dashboard/api/snapshot') {
      await route.fulfill({ status: 200, contentType: 'application/json; charset=utf-8', body: JSON.stringify(snapshot) });
      return;
    }
    if (u.pathname === '/dashboard/api/preview.jpg') {
      await route.fulfill({ status: 200, contentType: 'image/png', body: previewBytes, headers: { ETag: '"qsm-preview-36"' } });
      return;
    }
    await route.fulfill({ status: 404, body: 'not found' });
  });
  await page.goto('http://qsm.local/', { waitUntil: 'domcontentloaded' });
  await page.waitForFunction(count => document.getElementById('captured')?.textContent === String(count), frames.length, { timeout: 10000 });
  await page.waitForTimeout(700);
  await page.screenshot({ path: path.join(outDir, `${name}-top.png`), fullPage: false });
  await page.screenshot({ path: path.join(outDir, `${name}-full.png`), fullPage: true });
  const detail = page.locator('#frames details').first();
  await detail.locator('summary').click();
  const starImage = detail.locator('img');
  await starImage.waitFor({state:'visible'});
  if (!(await starImage.evaluate(img=>img.complete && img.naturalWidth===135))) throw new Error('Stellar proof failed to decode');
  await detail.screenshot({path:path.join(outDir,`${name}-star-proof.png`)});
  await page.locator('#timelineCanvas').scrollIntoViewIfNeeded();
  const geom=await page.evaluate(()=>lastTimelineGeom);
  const index=frames.findIndex((f,i)=>i>0&&f.imageEvidenceAvailable);
  await page.locator('#timelineCanvas').hover({position:{x:geom.left+index/(frames.length-1)*geom.plotW,y:80}});
  await page.locator('#timelineTip img').waitFor({state:'visible'});
  if (errors.length) throw new Error(errors.join('\n'));
  console.log(`PASS ${name}: page, persistent proof and timeline hover`);
  await context.close();
}

await capture('web-desktop-1440x1000', { width: 1440, height: 1000 });
await capture('web-tablet-900x1100', { width: 900, height: 1100 });
await capture('web-mobile-430x932', { width: 430, height: 932 });
await browser.close();
