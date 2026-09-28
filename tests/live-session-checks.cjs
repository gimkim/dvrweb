// Tests cancellation and ownership without a browser, camera or network connection.
const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const source=fs.readFileSync('src/GimDvr/wwwroot/app.js','utf8');
const connect=source.slice(source.indexOf('async function connectLive('),source.indexOf('window.onLiveFullscreenChanged='));
const pending=[],encoders=[],intervals=new Set(),videos=new Map();let id=0;
class FakeHls {
 static isSupported(){return true;}static Events={MANIFEST_PARSED:'manifest',ERROR:'error'};static ErrorTypes={MEDIA_ERROR:'media'};
 constructor(){this.destroyed=false;encoders.push(this);}loadSource(){}attachMedia(v){this.video=v;}on(){}destroy(){this.destroyed=true;}
}
const context=vm.createContext({Hls:FakeHls,Map,Promise,document:{getElementById:id=>{if(!videos.has(id))videos.set(id,{addEventListener(){},removeEventListener(){},removeAttribute(){},load(){},play(){return Promise.resolve();}});return videos.get(id);}},api:()=>new Promise((resolve,reject)=>pending.push({resolve,reject})),isViewing:()=>true,liveMessage(){},setInterval:()=>{intervals.add(++id);return id;},clearInterval:id=>intervals.delete(id),setTimeout(){},clearTimeout(){}});
vm.runInContext('const liveSessions=new Map();'+connect+';globalThis.sessions=liveSessions;',context);
(async()=>{
 const old=context.connectLive({id:'a'});context.sessions.get('a').destroy();pending.shift().resolve();await old;
 assert.equal(encoders.length,0);assert.equal(intervals.size,0);console.log('PASS stop during watch request creates no player or heartbeat');
 const failed=context.connectLive({id:'a'});context.sessions.get('a').destroy();pending.shift().reject(Error('late'));await failed;
 console.log('PASS late failed request after stop is ignored');
 for(const camera of ['a','b','c']){const p=context.connectLive({id:camera});pending.shift().resolve();await p;}
 assert.equal(intervals.size,3);context.sessions.get('b').destroy();assert.equal(intervals.size,2);assert.equal(encoders[1].destroyed,true);assert.equal(encoders[0].destroyed,false);assert.equal(encoders[2].destroyed,false);
 console.log('PASS stopping one camera leaves other two players and heartbeats alive');
 const replacement=context.connectLive({id:'a'});assert.equal(encoders[0].destroyed,true);pending.shift().resolve();await replacement;assert.equal(intervals.size,2);
 context.sessions.forEach(s=>s.destroy());assert.equal(intervals.size,0);assert.equal(context.sessions.size,0);assert.ok(encoders.every(e=>e.destroyed));
 console.log('PASS reconnect replaces one session and shutdown clears all owned resources');
})().catch(e=>{console.error(e);process.exitCode=1;});
