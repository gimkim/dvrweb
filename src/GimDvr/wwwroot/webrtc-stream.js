'use strict';
// Shared web/Android player: WebRTC first, one copy-stream fallback per viewing session.
function startLiveStream(video,id,onStatus){
 let stopped=false,fallback=null,pc=null,session=null,timer=null,heartbeat=null,checking=false,lastFrameAt=Date.now(),lastFrames=0;
 const controller=new AbortController();
 const endpoint='api/webrtc/';
 const release=token=>fetch(endpoint+encodeURIComponent(token),{method:'DELETE',headers:{'X-DVR-Request':'1'},keepalive:true}).catch(()=>{});
 function cleanup(){clearTimeout(timer);clearInterval(heartbeat);if(pc){pc.ontrack=pc.onconnectionstatechange=null;pc.close();pc=null;}video.srcObject=null;if(session){release(session);session=null;}}
 function useFallback(){if(stopped||fallback)return;controller.abort();cleanup();video.dataset.transport='fmp4';video.removeAttribute('src');video.load();fallback=startCopyStream(video,id,onStatus);}
 async function negotiate(){
  if(typeof RTCPeerConnection==='undefined'){useFallback();return;}
  try{
   pc=new RTCPeerConnection({iceServers:[],bundlePolicy:'max-bundle'});
   const peer=pc,receiver=peer.addTransceiver('video',{direction:'recvonly'});
   const codecs=typeof RTCRtpReceiver!=='undefined'?RTCRtpReceiver.getCapabilities('video')?.codecs:null;
   if(codecs&&receiver.setCodecPreferences){const h264=codecs.filter(c=>c.mimeType.toLowerCase()==='video/h264');if(!h264.length){useFallback();return;}receiver.setCodecPreferences(h264);}
   video.muted=true;video.playbackRate=1;video.dataset.transport='webrtc';
   peer.ontrack=e=>{if(stopped||fallback)return;video.srcObject=new MediaStream([e.track]);video.play().catch(()=>{});};
   peer.onconnectionstatechange=()=>{if(peer.connectionState==='failed'||peer.connectionState==='closed')useFallback();};
   timer=setTimeout(useFallback,25000);
   await peer.setLocalDescription(await peer.createOffer());
   await new Promise(resolve=>{
    let wait;const done=()=>{clearTimeout(wait);peer.removeEventListener('icegatheringstatechange',changed);controller.signal.removeEventListener('abort',done);resolve();};
    const changed=()=>{if(peer.iceGatheringState==='complete')done();};
    wait=setTimeout(done,2500);peer.addEventListener('icegatheringstatechange',changed);controller.signal.addEventListener('abort',done,{once:true});changed();if(controller.signal.aborted)done();
   });
   if(stopped||fallback)return;
   const response=await fetch(`api/cameras/${encodeURIComponent(id)}/webrtc`,{method:'POST',headers:{'Content-Type':'application/json','X-DVR-Request':'1'},body:JSON.stringify({sdp:peer.localDescription.sdp}),signal:controller.signal});
   if(!response.ok)throw Error('WebRTC unavailable');
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
     if(frames>lastFrames){lastFrames=frames;lastFrameAt=Date.now();clearTimeout(timer);onStatus('live');}
     else if(Date.now()-lastFrameAt>10000)useFallback();
    }catch{useFallback();}finally{checking=false;}
   },3000);
  }catch{useFallback();}
 }
 negotiate();
 return{destroy(){if(stopped)return;stopped=true;controller.abort();cleanup();fallback?.destroy();}};
}
if(typeof module!=='undefined')module.exports={startLiveStream};
