'use strict';
let rtcNetworkRetryAt=0;
// Shared web/Android player: WebRTC first, one copy-stream fallback per viewing session.
function startLiveStream(video,id,onStatus){
 let stopped=false,fallback=null,pc=null,session=null,timer=null,heartbeat=null,checking=false,lastFrameAt=Date.now(),lastFrames=0,frameRequest=null,firstFrameMs=null;
 const begin=Date.now();const trace=(stage,detail='')=>{video.dataset.rtcStage=stage;video.dataset.rtcElapsedMs=String(Date.now()-begin);video.dataset.rtcDetail=detail;console.info('[GimDVR WebRTC]',id,stage,Date.now()-begin,detail);};
 const controller=new AbortController();
 const endpoint='api/webrtc/';
 const release=token=>fetch(endpoint+encodeURIComponent(token),{method:'DELETE',headers:{'X-DVR-Request':'1'},keepalive:true}).catch(()=>{});
 function cleanup(){if(frameRequest!==null)video.cancelVideoFrameCallback?.(frameRequest);clearTimeout(timer);clearInterval(heartbeat);if(pc){pc.ontrack=pc.onconnectionstatechange=null;pc.close();pc=null;}video.srcObject=null;if(session){release(session);session=null;}}
 function useFallback(reason='startup-timeout'){if(stopped||fallback)return;trace('fallback',String(reason));if(reason==='ice-timeout'||reason==='peer-failed')rtcNetworkRetryAt=Date.now()+60000;controller.abort();cleanup();video.dataset.transport='fmp4';video.removeAttribute('src');video.load();fallback=startCopyStream(video,id,onStatus);}
 async function negotiate(){
  if(typeof RTCPeerConnection==='undefined'||Date.now()<rtcNetworkRetryAt){useFallback('unavailable-or-network-cooldown');return;}
  try{
   pc=new RTCPeerConnection({iceServers:[],bundlePolicy:'max-bundle'});
   const peer=pc,receiver=peer.addTransceiver('video',{direction:'recvonly'});
   // Camera packets arrive in bursts; this is a browser hint, not a hard latency cap.
   try{if('jitterBufferTarget' in receiver.receiver)receiver.receiver.jitterBufferTarget=400;}catch{}
   const codecs=typeof RTCRtpReceiver!=='undefined'?RTCRtpReceiver.getCapabilities('video')?.codecs:null;
   if(codecs&&receiver.setCodecPreferences){const h264=codecs.filter(c=>c.mimeType.toLowerCase()==='video/h264');if(!h264.length){useFallback();return;}receiver.setCodecPreferences(h264);}
   video.muted=true;video.playbackRate=1;video.dataset.transport='webrtc';
   peer.ontrack=e=>{if(stopped||fallback)return;video.srcObject=new MediaStream([e.track]);frameRequest=video.requestVideoFrameCallback?.(()=>{if(stopped||fallback)return;firstFrameMs=Date.now()-begin;lastFrameAt=Date.now();clearTimeout(timer);trace('first-frame');});video.play().catch(()=>{});};
   peer.onconnectionstatechange=()=>{trace('peer',peer.connectionState);if(peer.connectionState==='connected'){clearTimeout(timer);timer=setTimeout(()=>useFallback('no-decoded-frames'),10000);}if(peer.connectionState==='failed'||peer.connectionState==='closed')useFallback('peer-'+peer.connectionState);};
   timer=setTimeout(()=>useFallback('offer-timeout'),18000);
   await peer.setLocalDescription(await peer.createOffer());
   await new Promise(resolve=>{
    let wait;const done=()=>{clearTimeout(wait);peer.removeEventListener('icegatheringstatechange',changed);controller.signal.removeEventListener('abort',done);resolve();};
    const changed=()=>{if(peer.iceGatheringState==='complete')done();};
    wait=setTimeout(done,2500);peer.addEventListener('icegatheringstatechange',changed);controller.signal.addEventListener('abort',done,{once:true});changed();if(controller.signal.aborted)done();
   });
   if(stopped||fallback)return;
   const response=await fetch(`api/cameras/${encodeURIComponent(id)}/webrtc`,{method:'POST',headers:{'Content-Type':'application/json','X-DVR-Request':'1'},body:JSON.stringify({sdp:peer.localDescription.sdp}),signal:controller.signal});
   if(!response.ok)throw Error('offer-http-'+response.status);
   trace('answer');clearTimeout(timer);timer=setTimeout(()=>useFallback('ice-timeout'),4000);
   const result=await response.json();
   if(stopped||fallback){if(result.session)release(result.session);return;}
   session=result.session;await peer.setRemoteDescription({type:'answer',sdp:result.sdp});
   lastFrameAt=Date.now();
   heartbeat=setInterval(async()=>{
    if(checking||stopped||fallback)return;checking=true;
    try{
     const reply=await fetch(endpoint+encodeURIComponent(session),{method:'PUT',headers:{'X-DVR-Request':'1'},signal:controller.signal});if(!reply.ok)throw Error('WebRTC lease expired');
     const stats=await peer.getStats();if(stopped||fallback)return;let frames=0;stats.forEach(s=>{if(s.type==='inbound-rtp'&&(s.kind==='video'||s.mediaType==='video'))frames+=s.framesDecoded||0;});
     frames=Math.max(frames,video.getVideoPlaybackQuality?.().totalVideoFrames||0);
     const metrics={frames,firstFrameMs,at:Date.now(),state:peer.connectionState,jitterTargetMs:receiver.receiver?.jitterBufferTarget??null};
     stats.forEach(s=>{if(s.type==='inbound-rtp'&&(s.kind==='video'||s.mediaType==='video')){metrics.received=s.framesReceived;metrics.keyFrames=s.keyFramesDecoded;metrics.bytes=s.bytesReceived;metrics.pli=s.pliCount;metrics.dropped=s.framesDropped;metrics.freezes=s.freezeCount;metrics.freezeSeconds=s.totalFreezesDuration;metrics.packetsLost=s.packetsLost;metrics.jitterMs=Math.round((s.jitter||0)*1000);metrics.jitterBufferMs=s.jitterBufferEmittedCount?Math.round(s.jitterBufferDelay/s.jitterBufferEmittedCount*1000):0;}if(s.type==='transport'&&s.selectedCandidatePairId){const pair=stats.get(s.selectedCandidatePairId);metrics.rttMs=Math.round((pair?.currentRoundTripTime||0)*1000);metrics.protocol=stats.get(pair?.localCandidateId)?.protocol;}});
     video.dataset.rtcDiagnostics=JSON.stringify(metrics);
     if(frames>lastFrames){lastFrames=frames;lastFrameAt=Date.now();clearTimeout(timer);onStatus('live');}
     else if(Date.now()-lastFrameAt>10000)useFallback('no-decoded-frames');
    }catch(e){useFallback(e.message);}finally{checking=false;}
   },3000);
  }catch(e){useFallback(e.message);}
 }
 negotiate();
 return{destroy(){if(stopped)return;stopped=true;controller.abort();cleanup();fallback?.destroy();}};
}
if(typeof module!=='undefined')module.exports={startLiveStream};
