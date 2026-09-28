// Deterministic MSE lifecycle fixture; no browser, device, camera or HTTP access.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const {startCopyStream}=require('../src/GimDvr/wwwroot/copy-stream.js');
class Events {constructor(){this.events=new Map();}addEventListener(k,f){if(!this.events.has(k))this.events.set(k,new Set());this.events.get(k).add(f);}removeEventListener(k,f){this.events.get(k)?.delete(f);}emit(k){for(const f of [...this.events.get(k)||[]])f();}}
const turns=async()=>{for(let i=0;i<12;i++)await new Promise(r=>setImmediate(r));};
function frame(data){const h=Buffer.alloc(4);h.writeUInt32BE(data.length);return Buffer.concat([h,data]);}
const fixture=fs.readdirSync('artifacts').filter(n=>n.startsWith('copy-checks-')).sort().at(-1);
const init=fs.readFileSync(path.join('artifacts',fixture,'cache','init.mp4'));
let sb,ms,reads=[],resolveRead,ranges=[],nextRanges=[],cancels=0;
const reader={read(){return reads.length?Promise.resolve({value:reads.shift(),done:false}):new Promise(r=>resolveRead=r);},cancel(){cancels++;resolveRead?.({done:true});return Promise.resolve();}};
function send(data){const value=frame(data);if(resolveRead){const r=resolveRead;resolveRead=null;r({value,done:false});}else reads.push(value);}
class BufferMock extends Events {constructor(){super();this.removed=[];this.appends=0;this.buffered={get length(){return ranges.length;},start:i=>ranges[i][0],end:i=>ranges[i][1]};}appendBuffer(data){this.appends++;if(data.length===1)ranges=nextRanges;queueMicrotask(()=>{this.emit('updateend');video.emit('canplay');});}remove(a,b){this.removed.push([a,b]);queueMicrotask(()=>this.emit('updateend'));}}
global.MediaSource=class extends Events {constructor(){super();ms=this;queueMicrotask(()=>this.emit('sourceopen'));}static isTypeSupported(){return true;}addSourceBuffer(){sb=new BufferMock();return sb;}};
global.URL.createObjectURL=()=> 'blob:fixture';global.URL.revokeObjectURL=()=>{};
global.fetch=async(url,{signal})=>{signal.addEventListener('abort',()=>resolveRead?.({done:true}),{once:true});return {ok:true,headers:{get:n=>({'X-Startup-Ms':'900','X-Rebuffer-Ms':'600','X-Live-Target-Ms':'200'})[n]},body:{getReader:()=>reader}};};
const video=new Events();Object.assign(video,{dataset:{},isConnected:true,readyState:4,currentTime:0,paused:true,playCalls:0,pause(){if(!this.paused){this.paused=true;this.emit('pause');}},play(){this.playCalls++;this.paused=false;this.emit('playing');return Promise.resolve();}});
const doc=new Events();doc.getElementById=()=>null;
const full={setAttribute(){}};const wrap={querySelector:q=>q==='video'?video:q==='[data-live-fullscreen]'?full:null,closest:()=>({dataset:{viewing:'true'}}),contains:()=>false};
const controls=vm.runInNewContext(fs.readFileSync('src/GimDvr/wwwroot/live-controls.js','utf8')+';bindLiveControls(wrap)',{document:doc,window:{},wrap,Event});
const statuses=[];const player=startCopyStream(video,'fixture',s=>statuses.push(s));
async function append(r){nextRanges=r;send(Buffer.from([1]));await turns();}
(async()=>{try{
 await turns();send(init);await turns();
 await append([[100,100.3]]);assert.equal(video.playCalls,0);
 await append([[100,100.6]]);assert.equal(video.playCalls,0);
 await append([[100,100.9]]);assert.equal(video.playCalls,1);assert.ok(Math.abs(video.currentTime-100)<0.001);
 console.log('PASS startup900ms is retained despite live target200ms');
 video.currentTime=100.9;video.emit('waiting');assert.equal(video.paused,true);const plays=video.playCalls;
 await append([[100,101.05]]);await append([[100,101.2]]);await append([[100,101.35]]);assert.equal(video.playCalls,plays);assert.equal(video.paused,true);
 await append([[100,101.5]]);assert.equal(video.playCalls,plays+1);assert.equal(video.paused,false);
 console.log('PASS starvation pauses through three150ms fragments and resumes only at600ms');
 const statusCount=statuses.length;video.currentTime=101.1;await append([[100,101.65]]);assert.equal(statuses.length,statusCount);assert.equal(video.playCalls,plays+1);
 console.log('PASS ordinary segment appends neither restart playback nor announce live');
 const before=video.currentTime;await append([[100,101.65],[103,103.15]]);assert.equal(video.currentTime,before);
 video.currentTime=101.65;video.emit('waiting');await append([[100,101.65],[103,103.3]]);assert.equal(video.paused,true);assert.equal(video.currentTime,101.65);
 await append([[100,101.65],[103,103.6]]);assert.ok(Math.abs(video.currentTime-103)<0.001);assert.equal(video.paused,false);
 console.log('PASS disjoint range does not cause per-append seeking; gap recovery waits for reserve');
 assert.equal(sb.removed.length,0);
 const continuousPlays=video.playCalls,continuousStatuses=statuses.length;
 for(let i=0;i<400;i++){
  const end=104+i*0.15;video.currentTime=end-0.75;const expected=video.currentTime;
  await append([[100,end]]);assert.equal(video.currentTime,expected);assert.equal(video.playbackRate,1);
 }
 assert.equal(video.playCalls,continuousPlays);assert.equal(statuses.length,continuousStatuses);
 assert.ok(sb.removed.length>0&&sb.removed.length<=12);for(const [a,b] of sb.removed){assert.equal(a,0);assert.ok(b>100);}
 console.log('PASS400 successive150ms fragments preserve playhead, speed and play state; history eviction is throttled');
 player.destroy();await turns();assert.equal(video.events.get('waiting').size,0);assert.equal(video.events.get('playing').size,0);assert.equal(cancels,1);
 controls.destroy();console.log('PASS destroy cancels reader and removes player listeners; controls respect player pause/canplay ownership');
 }finally{player.destroy();}
})().catch(e=>{console.error(e);process.exitCode=1;});
