// Tests cancellation and ownership without a browser, camera or network connection.
const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const source=fs.readFileSync('src/GimDvr/wwwroot/app.js','utf8');
const connect=source.slice(source.indexOf('async function connectLive('),source.indexOf('window.onLiveFullscreenChanged='));
const pending=[],encoders=[],intervals=new Set(),videos=new Map();let id=0;
class FakeHls {
 static isSupported(){return true;}static Events={MANIFEST_PARSED:'manifest',ERROR:'error'};static ErrorTypes={MEDIA_ERROR:'media'};
 constructor(options){this.options=options;this.destroyed=false;encoders.push(this);}loadSource(src){this.src=src;}attachMedia(v){this.video=v;}on(){}destroy(){this.destroyed=true;}
}
const context=vm.createContext({Hls:FakeHls,Map,Promise,document:{getElementById:id=>{if(!videos.has(id))videos.set(id,{addEventListener(){},removeEventListener(){},removeAttribute(){},load(){},play(){return Promise.resolve();}});return videos.get(id);}},api:()=>new Promise((resolve,reject)=>pending.push({resolve,reject})),isViewing:()=>true,liveMessage(){},setInterval:()=>{intervals.add(++id);return id;},clearInterval:id=>intervals.delete(id),setTimeout(){},clearTimeout(){}});
vm.runInContext('const liveSessions=new Map();'+connect+';globalThis.sessions=liveSessions;',context);
(async()=>{
 const old=context.connectLive({id:'a'});context.sessions.get('a').destroy();pending.shift().resolve();await old;
 assert.equal(encoders.length,0);assert.equal(intervals.size,0);console.log('PASS stop during watch request creates no player or heartbeat');
 const failed=context.connectLive({id:'a'});context.sessions.get('a').destroy();pending.shift().reject(Error('late'));await failed;
 console.log('PASS late failed request after stop is ignored');
 for(const camera of ['a','b','c']){const p=context.connectLive({id:camera});pending.shift().resolve();await p;}
 const overview=encoders[0].options;assert.equal(overview.liveSyncDurationCount,1);assert.equal(overview.liveMaxLatencyDurationCount,2);assert.equal(overview.liveSyncOnStallIncrease,0);assert.equal(overview.maxBufferLength,4);assert.equal(overview.maxLiveSyncPlaybackRate,1.1);assert.equal(overview.liveSyncDurationCount*4,4);
 console.log('PASS four-second source segments target four seconds instead of eight, with bounded catch-up and no stall latency growth');
 assert.equal(intervals.size,3);context.sessions.get('b').destroy();assert.equal(intervals.size,2);assert.equal(encoders[1].destroyed,true);assert.equal(encoders[0].destroyed,false);assert.equal(encoders[2].destroyed,false);
 console.log('PASS stopping one camera leaves other two players and heartbeats alive');
 const replacement=context.connectLive({id:'a'});assert.equal(encoders[0].destroyed,true);pending.shift().resolve();await replacement;assert.equal(intervals.size,2);
 context.sessions.forEach(s=>s.destroy());assert.equal(intervals.size,0);assert.equal(context.sessions.size,0);assert.ok(encoders.every(e=>e.destroyed));
 console.log('PASS reconnect replaces one session and shutdown clears all owned resources');
 context.isViewing=()=>false;context.document.querySelector=()=>null;const focus=context.connectLive({id:'a'},'focus');pending.shift().resolve();await focus;
 assert.equal(encoders.at(-1).src,'api/focus/a/index.m3u8');assert.equal(encoders.at(-1).options.liveSyncDuration,0.7);context.sessions.forEach(s=>s.destroy());
 console.log('PASS single-camera focus uses distinct low-buffer stream even when overview preference is off');
})().catch(e=>{console.error(e);process.exitCode=1;});
