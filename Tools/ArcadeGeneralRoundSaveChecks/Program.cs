using System.Reflection;
using Game.Tests;
if (args.Length == 3 && args[0] == "testClass=SlayerCheckpointTransaction" && args[1] == "maxTests=8" && args[2] == "includePassingTests=true") return SlayerCheckpointChecks.Run(8);
if (args.Length == 3 && args[0] == "testClass=RoundCheckpointTransaction" && args[1] == "maxTests=9" && args[2] == "includePassingTests=true") return SystemChecks.Run();
if (args.Length != 3 || args[0] != "testClass=RoundCheckpointTests" || args[1] != "maxTests=19" || args[2] != "includePassingTests=true") throw new Exception("Explicit short testClass, maxTests=19, includePassingTests=true required");
var methods = typeof(RoundCheckpointTests).GetMethods().Where(m => m.IsDefined(typeof(NUnit.Framework.TestAttribute))).ToArray();
if (methods.Length == 0 || methods.Length > 19) throw new Exception("Invalid test count");
int failures = 0;
foreach (var method in methods) { try { method.Invoke(new RoundCheckpointTests(),null); Console.WriteLine("PASS RoundCheckpointTests."+method.Name); } catch(Exception error) { failures++; Console.WriteLine("FAIL RoundCheckpointTests."+method.Name+": "+(error.InnerException ?? error).Message); } }
Console.WriteLine($"RoundCheckpointTests: {methods.Length-failures}/{methods.Length} passed"); return failures == 0 ? 0 : 1;
