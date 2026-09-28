'use strict';
class CopyPacketReader {
 constructor(reader){this.reader=reader;this.pending=new Uint8Array();}
 async next(){
  for(;;){
   if(this.pending.length>=4){const size=new DataView(this.pending.buffer,this.pending.byteOffset,4).getUint32(0);if(size===0||size>16*1024*1024)throw Error('Invalid live packet');if(this.pending.length>=size+4){const packet=this.pending.slice(4,size+4);this.pending=this.pending.slice(size+4);return packet;}}
   const {done,value}=await this.reader.read();if(done)throw Error('Live stream ended');const joined=new Uint8Array(this.pending.length+value.length);joined.set(this.pending);joined.set(value,this.pending.length);this.pending=joined;
  }
 }
}
function copyVideoCodec(init){
 for(let i=4;i+8<init.length;i++)if(init[i]===97&&init[i+1]===118&&init[i+2]===99&&init[i+3]===67){
  const size=new DataView(init.buffer,init.byteOffset+i-4,4).getUint32(0),end=i-4+size;
  const invalid=()=>{throw Error('ข้อมูลเริ่มต้น H.264 ไม่มี SPS/PPS ที่สมบูรณ์');};
  if(size<15||end>init.length||init[i+4]!==1)invalid();
  let p=i+10;const sps=init[i+9]&31;if(!sps)invalid();
  function parameters(count){for(let n=0;n<count;n++){if(p+2>end)invalid();const length=(init[p]<<8)|init[p+1];p+=2;if(!length||p+length>end)invalid();p+=length;}}
  parameters(sps);if(p>=end)invalid();const pps=init[p++];if(!pps)invalid();parameters(pps);
  return 'avc1.'+[init[i+5],init[i+6],init[i+7]].map(b=>b.toString(16).padStart(2,'0')).join('');
 }
 throw Error('สตรีมนี้ไม่มี H.264 configuration ที่รองรับ');
}
function startCopyStream(video,id,onStatus){
 if(typeof MediaSource==='undefined')throw Error('เบราว์เซอร์นี้ไม่รองรับสตรีมต่อเนื่อง');
 let disposed=false,attempt=null,url=null,wake=null;
 const diag={startedAt:Date.now(),firstPlayingMs:null,waits:0,plays:0,packets:0,maxPacketGapMs:0,seeks:0,restarts:0};let lastPacketAt=0,lastReport=0;
 const report=()=>{if(Date.now()-lastReport<1000)return;lastReport=Date.now();const q=video.getVideoPlaybackQuality?.();video.dataset.copyDiagnostics=JSON.stringify({...diag,currentTime:video.currentTime,paused:video.paused,readyState:video.readyState,frames:q?.totalVideoFrames,dropped:q?.droppedVideoFrames,at:Date.now()});};
 const retryDelay=()=>new Promise(resolve=>{const timer=setTimeout(()=>{wake=null;resolve();},500);wake=()=>{clearTimeout(timer);wake=null;resolve();};});
 async function run(){
  while(!disposed){
   const controller=new AbortController();attempt=controller;let buffer=null,started=false,rebuffer=false,lastData=Date.now(),reader=null;let watchdog,lastTrim=0;
   const source=new MediaSource();url=URL.createObjectURL(source);video.src=url;video.muted=true;video.autoplay=false;video.playbackRate=1;
   const waiting=()=>{if(started&&!rebuffer){diag.waits++;report();rebuffer=true;video.pause();video.playbackRate=1;onStatus('buffering');}};
   const playing=()=>{diag.plays++;diag.firstPlayingMs??=Date.now()-diag.startedAt;report();if(!rebuffer&&!disposed)onStatus('live');};
   video.addEventListener('waiting',waiting);video.addEventListener('playing',playing);
   try{
    await new Promise((resolve,reject)=>{const clean=()=>{source.removeEventListener('sourceopen',opened);controller.signal.removeEventListener('abort',aborted);};const opened=()=>{clean();resolve();},aborted=()=>{clean();reject(Error('closed'));};source.addEventListener('sourceopen',opened);controller.signal.addEventListener('abort',aborted,{once:true});if(controller.signal.aborted)aborted();});
    onStatus('buffering');
    watchdog=setInterval(()=>{if(Date.now()-lastData>(started?10000:30000))controller.abort();},1000);
    const response=await fetch(`api/cameras/${encodeURIComponent(id)}/copy-stream`,{signal:controller.signal,cache:'no-store'});
    if(!response.ok)throw Error(`Stream HTTP ${response.status}`);
    const setting=(name)=>{const value=Number(response.headers.get(name));return Number.isFinite(value)&&value>=50&&value<=5000?value/1000:0.3;};
    const startup=setting("X-Startup-Ms"),resume=setting("X-Rebuffer-Ms"),target=setting("X-Live-Target-Ms");
    const initialReserve=Math.max(startup,target),resumeReserve=Math.max(resume,target),steadyReserve=Math.max(initialReserve,resumeReserve);
    reader=response.body.getReader();const packets=new CopyPacketReader(reader),init=await packets.next();lastData=Date.now();
    const mime=`video/mp4; codecs="${copyVideoCodec(init)}"`;if(!MediaSource.isTypeSupported(mime))throw Error('เบราว์เซอร์ไม่รองรับรูปแบบภาพของกล้อง');
    buffer=source.addSourceBuffer(mime);
    async function update(action){
     if(controller.signal.aborted)throw Error('closed');
     await new Promise((resolve,reject)=>{const clean=()=>{buffer.removeEventListener('updateend',done);buffer.removeEventListener('error',fail);controller.signal.removeEventListener('abort',fail);};const done=()=>{clean();resolve();},fail=()=>{clean();reject(Error('Media buffer failed'));};buffer.addEventListener('updateend',done,{once:true});buffer.addEventListener('error',fail,{once:true});controller.signal.addEventListener('abort',fail,{once:true});try{action();}catch(e){clean();reject(e);}});
    }
    await update(()=>buffer.appendBuffer(init));
    while(!disposed&&!controller.signal.aborted){
     const data=await packets.next();lastData=Date.now();diag.packets++;if(lastPacketAt)diag.maxPacketGapMs=Math.max(diag.maxPacketGapMs,lastData-lastPacketAt);lastPacketAt=lastData;await update(()=>buffer.appendBuffer(data));
     if(!buffer.buffered.length)continue;
     // Use the range containing the playhead, not a newer disjoint range on every append.
     const ranges=buffer.buffered;let range=-1;
     for(let i=0;i<ranges.length;i++)if(video.currentTime>=ranges.start(i)&&video.currentTime<=ranges.end(i)){range=i;break;}
     if(!started){
      range=ranges.length-1;const start=ranges.start(range),end=ranges.end(range);
      if(end-start+0.001<initialReserve)continue;
      video.currentTime=Math.max(start,end-initialReserve);started=true;
     }else if(range<0||(rebuffer&&range<ranges.length-1&&ranges.end(range)-video.currentTime<0.05)){
      // Recover a real timestamp gap only once enough data exists beyond it.
      range=ranges.length-1;const start=ranges.start(range),end=ranges.end(range);
      if(end-start+0.001<resumeReserve)continue;
      diag.seeks++;video.currentTime=Math.max(start,end-resumeReserve);
     }
     const end=ranges.end(range),ahead=end-video.currentTime;diag.aheadMs=Math.round(ahead*1000);diag.ranges=ranges.length;report();
     if(rebuffer&&ahead+0.001<resumeReserve)continue;
     const reserve=rebuffer?resumeReserve:steadyReserve;
     rebuffer=false;
     if(ahead>Math.max(2,reserve+1.7)){diag.seeks++;video.currentTime=Math.max(ranges.start(range),end-reserve);}
     video.playbackRate=end-video.currentTime>Math.max(0.9,steadyReserve+0.6)?1.05:1;
     if(video.paused)video.play().catch(()=>{});
     // Do not churn decoder eviction on each fragment. Keep a larger decoded history.
     if(video.currentTime-lastTrim>=5&&video.currentTime>35&&ranges.start(0)<video.currentTime-35){
      await update(()=>buffer.remove(0,video.currentTime-30));lastTrim=video.currentTime;
     }
    }
   }catch(e){if(!disposed){diag.restarts++;diag.error=e.message;report();onStatus('buffering');}}
   finally{
    clearInterval(watchdog);controller.abort();reader?.cancel().catch(()=>{});video.removeEventListener('waiting',waiting);video.removeEventListener('playing',playing);
    if(url){URL.revokeObjectURL(url);url=null;}
   }
   if(!disposed)await retryDelay();
  }
 }
 run();
 return {destroy(){disposed=true;attempt?.abort();wake?.();if(url){URL.revokeObjectURL(url);url=null;}}};
}
if(typeof module!=='undefined')module.exports={CopyPacketReader,copyVideoCodec,startCopyStream};
