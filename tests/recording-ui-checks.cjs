// Code-only UI fixture: no browser, camera, or production requests.
const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const source=fs.readFileSync('src/GimDvr/wwwroot/app.js','utf8');
const nodes=new Map();
function $(id){if(!nodes.has(id))nodes.set(id,{innerHTML:'',textContent:'',checked:false,disabled:true,open:false,addEventListener(k,fn){this[k]=fn;},close(){this.open=false;this.closeEvent?.();},showModal(){this.open=true;}});return nodes.get(id);}
const video=$('#playVideo');Object.assign(video,{duration:60,currentTime:0,play:()=>Promise.resolve(),pause(){this.paused=true;},removeAttribute(){},load(){}});
const form=$('#recordingFilter');form.elements={camera:{value:'a'},from:{value:'2026-09-28T10:00:20'},to:{value:'2026-09-28T10:01:20'}};form.reportValidity=()=>true;
const rows=[{id:'one',cameraName:'A',start:new Date('2026-09-28T10:00:00').toISOString(),duration:60,bytes:1000},{id:'two',cameraName:'A',start:new Date('2026-09-28T10:01:00').toISOString(),duration:60,bytes:1000}];
let query;const buttons=[{dataset:{play:'0'}},{dataset:{play:'1'}}];
const ctx=vm.createContext({$,Date,URLSearchParams,FormData:class{get(k){return form.elements[k].value;}},clips:[],clipIndex:0,cameras:[{id:'a',name:'A'}],page:'recordings',esc:String,title:()=>'',toast(){},document:{querySelectorAll:()=>buttons},api:async q=>{query=q;return rows;}});
vm.runInContext(source.slice(source.indexOf('function detectionIcons('),source.indexOf('async function renderUsers')),ctx);
(async()=>{
 ctx.renderRecordings();const html=$('#main').innerHTML;
 assert.match(html,/id="recordingTimes" disabled/);
 const dates=[...html.matchAll(/type="datetime-local"[^>]*value="([^"]+)"/g)].map(m=>new Date(m[1]));assert.equal(dates[1]-dates[0],3600000);
 form.elements.camera.onchange();assert.equal($('#recordingTimes').disabled,false);
 console.log('PASS select camera first and default last hour');
 await ctx.loadRecordings();assert.match(query,/camera=a/);assert.match(query,/from=/);assert.match($('#recordingList').innerHTML,/class="recording-row"/);
 $('#playAll').onclick();assert.equal($('#autoNext').checked,true);assert.equal(video.src,'api/recordings/one/video');assert.equal($('#player').open,true);video.onloadedmetadata();assert.equal(video.currentTime,20);
 console.log('PASS selected camera range and first clip offset');
 video.onended();assert.equal(video.src,'api/recordings/two/video');video.currentTime=21;video.ontimeupdate();assert.equal(video.paused,true);
 buttons[0].onclick();assert.equal($('#autoNext').checked,false);video.onended();assert.equal(video.src,'api/recordings/one/video');
 console.log('PASS play-all advances and selected end stops; individual clip does not advance');
 const index=fs.readFileSync('src/GimDvr/wwwroot/index.html','utf8'),css=fs.readFileSync('src/GimDvr/wwwroot/app.css','utf8');
 assert.match(index,/<nav class="app-view-nav"[^>]*>.*data-page="recordings"/);assert.match(css,/\.android-app \.app-view-nav\{display:flex/);assert.match(index,/<video id="playVideo" controls playsinline/);
 console.log('PASS Android recordings navigation and inline playback contract');
 assert.match(ctx.detectionIcons({state:'complete',motion:true,human:false}),/Motion: พบ/);
 assert.match(ctx.detectionIcons({state:'complete',motion:true,human:false}),/Human: ไม่พบในภาพที่สุ่มตรวจ/);
 assert.equal((ctx.detectionIcons(null).match(/class="detect-badge unknown"/g)||[]).length,2);
 assert.match(ctx.detectionIcons({state:'partial',motion:false,human:null}),/Human: ข้อมูลไม่ครบ/);
 assert.doesNotMatch($('#recordingList').innerHTML,/<table/);
 console.log('PASS compact results preserve positive, negative and unknown accessible SVG indicators');
})().catch(e=>{console.error(e);process.exitCode=1;});
