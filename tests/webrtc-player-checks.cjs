// WebRTC lifecycle functional fixture: no browser, camera, production HTTP or device.
const assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm');
const source=fs.readFileSync('src/GimDvr/wwwroot/webrtc-stream.js','utf8');
async function settle(){for(let i=0;i<10;i++)await new Promise(r=>setImmediate(r));}
function fixture({supported=true,fail=false,defer=false}={}){
 let peer,resolveOffer,frames=0,now=0,lease=true,copies=0,copyDestroyed=0;const requests=[],timers=new Map(),intervals=new Map(),statuses=[];let seq=0;
 const video={dataset:{},srcObject:null,removeAttribute(){},load(){},play:()=>Promise.resolve()};
 class Peer {constructor(){peer=this;this.iceGatheringState='complete';this.connectionState='new';}addTransceiver(kind,options){assert.equal(kind,'video');assert.equal(options.direction,'recvonly');return{setCodecPreferences:cs=>assert.equal(cs[0].mimeType,'video/H264')};}async createOffer(){return{type:'offer',sdp:'v=0\r\nm=video 9 UDP/TLS/RTP/SAVPF 96\r\na=recvonly\r\n'};}async setLocalDescription(d){this.localDescription=d;}async setRemoteDescription(d){this.remote=d;}addEventListener(){}removeEventListener(){}close(){this.closed=true;}async getStats(){return new Map([[1,{type:'inbound-rtp',kind:'video',framesDecoded:frames}]]);}}
 const ctx=vm.createContext({console,AbortController,Date:{now:()=>now},MediaStream:class{constructor(tracks){this.tracks=tracks;}},RTCRtpReceiver:{getCapabilities:()=>({codecs:[{mimeType:'video/H264'}]})},setTimeout:(f,ms)=>{timers.set(++seq,{f,ms});return seq;},clearTimeout:id=>timers.delete(id),setInterval:(f)=>{intervals.set(++seq,f);return seq;},clearInterval:id=>intervals.delete(id),startCopyStream:()=>{copies++;return{destroy(){copyDestroyed++;}};},fetch:async(url,options)=>{
  requests.push({url,...options});if(options.method==='DELETE')return{ok:true};if(options.method==='PUT')return{ok:lease};
  if(defer)return new Promise(r=>resolveOffer=r);return{ok:!fail,json:async()=>({session:'session-a',sdp:'answer'})};
 }});if(supported)ctx.RTCPeerConnection=Peer;vm.runInContext(source,ctx);
 return{start:()=>ctx.startLiveStream(video,'a',s=>statuses.push(s)),video,requests,statuses,timers,intervals,get peer(){return peer;},get copies(){return copies;},get copyDestroyed(){return copyDestroyed;},resolve:()=>resolveOffer({ok:true,json:async()=>({session:'late',sdp:'answer'})}),frames:n=>frames=n,time:n=>now=n,lease:v=>lease=v};
}
(async()=>{
 let f=fixture({supported:false}),p=f.start();await settle();assert.equal(f.copies,1);p.destroy();assert.equal(f.copyDestroyed,1);console.log('PASS unsupported WebRTC falls back once and releases copy player');
 f=fixture({fail:true});p=f.start();await settle();assert.equal(f.copies,1);assert.equal(f.peer.closed,true);p.destroy();console.log('PASS signaling failure closes peer before fMP4 fallback');
 f=fixture();p=f.start();await settle();assert.equal(f.peer.remote.sdp,'answer');assert.equal(f.copies,0);f.peer.ontrack({track:{kind:'video'}});assert.ok(f.video.srcObject);f.frames(15);await [...f.intervals.values()][0]();assert.deepEqual(f.statuses,['live']);assert.equal([...f.timers.values()].filter(t=>t.ms===25000).length,0);
 f.lease(false);await [...f.intervals.values()][0]();assert.equal(f.copies,1);assert.equal(f.video.srcObject,null);assert.equal(f.peer.closed,true);assert.ok(f.requests.some(r=>r.method==='DELETE'&&r.url.endsWith('session-a')));p.destroy();console.log('PASS decoded frames confirm live; revoked lease tears down peer and session');
 f=fixture({defer:true});p=f.start();await settle();p.destroy();f.resolve();await settle();assert.equal(f.copies,0);assert.ok(f.requests.some(r=>r.method==='DELETE'&&r.url.endsWith('late')));assert.equal(f.intervals.size,0);console.log('PASS stop during negotiation deletes late server session without resurrecting player');
 f=fixture();p=f.start();await settle();f.time(11000);await [...f.intervals.values()][0]();assert.equal(f.copies,1);p.destroy();console.log('PASS connected but undecodable stream falls back instead of hanging indefinitely');
 const routes=fs.readFileSync('src/GimDvr/Program.cs','utf8');assert.match(routes,/MapPost\("\/api\/cameras\/\{id\}\/webrtc"[\s\S]*?RequireAuthorization\(\)/);assert.match(routes,/MapPut\("\/api\/webrtc\/\{id\}"[^\n]+RequireAuthorization/);assert.match(routes,/MapDelete\("\/api\/webrtc\/\{id\}"[^\n]+RequireAuthorization/);console.log('PASS all signaling/lease/delete routes require authentication');
})().catch(e=>{console.error(e);process.exitCode=1;});
