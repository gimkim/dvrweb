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
function saveLiveFrame(video,wrap){
 if(video.readyState<2||!video.videoWidth||!video.videoHeight)throw Error('ยังไม่มีภาพให้บันทึก');
 const canvas=document.createElement('canvas');canvas.width=video.videoWidth;canvas.height=video.videoHeight;
 const context=canvas.getContext('2d');if(!context)throw Error('อุปกรณ์ไม่รองรับการบันทึกภาพ');
 context.drawImage(video,0,0,canvas.width,canvas.height);
 const link=document.createElement('a');link.href=canvas.toDataURL('image/jpeg',0.95);
 const camera=(video.id||'camera').replace(/^v-/,'').replace(/[^a-zA-Z0-9_-]/g,'_');
 link.download=`GimDVR-${camera}-${new Date().toISOString().replace(/[:.]/g,'-')}.jpg`;
 link.hidden=true;wrap.appendChild(link);try{link.click();}finally{link.remove();canvas.width=canvas.height=0;}
}
function bindLiveControls(wrap){
 const video=wrap.querySelector('video'),mute=wrap.querySelector('[data-live-mute]'),full=wrap.querySelector('[data-live-fullscreen]');let active=true;
 const snapshot=wrap.querySelector('[data-live-snapshot]');
 if(snapshot)snapshot.onclick=()=>{try{saveLiveFrame(video,wrap);snapshot.title='เซฟภาพปัจจุบัน';}catch(e){snapshot.title=e.message;if(typeof toast==='function')toast(e.message);}};
 video.controls=false;video.disablePictureInPicture=true;video.disableRemotePlayback=true;
 const soundState=()=>{if(!mute)return;const label=video.muted?'เปิดเสียง':'ปิดเสียง';mute.textContent=video.muted?'🔇':'🔊';mute.title=label;mute.setAttribute('aria-label',label);};
 const fullState=()=>{const selected=liveFullscreenWrap()===wrap,label=selected?'ออกจากเต็มจอ':'เต็มจอ';full.title=label;full.setAttribute('aria-label',label);if(document.fullscreenElement===wrap)window.onLiveFullscreenChanged?.(wrap);else if(!liveFullscreenWrap()&&wrap.contains(document.getElementById('controls')))window.onLiveFullscreenChanged?.(null);};
 const contextMenu=e=>e.preventDefault();
 if(mute)mute.onclick=()=>{video.muted=!video.muted;soundState();};
 full.disabled=!wrap.requestFullscreen&&!window.GimDvrAndroid;
 full.onclick=async()=>{try{
  if(liveFullscreenWrap()===wrap){window.exitLiveFullscreen();return;}
  if(window.GimDvrAndroid){window.exitLiveFullscreen();appFullscreenWrap=wrap;wrap.classList.add('app-fullscreen');window.GimDvrAndroid.setFullscreen(true);window.onLiveFullscreenChanged?.(wrap);document.dispatchEvent(new Event('gimdvrfullscreenchange'));}
  else{await wrap.requestFullscreen();if(document.fullscreenElement===wrap&&window.matchMedia?.('(pointer:coarse)').matches){try{await window.screen?.orientation?.lock?.('landscape');}catch{full.title='หมุนอุปกรณ์เป็นแนวนอน';}}}
 }catch{full.title='เบราว์เซอร์ไม่อนุญาตให้เปิดเต็มจอ';}};
 video.addEventListener('volumechange',soundState);video.addEventListener('contextmenu',contextMenu);
 document.addEventListener('fullscreenchange',fullState);document.addEventListener('gimdvrfullscreenchange',fullState);soundState();fullState();
 return {destroy(){active=false;if(snapshot)snapshot.onclick=null;if(liveFullscreenWrap()===wrap)window.exitLiveFullscreen();if(mute)mute.onclick=null;full.onclick=null;video.removeEventListener('volumechange',soundState);video.removeEventListener('contextmenu',contextMenu);document.removeEventListener('fullscreenchange',fullState);document.removeEventListener('gimdvrfullscreenchange',fullState);}};
}
