const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
let draw,clicked=0,removed=0,appended=0,canvas;
const context=vm.createContext({window:{},Date,document:{addEventListener(){},createElement(kind){if(kind==='canvas')return canvas={getContext:()=>({drawImage(...args){draw=args;}}),toDataURL:(type,q)=>{assert.equal(type,'image/jpeg');assert.equal(q,0.95);return 'data:image/jpeg;base64,fixture';}};return{click(){clicked++;assert.match(this.download,/^GimDVR-garage-.*\.jpg$/);},remove(){removed++;}};}}});
vm.runInContext(fs.readFileSync('src/GimDvr/wwwroot/live-controls.js','utf8'),context);
const video={id:'v-garage',readyState:4,videoWidth:1920,videoHeight:1080};const wrap={appendChild(){appended++;}};
for(const transport of ['webrtc','fmp4']){video.dataset={transport};context.saveLiveFrame(video,wrap);assert.equal(draw[0],video);assert.deepEqual(draw.slice(1),[0,0,1920,1080]);assert.equal(canvas.width,0);}
assert.equal(clicked,2);assert.equal(removed,2);assert.equal(appended,2);console.log('PASS both live transports capture displayed video at native dimensions and clean download elements');
assert.throws(()=>context.saveLiveFrame({...video,readyState:1},wrap));assert.equal(clicked,2);console.log('PASS no frame does not download a blank image');
const app=fs.readFileSync('src/GimDvr/wwwroot/app.js','utf8');assert.match(app,/class="live-snapshot" data-live-snapshot/);console.log('PASS snapshot button is in shared overview/single video wrapper');
// Native bridge must receive the JPEG rather than clicking a data URL in WebView.
let nativeImage;
context.window.GimDvrAndroid={saveImage(data,name){nativeImage={data,name};}};
context.saveLiveFrame(video,wrap);
assert.equal(nativeImage.data,'data:image/jpeg;base64,fixture');assert.match(nativeImage.name,/\.jpg$/);assert.equal(clicked,2);assert.equal(canvas.width,0);
console.log('PASS Android snapshot uses native save bridge without anchor navigation');
context.window.GimDvrAndroid={};assert.throws(()=>context.saveLiveFrame(video,wrap),/1\.0\.2/);assert.equal(canvas.width,0);
console.log('PASS old APK reports required update instead of silently doing nothing');
