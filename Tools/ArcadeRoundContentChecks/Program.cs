using FirepowerLoop.Arcade;
using R = FirepowerLoop.Arcade.ArcadeRoundContentRules;

// 精确类过滤和数量上限为必填，不允许全套回退。
var options = args.Select(a => a.Split('=', 2)).ToDictionary(a => a[0], a => a.Length > 1 ? a[1] : "");
if (!options.TryGetValue("testClass", out var cls) || cls != "RoundContent" ||
    !options.TryGetValue("maxTests", out var maxText) || !int.TryParse(maxText, out var max) || max < 1 ||
    !options.TryGetValue("includePassingTests", out var passing) || passing != "true")
    throw new ArgumentException("Require testClass=RoundContent maxTests=N includePassingTests=true");
var p = new ArcadeContentTuning(3,5,1,.3f,2,1.2f,6,2,2,8,3,4,.2f,2);
var tests = new (string, Action)[] {
 ("Warning protects before active edge", () => { Require(R.Phase(0,8,3)==0 && R.Phase(2.99,8,3)==0,"unsafe warning"); Require(R.Phase(3,8,3)==1 && R.Phase(8,8,3)==0,"cycle boundary"); }),
 ("Pause freezes clock and forbids catchup damage", () => { var c=new ArcadeContentClock(); c.Advance(1,false); Require(c.TryHit(1,2),"first contact"); c.Advance(90,true); Require(c.Elapsed==1&&!c.TryHit(1,2),"pause consumed clock"); c.Advance(2,false); Require(c.TryHit(1,2)&&!c.TryHit(1,2),"repeat contact"); }),
 ("Target cooldowns remain independent and releasable", () => { var c=new ArcadeContentClock(); Require(c.TryHit(11,1)&&c.TryHit(22,1)&&!c.TryHit(11,1),"target identities shared"); c.ForgetTarget(11); Require(c.TryHit(11,1),"reused identity inherited cooldown"); }),
 ("Invalid clock steps never poison future hits", () => { var c=new ArcadeContentClock(); c.Advance(float.NaN,false); c.Advance(float.PositiveInfinity,false); c.Advance(-1,false); Require(c.Elapsed==0,"invalid time advanced"); Require(!c.TryHit(1,0)&&!c.TryHit(1,float.NaN),"invalid interval hit"); }),
 ("Mannequin freezes on hit then resumes configured chase", () => { Require(R.SpecialMotion(3,4,3,p)==0,"hit did not freeze"); Require(R.SpecialMotion(3,5.1,3,p)==2,"did not resume chase"); Require(R.SpecialMotion(2,5.1,3,p)==1,"ordinary crawler multiplied twice"); }),
 ("Burrow charge exposes damage window and armor cycles", () => { Require(R.IsUnderground(4,1,p)&&!R.IsUnderground(4,2,p),"burrow phase"); Require(R.IncomingDamageScale(4,1,p)==0,"underground took damage"); Require(R.SpecialMotion(4,2,-1,p)==2,"charge absent"); Require(R.IsArmored(13,1,p)&&!R.IsArmored(13,2,p),"armor phase"); Require(R.IncomingDamageScale(13,1,p)==.5f,"armor ineffective"); }),
 ("Aura death zones and spores use configured reach", () => { Require(R.AllyBuff(7,3,p)==2&&R.AllyBuff(7,5,p)==1&&R.AllyBuff(2,1,p)==1,"aura leak"); Require(R.DeathAreaRadius(1,p)==4&&R.DeathAreaRadius(15,p)==4&&R.DeathAreaRadius(2,p)==0,"death zone type"); Require(R.SporeSpeedScale(true,p)==.5f&&R.SporeSpeedScale(false,p)==1,"spore slow"); }),
 ("Moving hazards travel and circle deterministically", () => { R.EventCenter(2,1,0,0,4,2,out var x,out var y); Require(Near(x,-2)&&Near(y,0),"ghost travel"); R.EventCenter(6,Math.PI,0,0,4,2,out x,out y); Require(Near(x,0)&&Near(y,4),"vortex orbit"); }),
 ("Circle and laser contact include thick endpoints", () => { Require(R.TouchesCircle(5,0,1,0,0,4)&&!R.TouchesCircle(5.1f,0,1,0,0,4),"circle edge"); Require(R.TouchesSegment(2.5f,0,.5f,-2,0,2,0,0)&&!R.TouchesSegment(0,2,.5f,-2,0,2,0,.5f),"laser capsule"); Require(R.TouchesSegment(0,1,1,0,0,0,0,0),"degenerate line"); }),
 ("Damage never heals overflows or exceeds maximum", () => { Require(R.Damage(100,.2f)==20&&R.Damage(100,10)==100,"damage cap"); Require(R.Damage(100,float.NaN)==0&&R.Damage(100,-1)==0&&R.Damage(0,1)==0,"invalid damage"); }),
 ("Ice preserves paused velocity and decays without input", () => { R.UpdateIceVelocity(8,0,0,1,10,.5f,1,true,out var x,out var y); Require(x==8&&y==0,"pause moved ice"); R.UpdateIceVelocity(8,0,0,0,10,.5f,1,false,out x,out y); Require(Near(x,4)&&Near(y,0),"inertia decay"); R.UpdateIceVelocity(0,0,10,0,10,.5f,1,false,out x,out y); Require(Near(x,5),"diagonal/input speed unbounded"); }),
 ("Tracking turns gradually and retains projectile speed", () => { R.SteerTowards(4,0,0,10,(float)Math.PI/2, .5f,out var x,out var y); Require(Near(x,2.828427f)&&Near(y,2.828427f),"snapped tracking"); R.SteerTowards(4,0,0,10,10,0,out x,out y); Require(x==4&&y==0,"zero delta turn"); }),
 ("Bosses cycle their actual distinct attack patterns", () => { Require(R.BossAttack(1,0,6)==0&&R.BossAttack(1,6,6)==1&&R.BossAttack(1,12,6)==0,"guardian attacks"); Require(R.BossAttack(4,18,6)==3&&R.BossAttack(0,5,6)==-1,"final boss attacks"); }),
 ("Every special and boss has a dedicated silhouette", () => { var names=new HashSet<string>(); var shapes=new HashSet<string>(); for(int i=1;i<=17;i++){var v=R.GetVisualProfile(i,0);Require(names.Add(v.Name)&&v.Parts.Length>=2,"special silhouette absent"); Require(shapes.Add(string.Join(";",v.Parts.Select(q=>$"{q.Primitive},{q.X},{q.Y},{q.Z},{q.ScaleX},{q.ScaleY},{q.ScaleZ}"))),"special only recolored");} for(int i=1;i<=4;i++){var v=R.GetVisualProfile(0,i);Require(names.Add(v.Name)&&v.Parts.Length>=3,"boss silhouette absent");} }),
 ("Invalid tuning is rejected at shared boundary", () => { R.Validate(p); bool rejected=false; try { R.Validate(new ArcadeContentTuning(3,5,1,.3f,2,1,1,2,2,8,3,4,.2f,2)); } catch(ArgumentException){rejected=true;} Require(rejected,"warning longer than skill period accepted"); rejected=false;try{R.Validate(new ArcadeContentTuning(3,5,1,float.NaN,2,1,6,2,2,8,3,4,.2f,2));}catch(ArgumentException){rejected=true;}Require(rejected,"NaN accepted"); })
};
if (tests.Length > max) throw new InvalidOperationException("Test limit exceeded");
int failed=0;
foreach (var (name, test) in tests) { try { test(); Console.WriteLine("PASS RoundContent."+name); } catch(Exception ex) { failed++; Console.WriteLine("FAIL RoundContent."+name+": "+ex.Message); } }
Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed");
Environment.ExitCode=failed==0?0:1;
static void Require(bool value,string why) { if(!value)throw new InvalidOperationException(why); }
static bool Near(float a,float b)=>Math.Abs(a-b)<.0001f;
