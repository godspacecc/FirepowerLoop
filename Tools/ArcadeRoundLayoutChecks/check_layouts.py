import argparse,csv,collections,pathlib,sys
p=argparse.ArgumentParser();p.add_argument('--testClass',required=True,choices=['ArcadeRoundLayoutChecks']);p.add_argument('--maxTests',required=True,type=int);p.add_argument('--includePassingTests',required=True,choices=['true']);a=p.parse_args()
assert a.maxTests>=3,'maximum must allow three declared checks'
root=pathlib.Path(__file__).resolve().parents[2]; path=root/'Config/DataCsv/arcade_round_layout.csv'
rows=[]
if path.exists():
 with path.open(encoding='utf-8-sig',newline='') as f:
  source=list(csv.reader(f));rows=[dict(zip(source[0][1:],r[1:])) for r in source[2:] if r and not r[0].startswith('#')]
def require(ok,msg):
 if not ok: raise AssertionError(msg)
require(len(rows)==80,f'expected 80 playable layouts, got {len(rows)}')
require({(int(r['ArenaConfigNumber']),int(r['RoundNumber'])) for r in rows}=={(n,r) for n in range(1,21) for r in range(1,5)},'missing or duplicate theme rounds')
print('PASS Complete eighty theme rounds')
for r in rows:
 n=int(r['ArenaConfigNumber']);rn=int(r['RoundNumber']);require(r['Id']==f'arena.{n}.round.{rn}' and r['Version']=='1','unstable identity')
 obs=[tuple(map(float,o.split('|'))) for o in r['Obstacles'].split(';') if o]
 require(all(abs(x)+w<19.5 and abs(y)+h<19.5 and min(w,h)>0 for x,y,w,h in obs),'invalid obstacle bounds')
 def walk(x,y):return abs(x)<=18 and abs(y)<=18 and not any(abs(x-ox)<w+1.1 and abs(y-oy)<h+1.1 for ox,oy,w,h in obs)
 targets=[(float(r['SpawnX']),float(r['SpawnY'])),(float(r['ExitX']),float(r['ExitY']))]+[tuple(map(float,e.split('|'))) for e in r['Entries'].split(';')]
 require(all(walk(x,y) for x,y in targets),f"blocked multiplayer landmark {r['Id']}")
 start=tuple(round(v) for v in targets[0]);seen={start};q=collections.deque([start])
 while q:
  x,y=q.popleft()
  for pos in [(x+1,y),(x-1,y),(x,y+1),(x,y-1)]:
   if pos not in seen and walk(*pos):seen.add(pos);q.append(pos)
 require(all(tuple(round(v) for v in point) in seen for point in targets),f"disconnected landmarks {r['Id']}")
print('PASS Multiplayer clearance and connected entrances exits')
for n in range(1,21):
 group=[r for r in rows if int(r['ArenaConfigNumber'])==n]
 require(len({r['Obstacles'] for r in group})==4,f'four distinct obstacle arrangements required: {n}')
 for r in group:
  require(0<=int(r['SpecialKind'])<=17 and 0<=int(r['EventKind'])<=10 and 0<=int(r['BossKind'])<=4,'invalid content enum')
  require(0<=float(r['SpecialRatio'])<=1 and float(r['EnemyHealthScale'])>0 and float(r['EnemySpeedScale'])>0,'invalid content tuning')
print('PASS Distinct four round designs and bounded content tuning')
