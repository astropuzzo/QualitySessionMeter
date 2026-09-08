namespace NINA.Plugin.QualitySessionMeter.Core;

internal static class WebDashboardPage {
    public const string Html = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
<meta name="theme-color" content="#0b0d10">
<title>QualitySessionMeter</title>
<style>
:root{color-scheme:dark;--bg:#0b0d10;--card:#14181d;--card2:#101419;--line:#303741;--grid:#29313a;--muted:#a7b0bc;--text:#f1f4f8;--blue:#8ab4f8;--green:#81c995;--yellow:#fdd663;--red:#f28b82;--purple:#c58af9}
*{box-sizing:border-box}html,body{min-height:100%;background:var(--bg)}body{margin:0;color:var(--text);font:14px/1.45 Inter,ui-sans-serif,system-ui,-apple-system,Segoe UI,sans-serif;background:radial-gradient(circle at 15% -10%,#182133 0,transparent 34%),var(--bg)}
.wrap{width:min(1320px,100%);margin:auto;padding:18px}.top{display:flex;align-items:center;justify-content:space-between;gap:12px;margin-bottom:16px}.brand h1{margin:0;font-size:22px;letter-spacing:-.02em}.brand div{color:var(--muted);font-size:12px}.live{display:flex;align-items:center;gap:7px;color:var(--green);font-size:12px}.dot{width:8px;height:8px;border-radius:50%;background:currentColor;box-shadow:0 0 12px currentColor}
.grid{display:grid;grid-template-columns:repeat(12,1fr);gap:12px}.card{background:linear-gradient(180deg,#171b21,#12161b);border:1px solid var(--line);border-radius:14px;padding:15px;min-width:0}.summary{grid-column:span 2}.summary .n{font-size:27px;font-weight:760;letter-spacing:-.04em}.label{color:var(--muted);font-size:11px;text-transform:uppercase;letter-spacing:.08em}.current{grid-column:span 5}.preview{grid-column:span 7;min-height:280px;display:flex;flex-direction:column}.guide{grid-column:1/-1}.timeline{grid-column:1/-1}.history{grid-column:1/-1}.row{display:flex;justify-content:space-between;gap:14px;padding:6px 0;border-bottom:1px solid #252c34}.row:last-child{border:0}.row span:first-child{color:var(--muted)}.row strong{min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.status{display:inline-flex;padding:4px 8px;border-radius:999px;font-weight:700;font-size:11px;letter-spacing:.04em;background:#242b34}.accepted{color:var(--green);background:#18271f}.rejected{color:var(--red);background:#301d1f}.warning{color:var(--yellow);background:#2d2819}.learning{color:var(--blue);background:#192536}.error{color:var(--red);background:#301d1f}.section-title{font-size:14px;font-weight:700;margin-bottom:8px}.muted{color:var(--muted)}
#preview{width:100%;height:100%;min-height:240px;object-fit:contain;border-radius:10px;background:#090b0e;margin-top:10px;border:1px solid #20262d}.chart-wrap{position:relative;width:100%;overflow:hidden}.chart-tip{position:absolute;display:none;z-index:5;pointer-events:none;max-width:330px;padding:8px 10px;border-radius:8px;background:#080a0dcc;border:1px solid #3b4551;color:#eef2f6;font-size:11px;line-height:1.45;box-shadow:0 8px 28px #0008;white-space:normal}.timeline canvas{display:block;width:100%;height:365px}.guide canvas{display:block;width:100%;height:170px;margin-top:8px}.chart-note{color:var(--muted);font-size:11px;margin-top:-2px;margin-bottom:6px}table{width:100%;border-collapse:collapse;font-size:12px}th{color:var(--muted);font-weight:600;text-align:left;padding:8px 7px;border-bottom:1px solid var(--line)}td{padding:8px 7px;border-bottom:1px solid #252c34;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;max-width:240px}
@media(max-width:850px){.wrap{padding:12px}.summary{grid-column:span 4}.current,.preview,.guide,.timeline{grid-column:1/-1}.preview{min-height:220px}.history{overflow:auto}.top{align-items:flex-start}.brand h1{font-size:19px}.timeline canvas{height:345px}}
@media(max-width:520px){.summary{grid-column:span 6}.summary .n{font-size:23px}.card{border-radius:12px;padding:12px}.timeline canvas{height:330px}.guide canvas{height:150px}.chart-note{font-size:10px}}
</style>
</head>
<body>
<div class="wrap">
  <div class="top"><div class="brand"><h1>QualitySessionMeter</h1><div id="sub">Waiting for N.I.N.A. session data…</div></div><div class="live"><span class="dot"></span><span id="liveText">LOCAL</span></div></div>
  <div class="grid">
    <div class="card summary"><div class="label">Captured</div><div class="n" id="captured">—</div></div>
    <div class="card summary"><div class="label">Usable</div><div class="n" id="usable">—</div></div>
    <div class="card summary"><div class="label">Rejected</div><div class="n" id="rejected">—</div></div>
    <div class="card summary"><div class="label">Acceptance</div><div class="n" id="acceptance">—</div></div>
    <div class="card summary"><div class="label">Session Q</div><div class="n" id="sessionQ">—</div></div>
    <div class="card summary"><div class="label">Confidence</div><div class="n" id="sessionC">—</div></div>

    <div class="card current">
      <div class="section-title">Current frame <span id="status" class="status">—</span></div>
      <div class="row"><span>Quality</span><strong id="quality">—</strong></div>
      <div class="row"><span>Target / Filter</span><strong id="target">—</strong></div>
      <div class="row"><span>Guide RMS</span><strong id="rms">—</strong></div>
      <div class="row"><span>Stars Δ</span><strong id="stars">—</strong></div>
      <div class="row"><span>Background Δ</span><strong id="background">—</strong></div>
      <div class="row"><span>Cause</span><strong id="cause">—</strong></div>
      <div class="row"><span>File</span><strong id="file">—</strong></div>
    </div>

    <div class="card preview">
      <div class="section-title">Latest LIGHT preview</div><div class="muted" id="previewState">Waiting for an image…</div><img id="preview" alt="Latest LIGHT preview">
    </div>

    <div class="card guide">
      <div class="section-title">Live guiding · last 20 s</div>
      <div class="row"><span>Total RMS</span><strong id="grms">—</strong></div>
      <div class="row"><span>RA / DEC RMS</span><strong id="axes">—</strong></div>
      <div class="row"><span>Max excursion</span><strong id="gmax">—</strong></div>
      <div class="chart-wrap"><canvas id="guideCanvas"></canvas></div>
    </div>

    <div class="card timeline">
      <div class="section-title">Multichannel timeline</div>
      <div class="chart-note">Same model as the N.I.N.A. QSM timeline: Quality + Confidence, Guide RMS and image-signal deltas vs rolling baseline. Dashed lines are active limits; vertical markers are frame events.</div>
      <div class="chart-wrap"><canvas id="timelineCanvas"></canvas><div class="chart-tip" id="timelineTip"></div></div>
    </div>

    <div class="card history"><div class="section-title">Recent frames</div><div style="overflow:auto"><table><thead><tr><th>#</th><th>Status</th><th>Quality</th><th>Confidence</th><th>RMS</th><th>Stars Δ</th><th>BG Δ</th><th>Filter</th><th>File</th><th>Cause</th></tr></thead><tbody id="frames"></tbody></table></div></div>
  </div>
</div>
<script>
const $=id=>document.getElementById(id);
const finite=v=>v!==null&&v!==undefined&&Number.isFinite(Number(v));
const num=(v,d=1)=>finite(v)?Number(v).toFixed(d):'—';
const pct=v=>finite(v)?num(v,1)+'%':'—';
const arc=v=>finite(v)?num(v,2)+'″':'—';
let lastSnapshot=null,lastTimelineGeom=null;
function statusClass(v){return 'status '+String(v||'').toLowerCase().replace(/\s+/g,'-')}
function setupCanvas(canvas){const dpr=window.devicePixelRatio||1,w=Math.max(260,canvas.clientWidth||300),h=Math.max(120,canvas.clientHeight||180);if(canvas.width!==Math.round(w*dpr)||canvas.height!==Math.round(h*dpr)){canvas.width=Math.round(w*dpr);canvas.height=Math.round(h*dpr)}const c=canvas.getContext('2d');c.setTransform(dpr,0,0,dpr,0,0);c.clearRect(0,0,w,h);return{c,w,h}}
function line(c,x1,y1,x2,y2,color,width=1,dash=[]){c.save();c.strokeStyle=color;c.lineWidth=width;c.setLineDash(dash);c.beginPath();c.moveTo(x1,y1);c.lineTo(x2,y2);c.stroke();c.restore()}
function text(c,value,x,y,color='#a7b0bc',size=10,weight='400',align='left'){c.save();c.fillStyle=color;c.font=`${weight} ${size}px system-ui`;c.textAlign=align;c.textBaseline='top';c.fillText(value,x,y);c.restore()}
function valueY(v,min,max,top,height){return top+height*(1-(Math.min(max,Math.max(min,Number(v)))-min)/Math.max(.000001,max-min))}
function drawPath(c,frames,getter,min,max,left,top,width,height,color,lineWidth){let started=false;c.save();c.strokeStyle=color;c.lineWidth=lineWidth;c.beginPath();frames.forEach((f,i)=>{const v=getter(f);if(!finite(v)){started=false;return}const x=frames.length===1?left+width/2:left+i*width/(frames.length-1),y=valueY(v,min,max,top,height);if(!started){c.moveTo(x,y);started=true}else c.lineTo(x,y)});c.stroke();c.restore()}
function deriveCodes(f){const status=String(f?.status||'').toUpperCase();if(status==='ERROR')return'!';let t=((f?.probableCause||'')+' '+(f?.reason||'')).toUpperCase(),r='';if(/GUIDE|TRACK|WIND|RMS|EXCURSION/.test(t))r+='G';if(/STAR|CLOUD|TRANSPARENCY/.test(t))r+='S';if(/BACKGROUND|HAZE|BRIGHT SKY/.test(t))r+='B';if(status==='REJECTED'&&!r)r='?';return r}
function legend(c,x,y,maxWidth,color,name,detail){c.save();c.beginPath();c.rect(x,y-1,maxWidth,23);c.clip();c.fillStyle=color;c.beginPath();c.arc(x+5,y+6,3.2,0,Math.PI*2);c.fill();text(c,name,x+13,y,color,9,'650');text(c,detail,x+13,y+11,'#a7b0bc',8,'400');c.restore()}
function reference(c,left,width,y,color,label,bg='#11161c'){line(c,left,y,left+width,y,color,1,[5,4]);if(!label)return;c.save();c.font='600 8px system-ui';const tw=c.measureText(label).width,tx=left+width-tw-7,ty=Math.max(1,y-12);c.fillStyle=bg;c.fillRect(tx-3,ty-1,tw+6,12);text(c,label,tx,ty,color,8,'600');c.restore()}
function drawTimeline(canvas,frames,settings){const {c,w,h}=setupCanvas(canvas);const left=Math.min(215,Math.max(w<620?112:150,w*.24)),marker=36,gap=6,plotW=Math.max(1,w-left-1),bandH=Math.max(52,(h-marker-gap*2-1)/3),top=marker,rmsTop=marker+bandH+gap,imgTop=marker+2*(bandH+gap);lastTimelineGeom={left,plotW,marker,bandH,gap,w,h,frames};
  text(c,'EVENTS',3,2,'#e7ebf0',9,'650');text(c,'G guide · S sky · B background · ! error',3,16,'#a7b0bc',8);
  legend(c,3,top+3,left-8,'#8ab4f8','Quality','0–100');legend(c,3,top+25,left-8,'#c58af9','Confidence','0–100');legend(c,3,rmsTop+5,left-8,'#81c995','Guide RMS','arcsec · lower is better');legend(c,3,imgTop+3,left-8,'#fdd663','Stars Δ',settings?.enableStarCount?`% vs baseline · reject < -${num(settings.maxStarLossPercent,1)}%`:'% vs baseline · disabled');legend(c,3,imgTop+27,left-8,'#f28b82','Background Δ',settings?.enableBackground?`limits -${num(settings.maxBackgroundDecreasePercent,1)}/+${num(settings.maxBackgroundIncreasePercent,1)}%`:'% vs baseline · disabled');
  c.save();c.beginPath();c.rect(left,0,plotW,h);c.clip();
  [top,rmsTop,imgTop].forEach(y=>{c.fillStyle='#11161c';c.fillRect(left,y,plotW,bandH);c.strokeStyle='#303741';c.lineWidth=1;c.strokeRect(left+.5,y+.5,plotW-1,bandH-1);line(c,left,y+bandH/2,left+plotW,y+bandH/2,'#29313a',1)});
  text(c,'100',left+plotW-29,top+2,'#a7b0bc',8);text(c,'0',left+plotW-18,top+bandH-13,'#a7b0bc',8);
  if(!frames.length){text(c,'Waiting for assessed LIGHT frames',left+plotW/2,marker+(h-marker)/2,'#a7b0bc',10,'400','center');c.restore();return}
  drawPath(c,frames,x=>x.quality,0,100,left,top,plotW,bandH,'#8ab4f8',1.8);drawPath(c,frames,x=>x.confidence,0,100,left,top,plotW,bandH,'#c58af9',1.5);
  const obsRms=Math.max(0,...frames.map(x=>finite(x.guideRmsArcsec)?Number(x.guideRmsArcsec):0));let maxRms=Math.max(2,obsRms*1.15);if(settings?.enableGuideRms&&finite(settings.maxGuideRms)&&Number(settings.maxGuideRms)>0)maxRms=Math.max(maxRms,Number(settings.maxGuideRms)*1.30);drawPath(c,frames,x=>x.guideRmsArcsec,0,maxRms,left,rmsTop,plotW,bandH,'#81c995',1.7);if(settings?.enableGuideRms&&finite(settings.maxGuideRms)){const y=valueY(settings.maxGuideRms,0,maxRms,rmsTop,bandH);reference(c,left,plotW,y,'#81c995',`RMS limit ${num(settings.maxGuideRms,2)}″`)}text(c,`0–${num(maxRms,1)}″`,left+4,rmsTop+2,'#a7b0bc',8);
  const observed=Math.max(0,...frames.flatMap(x=>[finite(x.starDeltaPercent)?Math.abs(Number(x.starDeltaPercent)):0,finite(x.backgroundDeltaPercent)?Math.abs(Number(x.backgroundDeltaPercent)):0]));const threshold=Math.max(Number(settings?.maxStarLossPercent)||0,Number(settings?.maxBackgroundIncreasePercent)||0,Number(settings?.maxBackgroundDecreasePercent)||0);const imgMax=Math.min(500,Math.max(50,observed*1.15,threshold*1.25));drawPath(c,frames,x=>x.starDeltaPercent,-imgMax,imgMax,left,imgTop,plotW,bandH,'#fdd663',1.6);drawPath(c,frames,x=>x.backgroundDeltaPercent,-imgMax,imgMax,left,imgTop,plotW,bandH,'#f28b82',1.6);reference(c,left,plotW,valueY(0,-imgMax,imgMax,imgTop,bandH),'#87909b','0% rolling baseline');if(settings?.enableStarCount&&finite(settings.maxStarLossPercent))reference(c,left,plotW,valueY(-Number(settings.maxStarLossPercent),-imgMax,imgMax,imgTop,bandH),'#fdd663',null);if(settings?.enableBackground){if(finite(settings.maxBackgroundIncreasePercent))reference(c,left,plotW,valueY(Number(settings.maxBackgroundIncreasePercent),-imgMax,imgMax,imgTop,bandH),'#f28b82',null);if(finite(settings.maxBackgroundDecreasePercent))reference(c,left,plotW,valueY(-Number(settings.maxBackgroundDecreasePercent),-imgMax,imgMax,imgTop,bandH),'#f28b82',null)}text(c,`±${num(imgMax,0)}%`,left+4,imgTop+2,'#a7b0bc',8);
  let badgeRight=-1e9;frames.forEach((f,i)=>{const x=frames.length===1?left+plotW/2:left+i*plotW/(frames.length-1),st=String(f.status||'').toUpperCase();if(st==='REJECTED'){c.fillStyle='#ff6e691f';c.fillRect(x-2.5,marker,5,h-marker);line(c,x,marker,x,h-1,'#ff6e69',1.5);const code=deriveCodes(f);if(code){c.font='650 9px system-ui';const bw=Math.max(17,c.measureText(code).width+8),bx=x-bw/2;if(bx>badgeRight+3){c.fillStyle='#5b2429eb';c.strokeStyle='#ff827d';c.lineWidth=.8;c.beginPath();c.roundRect(bx,3,bw,15,4);c.fill();c.stroke();text(c,code,x,5,'#fff',9,'650','center');badgeRight=bx+bw}}}else if(st==='WARNING')line(c,x,marker,x,h-1,'#fdd663',1);else if(st==='ERROR'){line(c,x,marker,x,h-1,'#ff7878',1.4);if(x-8>badgeRight+3){c.fillStyle='#5b2429eb';c.beginPath();c.roundRect(x-8,3,16,15,4);c.fill();text(c,'!',x,5,'#fff',9,'650','center');badgeRight=x+8}}});c.restore()
}
function drawGuide(canvas,g){const {c,w,h}=setupCanvas(canvas),items=g?.series||[],pad=12;line(c,0,h/2,w,h/2,'#29313a',1);if(!items.length){text(c,'Waiting for PHD2 guide samples',w/2,h/2-6,'#a7b0bc',10,'400','center');return}const max=Math.max(1,...items.flatMap(x=>[Math.abs(Number(x.raArcsec)||0),Math.abs(Number(x.decArcsec)||0),Math.abs(Number(x.totalArcsec)||0)]))*1.2;const path=(getter,color)=>{let started=false;c.strokeStyle=color;c.lineWidth=1.5;c.beginPath();items.forEach((x,i)=>{const v=getter(x);if(!finite(v)){started=false;return}const px=items.length===1?w/2:i*w/(items.length-1),py=pad+(h-pad*2)*(1-(Number(v)+max)/(max*2));if(!started){c.moveTo(px,py);started=true}else c.lineTo(px,py)});c.stroke()};path(x=>x.raArcsec,'#8ab4f8');path(x=>x.decArcsec,'#f28b82');text(c,`RA`,5,4,'#8ab4f8',9,'650');text(c,`DEC`,28,4,'#f28b82',9,'650');text(c,`±${num(max,1)}″`,w-6,4,'#a7b0bc',8,'400','right')}
function timelineTip(e){const tip=$('timelineTip'),g=lastTimelineGeom;if(!g||!g.frames.length){tip.style.display='none';return}const rect=$('timelineCanvas').getBoundingClientRect(),x=e.clientX-rect.left;if(x<g.left||x>g.left+g.plotW){tip.style.display='none';return}const i=g.frames.length===1?0:Math.max(0,Math.min(g.frames.length-1,Math.round((x-g.left)/g.plotW*(g.frames.length-1)))),f=g.frames[i];tip.innerHTML=`<strong>Frame #${f.frameIndex??''} · ${f.status||''}</strong><br>Quality ${num(f.quality,0)} / 100 · Confidence ${pct(f.confidence)}<br>Guide RMS ${arc(f.guideRmsArcsec)}<br>Stars Δ ${pct(f.starDeltaPercent)} · Background Δ ${pct(f.backgroundDeltaPercent)}<br>${f.probableCause||f.reason||''}`;tip.style.display='block';tip.style.left=Math.min(rect.width-340,Math.max(4,x+12))+'px';tip.style.top=Math.max(4,e.clientY-rect.top-20)+'px'}
function render(s){lastSnapshot=s;const q=s.summary||{},f=s.currentFrame||{},g=s.guidingLive||{},frames=s.frames||[];$('captured').textContent=q.captured??0;$('usable').textContent=q.usable??0;$('rejected').textContent=q.rejected??0;$('acceptance').textContent=pct(q.acceptanceRate);$('sessionQ').textContent=num(q.sessionQuality,0);$('sessionC').textContent=num(q.sessionConfidence,0);$('status').textContent=f.status||'NO FRAME';$('status').className=statusClass(f.status);$('quality').textContent=finite(f.quality)?num(f.quality,0)+' / 100':'—';$('target').textContent=[f.target,f.filter].filter(Boolean).join(' · ')||'—';$('rms').textContent=arc(f.guideRmsArcsec);$('stars').textContent=pct(f.starDeltaPercent);$('background').textContent=pct(f.backgroundDeltaPercent);$('cause').textContent=f.probableCause||f.reason||'—';$('file').textContent=f.finalFileName||f.fileName||'—';$('grms').textContent=arc(g.rmsTotalArcsec);$('axes').textContent=arc(g.rmsRaArcsec)+' / '+arc(g.rmsDecArcsec);$('gmax').textContent=arc(g.maxExcursionArcsec);$('sub').textContent=(s.mode?.enabled?'QSM enabled':'QSM disabled')+' · '+(s.mode?.monitorOnly?'Monitor only':'Active file handling');$('frames').innerHTML=frames.slice().reverse().slice(0,30).map(x=>`<tr><td>${x.frameIndex??''}</td><td><span class="${statusClass(x.status)}">${x.status||''}</span></td><td>${num(x.quality,0)}</td><td>${num(x.confidence,0)}</td><td>${arc(x.guideRmsArcsec)}</td><td>${pct(x.starDeltaPercent)}</td><td>${pct(x.backgroundDeltaPercent)}</td><td>${x.filter||''}</td><td title="${x.finalFileName||x.fileName||''}">${x.finalFileName||x.fileName||''}</td><td title="${x.probableCause||x.reason||''}">${x.probableCause||x.reason||''}</td></tr>`).join('');drawGuide($('guideCanvas'),g);drawTimeline($('timelineCanvas'),frames,s.settings||{})}
async function refresh(){try{const r=await fetch('/dashboard/api/snapshot',{cache:'no-store'});if(r.status===401){location.reload();return}if(!r.ok)throw 0;render(await r.json());$('liveText').textContent='LIVE'}catch{$('liveText').textContent='RECONNECTING'}}
function preview(){const img=$('preview');img.src='/dashboard/api/preview.jpg?t='+Date.now();img.onload=()=>{$('previewState').textContent='Latest saved LIGHT'};img.onerror=()=>{$('previewState').textContent='Preview not available yet'}}
$('timelineCanvas').addEventListener('pointermove',timelineTip);$('timelineCanvas').addEventListener('pointerleave',()=>{$('timelineTip').style.display='none'});refresh();preview();setInterval(refresh,2000);setInterval(preview,5000);addEventListener('resize',()=>{if(lastSnapshot)render(lastSnapshot)});
</script>
</body>
</html>
""";
}
