import pathlib,sqlite3,importlib.util,subprocess,json,os,time,concurrent.futures
spec=importlib.util.spec_from_file_location('repair','dvrcam/deployment/repair-empty-avcc.py');mod=importlib.util.module_from_spec(spec);spec.loader.exec_module(mod)
c=sqlite3.connect('file:////gimkim-nas/C/Users/tatsa/web-data/GimDvr/dvr.db?mode=ro',uri=True);rows=list(c.execute('select id,path from recordings'));c.close()
ff=json.loads(pathlib.Path('dvrcam/src/GimDvr/appsettings.Development.json').read_text(encoding='utf-8'))['Dvr']['Ffmpeg']
def work(row):
 rid,p=row;q=pathlib.Path('//gimkim-nas/Music')/p[3:].replace('\\','/');tmp=q.with_suffix('.repairing.mp4');backup=q.with_suffix('.mp4.before-avcc-repair')
 result={'id':rid,'path':p}
 if not q.exists():return result|{'status':'missing'}
 try:
  if not mod.repair(q,tmp):return result|{'status':'not-empty-avcc'}
  r=subprocess.run([ff,'-v','error','-i',str(tmp),'-map','0:v:0','-map','0:a:0?','-f','null','-'],capture_output=True,timeout=90)
  if r.returncode or r.stderr:raise ValueError('Decode verification failed: '+r.stderr.decode(errors='replace')[:200])
  if backup.exists():raise ValueError('Backup already exists')
  stat=q.stat();os.rename(q,backup)
  try:os.replace(tmp,q);os.utime(q,ns=(stat.st_atime_ns,stat.st_mtime_ns))
  except:os.rename(backup,q);raise
  return result|{'status':'repaired','bytes':q.stat().st_size,'mediaPayloadUnchanged':True,'fullDecode':'passed'}
 except Exception as e:return result|{'status':'error','error':str(e)}
with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:results=list(pool.map(work,rows))
out=pathlib.Path('dvrcam/artifacts/recording-diagnosis/repair-report.json');out.write_text(json.dumps(results,indent=2),encoding='utf-8')
from collections import Counter
print(dict(Counter(r['status'] for r in results)))
for r in results:
 if r['status'] in ('error','missing'):print(r)
