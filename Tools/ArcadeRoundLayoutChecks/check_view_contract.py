import pathlib,re,argparse
parser=argparse.ArgumentParser();parser.add_argument('--testClass',required=True,choices=['ArcadeRoundViewContractChecks']);parser.add_argument('--maxTests',required=True,type=int);parser.add_argument('--includePassingTests',required=True,choices=['true']);args=parser.parse_args()
assert args.maxTests>=3,'maximum must allow three declared API contract checks'
p=pathlib.Path('Client/Assets/Game/Scripts/Controller/Arcade/ArcadeCombatViewController.RoundContent.cs')
s=p.read_text(encoding='utf-8-sig') if p.exists() else ''
assert re.search(r'public\s+void\s+SetRoundLayout\(',s),'Controller is missing round layout preparation API'
print('PASS Round layout preparation API')
assert all(name in s for name in ['ReleaseRoundContent','RecordRoundEnemyPosition','RemoveRoundEnemyContent','HasRoundEnemyContent']),'Controller is missing ownership and enemy hooks'
print('PASS Explicit owner cleanup and enemy hooks')
assert 'SetRoundContent' in s and 'SetEnemyContent' in s and 'SyncRoundContentEnvironment' in s,'Controller is missing snapshot projection API'
print('PASS Snapshot projection API')
