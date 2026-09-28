'use strict';
function bindLiveControls(wrap){
 const video=wrap.querySelector('video'),mute=wrap.querySelector('[data-live-mute]'),full=wrap.querySelector('[data-live-fullscreen]');
 let active=true;
 video.controls=false;video.disablePictureInPicture=true;video.disableRemotePlayback=true;
 const soundState=()=>{const label=video.muted?'เปิดเสียง':'ปิดเสียง';mute.textContent=video.muted?'🔇':'🔊';mute.title=label;mute.setAttribute('aria-label',label);};
 const fullState=()=>{const label=document.fullscreenElement===wrap?'ออกจากเต็มจอ':'เต็มจอ';full.title=label;full.setAttribute('aria-label',label);};
 const resume=()=>{if(active&&video.isConnected&&video.readyState>=2)video.play().catch(()=>{});};
 const contextMenu=e=>e.preventDefault();
 mute.onclick=()=>{video.muted=!video.muted;soundState();resume();};
 full.disabled=!wrap.requestFullscreen;
 full.onclick=async()=>{try{if(document.fullscreenElement===wrap)await document.exitFullscreen();else await wrap.requestFullscreen();}catch{full.title='เบราว์เซอร์ไม่อนุญาตให้เปิดเต็มจอ';}};
 video.addEventListener('volumechange',soundState);
 video.addEventListener('pause',resume);
 video.addEventListener('canplay',resume);
 video.addEventListener('contextmenu',contextMenu);
 document.addEventListener('fullscreenchange',fullState);
 soundState();fullState();
 return {destroy(){active=false;mute.onclick=null;full.onclick=null;video.removeEventListener('volumechange',soundState);video.removeEventListener('pause',resume);video.removeEventListener('canplay',resume);video.removeEventListener('contextmenu',contextMenu);document.removeEventListener('fullscreenchange',fullState);}};
}
