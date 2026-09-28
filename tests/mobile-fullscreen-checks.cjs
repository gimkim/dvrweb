const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const app=fs.readFileSync('src/GimDvr/wwwroot/app.js','utf8'),controls=fs.readFileSync('src/GimDvr/wwwroot/live-controls.js','utf8');
(async()=>{
 const order=[];const entry=vm.createContext({androidApp:false,window:{matchMedia:()=>({matches:true})},history:{pushState(){}},encodeURIComponent,navigate(){order.push('layout');return Promise.resolve();},document:{querySelector:()=>({click(){order.push('fullscreen');}})},toast(){}});
 vm.runInContext(app.slice(app.indexOf('function openSingle('),app.indexOf('function showOverview(')),entry);entry.openSingle('a');assert.deepEqual(order,['layout','fullscreen']);console.log('PASS mobile tap requests fullscreen synchronously after in-place layout change');
 function fixture(native=false,denyLock=false){
  const calls=[],listeners={},full={setAttribute(){}},video={addEventListener(){},removeEventListener(){}};
  const wrap={classList:{add(){},remove(){}},querySelector:s=>s==='video'?video:s==='[data-live-fullscreen]'?full:null,contains:()=>false,async requestFullscreen(){calls.push('fullscreen');doc.fullscreenElement=wrap;}};
  const doc={fullscreenElement:null,addEventListener(k,v){(listeners[k]??=[]).push(v);},removeEventListener(){},getElementById(){},dispatchEvent(){},async exitFullscreen(){calls.push('exit');doc.fullscreenElement=null;}};
  const win={matchMedia:()=>({matches:true}),screen:{orientation:{async lock(value){calls.push(value);if(denyLock)throw Error('unsupported');},unlock(){calls.push('unlock');}}}};
  if(native)win.GimDvrAndroid={setFullscreen:value=>calls.push(value?'native-landscape':'native-portrait')};
  const ctx=vm.createContext({window:win,document:doc,Event});vm.runInContext(controls,ctx);ctx.bindLiveControls(wrap);return{calls,full,win,doc,wrap};
 }
 const web=fixture();await web.full.onclick();assert.deepEqual(web.calls,['fullscreen','landscape']);web.win.exitLiveFullscreen();assert.ok(web.calls.includes('unlock'));assert.ok(web.calls.includes('exit'));console.log('PASS browser requests landscape after fullscreen and unlocks on exit');
 const denied=fixture(false,true);await denied.full.onclick();assert.equal(denied.doc.fullscreenElement,denied.wrap);assert.equal(denied.full.title,'หมุนอุปกรณ์เป็นแนวนอน');console.log('PASS unsupported orientation lock preserves fullscreen with manual-rotation hint');
 const native=fixture(true);await native.full.onclick();assert.ok(native.calls.includes('native-landscape'));assert.ok(!native.calls.includes('fullscreen'));native.win.exitLiveFullscreen();assert.ok(native.calls.includes('native-portrait'));console.log('PASS Android uses existing native landscape/fullscreen bridge and restores portrait');
})().catch(e=>{console.error(e);process.exitCode=1;});
