"""Run against a LOCAL DEVELOPMENT instance, never production. Uses its bootstrap.txt."""
import json, pathlib, re, secrets, sys, requests
base='http://127.0.0.1:5187/gimdvr/api/'
bootstrap=pathlib.Path(sys.argv[1]).read_text()
password=re.search(r'^Password: (.+)$',bootstrap,re.M)[1].strip()
checks=[]
def check(ok,name):
    assert ok,name
    checks.append(name)
    print('PASS',name)
for endpoint in ['cameras','cameras/garage/snapshot','live/garage/index.m3u8','recordings','recordings/missing/video','users']:
    check(requests.get(base+endpoint).status_code==401,'anonymous blocked: '+endpoint)
s=requests.Session();s.headers['X-DVR-Request']='1'
check(s.post(base+'login',json={'username':'admin','password':password}).status_code==200,'admin login')
cams=s.get(base+'cameras').json()
check(len(cams)==3 and all(not c['recordingEnabled'] and c['recordingRoot']=='' for c in cams),'three cameras recording disabled without paths')
check(all('secret' not in c and 'password' not in c for c in cams),'no camera credentials in API')
check(s.post(base+'cameras',json={},headers={'Origin':'https://untrusted.invalid'}).status_code==403,'cross-origin mutation rejected')
check(requests.post(base+'logout',cookies=s.cookies).status_code==400,'missing CSRF header rejected')
c=next(c for c in cams if c['id']=='garage').copy();c['id']='../escape'
check(s.post(base+'cameras',json={'camera':c,'password':'fixture'}).status_code==400,'path traversal camera id rejected')
check(s.get(base+'cameras/garage/talk-capability').json()['supported'] is False,'unsupported talk explicitly reported')
name='test_'+secrets.token_hex(4);pw=secrets.token_hex(16)
uid=s.post(base+'users',json={'username':name,'role':'viewer','enabled':True,'password':pw}).json()['id']
v=requests.Session();v.headers['X-DVR-Request']='1'
try:
    check(v.post(base+'login',json={'username':name,'password':pw}).status_code==200,'viewer login')
    check(v.get(base+'cameras').status_code==200,'viewer can view camera list')
    check(v.get(base+'users').status_code==403,'viewer cannot manage users')
    check(v.post(base+'cameras/garage/control',json={'action':'stop','value':0}).status_code==403,'viewer cannot control camera')
    check(v.post(base+'cameras',json={'camera':cams[0]}).status_code==403,'viewer cannot modify recording')
    check(v.post(base+'cameras/garage/ptz',json={'direction':'left','token':'00000000-0000-0000-0000-000000000001'}).status_code==403,'viewer cannot send hold-to-pan commands')
finally:
    s.put(base+'users/'+uid,json={'username':name,'role':'viewer','enabled':False,'password':None}).raise_for_status()
check(v.get(base+'me').status_code==401,'disabled account session revoked')
check(s.post(base+'logout').status_code==200 and s.get(base+'me').status_code==401,'logout invalidates cookie')
print(json.dumps({'passed':len(checks),'checks':checks},ensure_ascii=False))
