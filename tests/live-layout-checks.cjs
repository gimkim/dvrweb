// Layout/session ownership fixture. No real DOM/browser/network.
const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const source=fs.readFileSync('src/GimDvr/wwwroot/app.js','utf8');
function classes(){const values=new Set();return{toggle(k,on){on?values.add(k):values.delete(k);},has:k=>values.has(k)};}
const cards=['a','b','off'].map(id=>({dataset:{id},classList:classes(),video:{id},inert:false}));
const sessions=new Map(),original=new Map(),requests=[],destroyed=[];let stops=0,controls=0;
for(const id of ['a','b']){const s={destroy(){destroyed.push(id);sessions.delete(id);}};sessions.set(id,s);original.set(id,s);}
const ctx=vm.createContext({page:'live',singleId:null,cameras:cards.map(c=>({id:c.dataset.id,enabled:true})),me:{role:'viewer'},liveSessions:sessions,window:{exitLiveFullscreen(){}},document:{body:{classList:classes(),appendChild(){}},querySelector:s=>s==='.live-grid'?{}:{appendChild(){}},querySelectorAll:s=>s==='[data-page]'?[]:cards},$:()=>({}),isViewing:id=>id!=='off',closeFloatingControls(){controls++;},showControls(){},showOverview(){throw Error('unexpected invalid selection');},liveMessage(){},connectLive(c,mode){requests.push([c.id,mode]);sessions.set(c.id,{destroy(){sessions.delete(c.id);destroyed.push(c.id);}});return Promise.resolve();},stopLive(){stops++;throw Error('navigation must preserve streams');}});
vm.runInContext(source.slice(source.indexOf('async function navigate('),source.indexOf('function openSingle('))+source.slice(source.indexOf('function applyLiveLayout('),source.indexOf('function liveMessage(')),ctx);
(async()=>{
 await ctx.navigate('single','a');assert.ok(cards[0].classList.has('single-camera'));assert.ok(cards[1].classList.has('background-camera'));assert.equal(cards[1].inert,true);
 await ctx.navigate('single','b');assert.ok(cards[1].classList.has('single-camera'));await ctx.navigate('live',null);
 assert.equal(stops,0);assert.equal(requests.length,0);assert.equal(destroyed.length,0);for(const [id,s] of original)assert.equal(sessions.get(id),s);assert.ok(cards.every(c=>!c.classList.has('background-camera')&&!c.inert));console.log('PASS overview/single/camera-switch/back preserve every active session and video node without new requests');
 await ctx.navigate('single','off');assert.deepEqual(requests,[['off','focus']]);assert.equal(sessions.get('a'),original.get('a'));assert.equal(sessions.get('b'),original.get('b'));await ctx.navigate('live',null);assert.deepEqual(destroyed,['off']);assert.equal(ctx.isViewing('off'),false);console.log('PASS previously disabled camera gets temporary single-view session without changing preferences or other sessions');
 ctx.me.role='operator';await ctx.navigate('single','a');assert.ok(controls>0);assert.equal(sessions.get('a'),original.get('a'));console.log('PASS control-panel transition preserves streaming session');
})().catch(e=>{console.error(e);process.exitCode=1;});
