const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const source=fs.readFileSync('src/GimDvr/wwwroot/app.js','utf8'),line=source.split('\n').find(x=>x.startsWith('async function renderSystem'));
const nodes=new Map(),button={};const $=s=>{if(!nodes.has(s))nodes.set(s,{querySelector:()=>button});return nodes.get(s);};let saved;
const config={segmentMs:150,startupMs:300,rebufferMs:400,liveTargetMs:500};
const ctx=vm.createContext({$,title:()=>'',esc:String,Date,Promise,FormData:class{*[Symbol.iterator](){yield* Object.entries(config);}},api:async(path,method,data)=>{if(method){saved=data;return data;}return path==='detection-settings'?{workers:2,readRate:0}:path==='system'?{machine:'fixture',ffmpeg:true}:path==='audit'?[]:config;}});
vm.runInContext(line,ctx);
(async()=>{await ctx.renderSystem();assert.doesNotMatch($('#main').innerHTML,/streamSettings|rebufferMs|segmentMs/);console.log('PASS retired fallback buffer settings are absent');
assert.match($('#main').innerHTML,/name="workers"/);assert.match($('#main').innerHTML,/name="readRate"[^>]*value="0"/);const detectionRoutes=fs.readFileSync('src/GimDvr/Program.cs','utf8').split('\n').filter(x=>x.includes('"/api/detection-settings"'));assert.equal(detectionRoutes.length,2);for(const r of detectionRoutes)assert.match(r,/RequireAuthorization\("admin"\)/);console.log('PASS detection controls remain available and require admin');
const routes=fs.readFileSync('src/GimDvr/Program.cs','utf8');assert.match(routes,/copy-stream.*StatusCode\(410\).*RequireAuthorization/);assert.doesNotMatch(fs.readFileSync('src/GimDvr/wwwroot/index.html','utf8'),/src="copy-stream.js"/);console.log('PASS retired transport is not loaded and endpoint returns410');})();
