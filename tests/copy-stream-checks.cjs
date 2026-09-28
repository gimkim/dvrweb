const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const {CopyPacketReader,copyVideoCodec}=require('../src/GimDvr/wwwroot/copy-stream.js');
function frame(data){const h=Buffer.alloc(4);h.writeUInt32BE(data.length);return Buffer.concat([h,data]);}
function reader(bytes,step=1){let p=0;return{async read(){if(p>=bytes.length)return{done:true};const value=bytes.subarray(p,p+step);p+=value.length;return{done:false,value};}};}
(async()=>{
 const stream=new CopyPacketReader(reader(Buffer.concat([frame(Buffer.from('init')),frame(Buffer.from('chunk'))]),1));assert.equal(Buffer.from(await stream.next()).toString(),'init');assert.equal(Buffer.from(await stream.next()).toString(),'chunk');await assert.rejects(()=>stream.next(),/ended/);console.log('PASS fragmented network reads preserve init and media packet boundaries');
 for(const size of [0,16777217,0xffffffff]){const h=Buffer.alloc(4);h.writeUInt32BE(size);await assert.rejects(()=>new CopyPacketReader(reader(h,4)).next(),/Invalid/);}console.log('PASS empty and oversized frames rejected before allocation');
 await assert.rejects(()=>new CopyPacketReader(reader(Buffer.from([0,0,0,5,1,2]))).next(),/ended/);console.log('PASS truncated media frame cannot be appended');
 const fixture=fs.readdirSync('artifacts').filter(n=>n.startsWith('copy-checks-')).sort().at(-1);const init=fs.readFileSync(path.join('artifacts',fixture,'cache','init.mp4'));assert.match(copyVideoCodec(init),/^avc1\.[0-9a-f]{6}$/);assert.throws(()=>copyVideoCodec(new Uint8Array(20)),/H.264/);console.log('PASS real FFmpeg init supplies codec and invalid init rejected');
 const emptyAvcc=Buffer.from([0,0,0,8,97,118,99,67,0,0,0,16,115,116,116,115,0,0,0,0]);assert.throws(()=>copyVideoCodec(emptyAvcc),/SPS\/PPS/);console.log('PASS empty avcC followed by stts is rejected rather than read as codec avc1.000010');
 const damaged=Buffer.from(init),avcc=damaged.indexOf('avcC');damaged[avcc+9]=0xe0;assert.throws(()=>copyVideoCodec(damaged),/SPS\/PPS/);console.log('PASS codec initialization requires SPS entries');
 const truncated=Buffer.from(init);truncated.writeUInt32BE(truncated.length+100,avcc-4);assert.throws(()=>copyVideoCodec(truncated),/SPS\/PPS/);console.log('PASS codec initialization box cannot exceed available bytes');
})().catch(e=>{console.error(e);process.exitCode=1;});
