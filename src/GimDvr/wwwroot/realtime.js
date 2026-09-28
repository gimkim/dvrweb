'use strict';
function startRealtimeVideo(video,id,onStatus){
 let disposed=false,attempt=null,objectUrl=null;
 const sleep=ms=>new Promise(r=>setTimeout(r,ms));
 function stopAttempt(){attempt?.abort();}
 document.addEventListener('visibilitychange',stopAttempt);
 async function run(){
  while(!disposed){
   if(document.hidden){await sleep(250);continue;}
   const controller=new AbortController();attempt=controller;let watchdog,lastData=Date.now();
   let started=false,rebuffering=true,targetBuffer=1.5;
   video.autoplay=false;video.pause();video.playbackRate=1;
   const waiting=()=>{
    if(!started||controller.signal.aborted||rebuffering)return;
    rebuffering=true;targetBuffer=Math.min(3,targetBuffer+0.5);video.pause();
    onStatus('กำลังสะสมภาพเพื่อเล่นต่อ…');
   };
   video.addEventListener('waiting',waiting);
   try{
    const source=new MediaSource();objectUrl=URL.createObjectURL(source);video.src=objectUrl;
    await new Promise((resolve,reject)=>{source.addEventListener('sourceopen',resolve,{once:true});controller.signal.addEventListener('abort',()=>reject(Error('closed')),{once:true});});
    const response=await fetch(`api/cameras/${id}/live-stream`,{signal:controller.signal,cache:'no-store'});
    if(!response.ok)throw Error(`Stream HTTP ${response.status}`);
    const reader=response.body.getReader();let pending=new Uint8Array(),buffer;
    watchdog=setInterval(()=>{if(Date.now()-lastData>15000)controller.abort();},1000);
    async function packet(){
     while(pending.length<4||pending.length<4+new DataView(pending.buffer,pending.byteOffset,4).getUint32(0)){
      const {done,value}=await reader.read();if(done)throw Error('stream ended');lastData=Date.now();
      const next=new Uint8Array(pending.length+value.length);next.set(pending);next.set(value,pending.length);pending=next;
      if(pending.length>=4&&new DataView(pending.buffer,pending.byteOffset,4).getUint32(0)>16*1024*1024)throw Error('Invalid frame');
     }
     const size=new DataView(pending.buffer,pending.byteOffset,4).getUint32(0),data=pending.slice(4,4+size);pending=pending.slice(4+size);return data;
    }
    const init=await packet();let avc=-1,audio=false;
    for(let i=0;i<init.length-8;i++){const name=String.fromCharCode(...init.subarray(i,i+4));if(name==='avcC')avc=i;if(name==='mp4a')audio=true;}
    if(avc<0)throw Error('Missing H264 configuration');
    const codec='avc1.'+[init[avc+5],init[avc+6],init[avc+7]].map(x=>x.toString(16).padStart(2,'0')).join('');
    buffer=source.addSourceBuffer(`video/mp4; codecs="${codec}${audio?',mp4a.40.2':''}"`);
    async function update(action){await new Promise((resolve,reject)=>{const done=()=>{clean();resolve();},fail=()=>{clean();reject(Error('Media buffer failed'));},clean=()=>{buffer.removeEventListener('updateend',done);buffer.removeEventListener('error',fail);controller.signal.removeEventListener('abort',fail);};buffer.addEventListener('updateend',done,{once:true});buffer.addEventListener('error',fail,{once:true});controller.signal.addEventListener('abort',fail,{once:true});try{action();}catch(e){clean();reject(e);}});}
    await update(()=>buffer.appendBuffer(init));
    while(!disposed&&!controller.signal.aborted){
     const data=await packet();await update(()=>buffer.appendBuffer(data));
     if(buffer.buffered.length){
      const n=buffer.buffered.length-1,start=buffer.buffered.start(n),end=buffer.buffered.end(n),lag=end-video.currentTime;
      // Build a jitter reserve before playback; do not chase each fragment's live edge.
      if(!started){
       if(end-start<targetBuffer){onStatus('กำลังสะสมภาพก่อนเริ่ม…');continue;}
       video.currentTime=Math.max(start,end-targetBuffer);started=true;
      }else if(video.currentTime<start||lag>6){
       video.currentTime=Math.max(start,end-targetBuffer);
      }
      const available=end-video.currentTime;
      if(rebuffering&&available<targetBuffer-0.1){onStatus('กำลังสะสมภาพเพื่อเล่นต่อ…');continue;}
      rebuffering=false;video.playbackRate=1;
      if(video.paused)video.play().catch(()=>{});
      onStatus('● LIVE · บัฟเฟอร์ '+targetBuffer.toFixed(1)+' วินาที');
      if(video.currentTime>5&&buffer.buffered.start(0)<video.currentTime-5)await update(()=>buffer.remove(0,video.currentTime-3));
     }
    }
   }catch(e){if(!disposed&&!document.hidden)onStatus('กำลังเชื่อมต่อภาพสด…');}
   finally{video.removeEventListener('waiting',waiting);clearInterval(watchdog);controller.abort();if(objectUrl){URL.revokeObjectURL(objectUrl);objectUrl=null;}}
   if(!disposed)await sleep(500);
  }
 }
 run();
 return {destroy(){disposed=true;stopAttempt();document.removeEventListener('visibilitychange',stopAttempt);if(objectUrl)URL.revokeObjectURL(objectUrl);}};
}

