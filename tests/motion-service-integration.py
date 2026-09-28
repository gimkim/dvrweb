"""Local loopback service fixtures only; no production endpoint or private footage."""
import pathlib,json,secrets,subprocess,urllib.request,urllib.error,time,socket,sys,os
base=pathlib.Path(__file__).resolve().parents[1]
runtime=pathlib.Path(sys.argv[1]).resolve();root=base/'artifacts'/('motion-integration-'+time.strftime('%Y%m%d-%H%M%S'));root.mkdir()
ffmpeg=runtime/'tools/ffmpeg.exe';clip=root/'synthetic.mp4'
subprocess.run([str(ffmpeg),'-hide_banner','-loglevel','error','-f','lavfi','-i','color=c=black:s=640x360:r=10','-t','60','-c:v','libx264','-pix_fmt','yuv420p',str(clip)],check=True)
for device in ['CUDA','CPU']:
 data=root/device;data.mkdir();key=secrets.token_hex(32)
 (data/'service.json').write_text(json.dumps({'Motion':{'ApiKey':key,'DataRoot':str(data),'RuntimeRoot':str(runtime),'Device':device,'Decoder':'cpu' if device=='CPU' else 'cuda','CudaDllDirectory':str(runtime/'cuda')}}))
 with socket.socket() as sock:sock.bind(('127.0.0.1',0));port=sock.getsockname()[1]
 url=f'http://127.0.0.1:{port}'
 log=(data/'host.log').open('w')
 proc=subprocess.Popen(['dotnet',str(base/'artifacts/motion-release/MotionService.dll'),'--Motion:DataRoot='+str(data),'--urls='+url],stdout=log,stderr=log,creationflags=subprocess.CREATE_NO_WINDOW)
 def request(route,body=None,auth=True,extra=None):
  headers={'Authorization':'Bearer '+key} if auth else {}
  if body is not None:headers['Content-Type']='application/octet-stream'
  headers.update(extra or {})
  try:
   with urllib.request.urlopen(urllib.request.Request(url+route,data=body,headers=headers),timeout=100) as r:return r.status,r.read()
  except urllib.error.HTTPError as e:return e.code,e.read()
 try:
  deadline=time.monotonic()+90
  while True:
   if proc.poll() is not None:raise RuntimeError('Service exited; inspect private fixture host.log')
   try:
    status,body=request('/health')
    if status==200:break
   except (OSError,urllib.error.URLError):pass
   if time.monotonic()>deadline:raise RuntimeError('Engine readiness timeout')
   time.sleep(.25)
  assert json.loads(body)['device']==device,body
  assert request('/health',auth=False)[0]==401
  assert request('/analyze?duration=NaN',b'x')[0]==400
  assert request('/analyze?duration=60',b'x',extra={'Content-Type':'text/plain'})[0]==415
  assert request('/analyze?duration=60',b'x',extra={'Content-Length':str(256*1024*1024+1)})[0]==413
  start=time.monotonic();status,body=request('/analyze?duration=60',clip.read_bytes());elapsed=time.monotonic()-start
  result=json.loads(body)
  assert status==200 and result['state']=='complete' and result['device']==device and result['frames']==120 and result['human'] is False,(status,result)
  assert result['decoder']==('cpu-1thread' if device=='CPU' else 'cuda')
  assert not list((data/'incoming').glob('*.mp4'))
  print('PASS',device,'authenticated HTTP upload, actual inference, coverage, cleanup; 60s video in',round(elapsed,3),'s',flush=True)
  (data/'result.json').write_text(json.dumps({'result':result,'elapsedSeconds':elapsed},indent=2))
  status,body=request('/analyze?duration=60',b'not a video');bad=json.loads(body)
  assert status==200 and bad['state']=='error' and bad['human'] is None and bad['motion'] is None
  assert not list((data/'incoming').glob('*.mp4'))
  print('PASS invalid clip remains unknown, temporary upload removed; auth/size/type/duration boundaries',flush=True)
 finally:
  proc.terminate();proc.wait(timeout=15);log.close()
print('Evidence:',root)
