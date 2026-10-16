namespace NUnit.Framework { [System.AttributeUsage(System.AttributeTargets.Method)] public sealed class TestAttribute : System.Attribute { } }
namespace UnityEngine { public static class Application { public static string persistentDataPath => throw new System.InvalidOperationException("Tests must select isolated storage"); } }
