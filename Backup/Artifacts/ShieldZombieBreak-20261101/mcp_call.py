import urllib.request,json,sys
from pathlib import Path
headers={'Accept':'application/json, text/event-stream','Content-Type':'application/json'}
def rpc(obj):
 r=urllib.request.urlopen(urllib.request.Request('http://localhost:21032/mcp',data=json.dumps(obj).encode(),headers=headers),timeout=180)
 if r.headers.get('Mcp-Session-Id'):headers['Mcp-Session-Id']=r.headers['Mcp-Session-Id']
 t=r.read().decode()
 if not t:return {}
 if t.startswith('event:') or t.startswith('data:'):t=next(x[6:] for x in t.splitlines() if x.startswith('data: '))
 return json.loads(t)
rpc({'jsonrpc':'2.0','id':1,'method':'initialize','params':{'protocolVersion':'2024-11-05','capabilities':{},'clientInfo':{'name':'codex-local-verification','version':'1'}}})
rpc({'jsonrpc':'2.0','method':'notifications/initialized'})
args=json.loads(Path(sys.argv[2]).read_text(encoding='utf-8-sig'))
r=rpc({'jsonrpc':'2.0','id':2,'method':'tools/call','params':{'name':sys.argv[1],'arguments':args}})
Path(sys.argv[3]).write_text(json.dumps(r,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(r,ensure_ascii=True)[:3500])
