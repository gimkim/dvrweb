# Local synthetic gateway integration. No camera or production addresses.
import json,subprocess,time,urllib.request,urllib.error,shutil,sys
from pathlib import Path
root=Path(sys.argv[1] if len(sys.argv)>1 else 'artifacts/mediamtx').resolve()
config={'logLevel':'warn','api':True,'apiAddress':'127.0.0.1:19997','rtsp':False,'rtmp':False,'hls':False,'srt':False,'moq':False,'webrtc':True,'webrtcAddress':'127.0.0.1:18889','webrtcLocalUDPAddress':'127.0.0.1:18189','webrtcLocalTCPAddress':'127.0.0.1:18189','webrtcIPsFromInterfaces':False,'webrtcAdditionalHosts':['127.0.0.1'],'authInternalUsers':[{'user':'any','pass':'','ips':['127.0.0.1'],'permissions':[{'action':'read','path':''},{'action':'api','path':''}]}],'paths':{}}
(root/'smoke.json').write_text(json.dumps(config))
def req(url,method='GET',body=None,ctype='application/json'):
 r=urllib.request.Request(url,data=body,method=method,headers={'Content-Type':ctype});return urllib.request.urlopen(r,timeout=20)
log=open(root/'smoke.log','w');gateway=subprocess.Popen([str(root/'mediamtx.exe'),str(root/'smoke.json')],stdout=log,stderr=log,creationflags=subprocess.CREATE_NO_WINDOW);ff=None
try:
 for i in range(50):
  try:
   with req('http://127.0.0.1:19997/v3/config/global/get') as r: assert r.status==200
   break
  except Exception:time.sleep(.1)
 else:raise RuntimeError('Gateway startup failed')
 print('PASS pinned MediaMTX config starts with loopback-only signaling/control')
 with req('http://127.0.0.1:19997/v3/config/paths/add/synthetic','POST',json.dumps({'source':'udp+mpegts://127.0.0.1:34567','sourceOnDemand':True}).encode()) as r:assert r.status==200
 ff=subprocess.Popen([shutil.which('ffmpeg'),'-hide_banner','-loglevel','error','-re','-f','lavfi','-i','testsrc2=size=320x180:rate=15','-an','-c:v','libx264','-preset','ultrafast','-tune','zerolatency','-profile:v','baseline','-g','15','-f','mpegts','-muxdelay','0','udp://127.0.0.1:34567?pkt_size=1316'],stdout=log,stderr=log,creationflags=subprocess.CREATE_NO_WINDOW)
 sdp='\r\n'.join(['v=0','o=- 1 1 IN IP4 127.0.0.1','s=-','t=0 0','a=group:BUNDLE 0','m=video 9 UDP/TLS/RTP/SAVPF 96','c=IN IP4 0.0.0.0','a=mid:0','a=recvonly','a=rtcp-mux','a=ice-ufrag:test1234','a=ice-pwd:testPasswordForSynthetic123456','a=fingerprint:sha-256 '+':'.join(['AA']*32),'a=setup:actpass','a=rtpmap:96 H264/90000','a=fmtp:96 packetization-mode=1;profile-level-id=42e01f;level-asymmetry-allowed=1','a=rtcp-fb:96 nack','a=rtcp-fb:96 nack pli',''])
 locations=[]
 for i in range(2):
  with req('http://127.0.0.1:18889/synthetic/whep','POST',sdp.encode(),'application/sdp') as r:
   answer=r.read().decode();assert r.status==201 and 'H264/90000' in answer;locations.append('http://127.0.0.1:18889'+r.headers['Location'])
 print('PASS two WHEP offers negotiate H264 from one synthetic UDP input')
 for url in locations:
  with req(url,'DELETE') as r:assert r.status==200
 print('PASS WHEP DELETE closes each allocated session')
finally:
 if ff:ff.terminate();ff.wait(timeout=5)
 gateway.terminate();gateway.wait(timeout=5);log.close()
