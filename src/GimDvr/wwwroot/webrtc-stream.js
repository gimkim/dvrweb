'use strict';

// Shared web/Android player: H.265/H.264 WebRTC only; never start a fallback transport.
function startLiveStream(video,id,onStatus){
 let stopped=false,active=null,retry=null,attempts=0;
 function connect(){
  if(stopped)return;
  active=startWebRtcAttempt(video,id,status=>{if(stopped)return;if(status==='live')attempts=0;onStatus(status);},reason=>{
   if(stopped||reason==='unsupported-codec'||/^(offer-http-|lease-http-)(401|403)$/.test(reason))return;
   const delay=Math.min(15000,3000*Math.pow(2,Math.min(attempts++,3)));
   retry=setTimeout(()=>{retry=null;if(!stopped)connect();},delay);
  });
 }
 connect();
 return{destroy(){if(stopped)return;stopped=true;clearTimeout(retry);active?.destroy();active=null;}};
}
function startWebRtcAttempt(video,id,onStatus,onFailure){
 let stopped=false,failed=false,pc=null,session=null,timer=null,heartbeat=null,checking=false,lastFrameAt=Date.now(),lastFrames=0,frameRequest=null,firstFrameMs=null;
 const begin=Date.now();const trace=(stage,detail='')=>{video.dataset.rtcStage=stage;video.dataset.rtcElapsedMs=String(Date.now()-begin);video.dataset.rtcDetail=detail;console.info('[GimDVR WebRTC]',id,stage,Date.now()-begin,detail);};
 const controller=new AbortController();
 const endpoint='api/webrtc/';
 const release=token=>fetch(endpoint+encodeURIComponent(token),{method:'DELETE',headers:{'X-DVR-Request':'1'},keepalive:true}).catch(()=>{});
 function cleanup(){if(frameRequest!==null)video.cancelVideoFrameCallback?.(frameRequest);clearTimeout(timer);clearInterval(heartbeat);if(pc){pc.ontrack=pc.onconnectionstatechange=null;pc.close();pc=null;}video.srcObject=null;if(session){release(session);session=null;}}
 function failStream(reason='startup-timeout'){if(stopped||failed)return;failed=true;trace('error',String(reason));controller.abort();cleanup();video.dataset.transport='webrtc';video.dataset.starting='false';onStatus(reason==='unsupported-codec'?'อุปกรณ์นี้ไม่รองรับวิดีโอผ่าน WebRTC':/^(offer-http-|lease-http-)(401|403)$/.test(reason)?'ไม่มีสิทธิ์ดูภาพสด กรุณาเข้าสู่ระบบใหม่':'กำลังเชื่อมต่อใหม่');onFailure(String(reason));}
 async function negotiate(){
  if(typeof RTCPeerConnection==='undefined'){failStream('unsupported-codec');return;}
  try{
   pc=new RTCPeerConnection({iceServers:[],bundlePolicy:'max-bundle'});
   const peer=pc,receiver=peer.addTransceiver('video',{direction:'recvonly'});
   // Camera packets arrive in bursts; this is a browser hint, not a hard latency cap.
   try{if('jitterBufferTarget' in receiver.receiver)receiver.receiver.jitterBufferTarget=400;}catch{}
   const codecs=typeof RTCRtpReceiver!=='undefined'?RTCRtpReceiver.getCapabilities('video')?.codecs:null;
   if(!codecs||!receiver.setCodecPreferences){failStream('unsupported-codec');return;}
   if(codecs&&receiver.setCodecPreferences){const compatible=codecs.filter(c=>['video/h265','video/h264'].includes(c.mimeType.toLowerCase())).sort((a,b)=>Number(b.mimeType.toLowerCase()==='video/h265')-Number(a.mimeType.toLowerCase()==='video/h265'));if(!compatible.length){failStream('unsupported-codec');return;}receiver.setCodecPreferences(compatible);}
   video.muted=true;video.playbackRate=1;video.dataset.transport='webrtc';
   peer.ontrack=e=>{if(stopped||failed)return;video.srcObject=new MediaStream([e.track]);frameRequest=video.requestVideoFrameCallback?.(()=>{if(stopped||failed)return;firstFrameMs=Date.now()-begin;lastFrameAt=Date.now();clearTimeout(timer);trace('first-frame');});video.play().catch(()=>{});};
   peer.onconnectionstatechange=()=>{trace('peer',peer.connectionState);if(peer.connectionState==='connected'){clearTimeout(timer);timer=setTimeout(()=>failStream('no-decoded-frames'),10000);}if(peer.connectionState==='failed'||peer.connectionState==='closed')failStream('peer-'+peer.connectionState);};
   timer=setTimeout(()=>failStream('offer-timeout'),18000);
   await peer.setLocalDescription(await peer.createOffer());
   await new Promise(resolve=>{
    let wait;const done=()=>{clearTimeout(wait);peer.removeEventListener('icegatheringstatechange',changed);controller.signal.removeEventListener('abort',done);resolve();};
    const changed=()=>{if(peer.iceGatheringState==='complete')done();};
    wait=setTimeout(done,2500);peer.addEventListener('icegatheringstatechange',changed);controller.signal.addEventListener('abort',done,{once:true});changed();if(controller.signal.aborted)done();
   });
   if(stopped||failed)return;
   const response=await fetch(`api/cameras/${encodeURIComponent(id)}/webrtc`,{method:'POST',headers:{'Content-Type':'application/json','X-DVR-Request':'1'},body:JSON.stringify({sdp:peer.localDescription.sdp}),signal:controller.signal});
   if(!response.ok)throw Error('offer-http-'+response.status);
   trace('answer');clearTimeout(timer);timer=setTimeout(()=>failStream('ice-timeout'),4000);
   const result=await response.json();
   if(stopped||failed){if(result.session)release(result.session);return;}
   session=result.session;await peer.setRemoteDescription({type:'answer',sdp:result.sdp});
   lastFrameAt=Date.now();
   heartbeat=setInterval(async()=>{
    if(checking||stopped||failed)return;checking=true;
    try{
     const reply=await fetch(endpoint+encodeURIComponent(session),{method:'PUT',headers:{'X-DVR-Request':'1'},signal:controller.signal});if(!reply.ok)throw Error('lease-http-'+reply.status);
     const stats=await peer.getStats();if(stopped||failed)return;let frames=0;stats.forEach(s=>{if(s.type==='inbound-rtp'&&(s.kind==='video'||s.mediaType==='video'))frames+=s.framesDecoded||0;});
     frames=Math.max(frames,video.getVideoPlaybackQuality?.().totalVideoFrames||0);
     const metrics={frames,firstFrameMs,at:Date.now(),state:peer.connectionState,jitterTargetMs:receiver.receiver?.jitterBufferTarget??null};
     stats.forEach(s=>{if(s.type==='inbound-rtp'&&(s.kind==='video'||s.mediaType==='video')){metrics.received=s.framesReceived;metrics.keyFrames=s.keyFramesDecoded;metrics.bytes=s.bytesReceived;metrics.pli=s.pliCount;metrics.dropped=s.framesDropped;metrics.freezes=s.freezeCount;metrics.freezeSeconds=s.totalFreezesDuration;metrics.packetsLost=s.packetsLost;metrics.jitterMs=Math.round((s.jitter||0)*1000);metrics.jitterBufferMs=s.jitterBufferEmittedCount?Math.round(s.jitterBufferDelay/s.jitterBufferEmittedCount*1000):0;}if(s.type==='transport'&&s.selectedCandidatePairId){const pair=stats.get(s.selectedCandidatePairId);metrics.rttMs=Math.round((pair?.currentRoundTripTime||0)*1000);metrics.protocol=stats.get(pair?.localCandidateId)?.protocol;}});
     video.dataset.rtcDiagnostics=JSON.stringify(metrics);
     if(frames>lastFrames){lastFrames=frames;lastFrameAt=Date.now();clearTimeout(timer);onStatus('live');}
     else if(Date.now()-lastFrameAt>10000)failStream('no-decoded-frames');
    }catch(e){failStream(e.message);}finally{checking=false;}
   },3000);
  }catch(e){failStream(e.message);}
 }
 negotiate();
 return{destroy(){if(stopped)return;stopped=true;controller.abort();cleanup();}};
}
if(typeof module!=='undefined')module.exports={startLiveStream};
