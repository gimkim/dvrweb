'use strict';
let appFullscreenWrap=null;
function liveFullscreenWrap(){return appFullscreenWrap||document.fullscreenElement;}
function unlockLiveOrientation(){try{window.screen?.orientation?.unlock?.();}catch{}}
document.addEventListener('fullscreenchange',()=>{if(!document.fullscreenElement)unlockLiveOrientation();});
window.exitLiveFullscreen=()=>{
 unlockLiveOrientation();
 if(appFullscreenWrap){appFullscreenWrap.classList.remove('app-fullscreen');appFullscreenWrap=null;window.GimDvrAndroid?.setFullscreen(false);window.onLiveFullscreenChanged?.(null);document.dispatchEvent(new Event('gimdvrfullscreenchange'));}
 else if(document.fullscreenElement)document.exitFullscreen().catch(()=>{});
};
function bindLiveControls(wrap){
 const video=wrap.querySelector('video'),mute=wrap.querySelector('[data-live-mute]'),full=wrap.querySelector('[data-live-fullscreen]');let active=true;
 video.controls=false;video.disablePictureInPicture=true;video.disableRemotePlayback=true;
 const soundState=()=>{if(!mute)return;const label=video.muted?'เปิดเสียง':'ปิดเสียง';mute.textContent=video.muted?'🔇':'🔊';mute.title=label;mute.setAttribute('aria-label',label);};
 const fullState=()=>{const selected=liveFullscreenWrap()===wrap,label=selected?'ออกจากเต็มจอ':'เต็มจอ';full.title=label;full.setAttribute('aria-label',label);if(document.fullscreenElement===wrap)window.onLiveFullscreenChanged?.(wrap);else if(!liveFullscreenWrap()&&wrap.contains(document.getElementById('controls')))window.onLiveFullscreenChanged?.(null);};
 const resume=()=>{if(active&&video.isConnected&&wrap.closest('.camera-card')?.dataset.viewing!=='false'&&video.readyState>=2)video.play().catch(()=>{});};
 const contextMenu=e=>e.preventDefault();
 if(mute)mute.onclick=()=>{video.muted=!video.muted;soundState();resume();};
 full.disabled=!wrap.requestFullscreen&&!window.GimDvrAndroid;
 full.onclick=async()=>{try{
  if(liveFullscreenWrap()===wrap){window.exitLiveFullscreen();return;}
  if(window.GimDvrAndroid){window.exitLiveFullscreen();appFullscreenWrap=wrap;wrap.classList.add('app-fullscreen');window.GimDvrAndroid.setFullscreen(true);window.onLiveFullscreenChanged?.(wrap);document.dispatchEvent(new Event('gimdvrfullscreenchange'));}
  else{await wrap.requestFullscreen();if(document.fullscreenElement===wrap&&window.matchMedia?.('(pointer:coarse)').matches){try{await window.screen?.orientation?.lock?.('landscape');}catch{full.title='หมุนอุปกรณ์เป็นแนวนอน';}}}
 }catch{full.title='เบราว์เซอร์ไม่อนุญาตให้เปิดเต็มจอ';}};
 video.addEventListener('volumechange',soundState);video.addEventListener('pause',resume);video.addEventListener('canplay',resume);video.addEventListener('contextmenu',contextMenu);
 document.addEventListener('fullscreenchange',fullState);document.addEventListener('gimdvrfullscreenchange',fullState);soundState();fullState();
 return {destroy(){active=false;if(liveFullscreenWrap()===wrap)window.exitLiveFullscreen();if(mute)mute.onclick=null;full.onclick=null;video.removeEventListener('volumechange',soundState);video.removeEventListener('pause',resume);video.removeEventListener('canplay',resume);video.removeEventListener('contextmenu',contextMenu);document.removeEventListener('fullscreenchange',fullState);document.removeEventListener('gimdvrfullscreenchange',fullState);}};
}
