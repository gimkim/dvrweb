// Pure rendering/binding checks, no browser or network.
const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const app=fs.readFileSync('src/GimDvr/wwwroot/app.js','utf8');
const main={innerHTML:''},controls={};let bound=0;
const ctx=vm.createContext({me:{role:'operator'},cameras:[{id:'a',name:'A',enabled:true},{id:'b',name:'B',enabled:true},{id:'off',name:'Off',enabled:false}],isViewing:()=>false,esc:String,title:()=>'',document:{body:{classList:{remove(){}}},querySelectorAll:()=>[]},$:()=>main,bindCameraButtons:()=>bound++,connectLive:()=>{throw Error('disabled view must not start');}});
vm.runInContext(app.slice(app.indexOf('function renderLive(){'),app.indexOf('function renderSingle(){')),ctx);
ctx.renderLive();assert.match(main.innerHTML,/data-control="a"/);assert.match(main.innerHTML,/data-control="b"/);assert.doesNotMatch(main.innerHTML,/data-control="off"/);assert.equal(bound,1);assert.match(fs.readFileSync('src/GimDvr/wwwroot/app.css','utf8'),/\.live-grid \.single-top,\.live-grid \.live-actions\{display:none\}/);console.log('PASS overview restores per-camera controls for enabled cameras without changing view preferences');
ctx.me.role='viewer';ctx.renderLive();assert.doesNotMatch(main.innerHTML,/data-control=/);console.log('PASS viewer has no control button');
const events=new Map(),video={muted:true,addEventListener(k,v){events.set(k,v);},removeEventListener(k){events.delete(k);}},full={setAttribute(){}};
const wrap={querySelector:s=>s==='video'?video:s==='[data-live-fullscreen]'?full:null,requestFullscreen(){},contains:()=>false};
const binding=vm.createContext({window:{},document:{fullscreenElement:null,getElementById:()=>controls,addEventListener(){},removeEventListener(){}},Event});
vm.runInContext(fs.readFileSync('src/GimDvr/wwwroot/live-controls.js','utf8'),binding);
const owner=binding.bindLiveControls(wrap);assert.equal(typeof full.onclick,'function');assert.equal(video.controls,false);owner.destroy();assert.equal(events.size,0);assert.equal(full.onclick,null);console.log('PASS silent single-camera stream retains fullscreen and cleans up without a mute button');
const backButtons=[{},{}];let backCalls=0;ctx.document.querySelectorAll=s=>s==='[data-back-overview]'?backButtons:[];ctx.showOverview=()=>backCalls++;ctx.renderLive();backButtons.forEach(b=>b.onclick());assert.equal(backCalls,2);assert.match(main.innerHTML,/<div class="video-wrap"><button[^>]*data-back-overview/);console.log('PASS header and fullscreen back actions use the same overview navigation');
