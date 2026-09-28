"""Repair only empty AVC configuration in finalized app-owned MP4s; retain originals."""
import pathlib,struct,hashlib,json,subprocess,sys,os
U=lambda b: int.from_bytes(b,'big')
def boxes(b,start=0,end=None):
 end=len(b) if end is None else end
 while start<end:
  size=U(b[start:start+4]);typ=b[start+4:start+8];head=8
  if size==1:size=U(b[start+8:start+16]);head=16
  if size==0:size=end-start
  if size<head or start+size>end:raise ValueError('Invalid box bounds')
  yield start,size,typ,head
  start+=size

def repair(source,target):
 b=source.read_bytes();top=list(boxes(b));moov=next(x for x in top if x[2]==b'moov');mpos,msize,_,_=moov
 avcc=b.find(b'avcC',mpos,mpos+msize)
 if avcc<0 or U(b[avcc-4:avcc])!=8:return False
 # Strictly inspect video track and its first sample chunk, not arbitrary payload bytes.
 video=None
 for pos,size,typ,head in boxes(b,mpos+8,mpos+msize):
  if typ==b'trak' and pos<avcc<pos+size:video=b[pos:pos+size];break
 if video is None:raise ValueError('No video track')
 stco=video.find(b'stco');co64=video.find(b'co64');stsz=video.find(b'stsz')
 if stsz<0 or (stco<0 and co64<0):raise ValueError('No sample tables')
 off=U(video[stco+12:stco+16]) if stco>=0 else U(video[co64+12:co64+20])
 size=U(video[stsz+8:stsz+12]) or U(video[stsz+16:stsz+20]);sample=b[off:off+size]
 sps=pps=None;pos=0
 while pos+4<=len(sample):
  n=U(sample[pos:pos+4]);nal=sample[pos+4:pos+4+n]
  if n==0 or len(nal)!=n:raise ValueError('Not length-prefixed NAL')
  if nal[0]&31==7:sps=nal
  if nal[0]&31==8:pps=nal
  pos+=4+n
 if not sps or not pps:raise ValueError('No in-band SPS/PPS in first sample')
 config=bytes([1,sps[1],sps[2],sps[3],255,225])+len(sps).to_bytes(2,'big')+sps+bytes([1])+len(pps).to_bytes(2,'big')+pps
 def rebuild(start,end):
  out=bytearray()
  for pos,size,typ,head in boxes(b,start,end):
   payload=b[pos+head:pos+size]
   if typ==b'avcC':payload=config
   elif pos<avcc<pos+size:
    prefix=8 if typ==b'stsd' else 78 if typ in (b'avc1',b'avc3') else 0
    payload=payload[:prefix]+rebuild(pos+head+prefix,pos+size)
   out+=(len(payload)+8).to_bytes(4,'big')+typ+payload
  return out
 newmoov=rebuild(mpos,mpos+msize)
 # Keep every media byte and chunk offset unchanged by appending repaired metadata.
 data=bytearray(b);data[mpos+4:mpos+8]=b'free';data+=newmoov
 target.write_bytes(data)
 assert all(hashlib.sha256(b[p:p+n]).digest()==hashlib.sha256(data[p:p+n]).digest() for p,n,t,h in top if t==b'mdat')
 return True
if __name__=='__main__':
 repair(pathlib.Path(sys.argv[1]),pathlib.Path(sys.argv[2]))
