using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using Luban;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    public sealed class DialogueFlowTests
    {
        private static DialogueOptionDefinition Option(string id, string target = "", global::DialogueOptionAction action = global::DialogueOptionAction.GoToNode, int order = 1)
            => new DialogueOptionDefinition(id, order, "test.option", action, target);
        private static DialogueNodeDefinition Node(string id, string next = "", params DialogueOptionDefinition[] options)
            => new DialogueNodeDefinition(id, "test.line", next, options);
        private static DialogueDefinition Graph(params DialogueNodeDefinition[] nodes) => new DialogueDefinition("test", "npc", "start", nodes);

        [Test]
        public void Sequence_AdvancesAndEndsWithoutRepeatingOldClick()
        {
            var session = new DialogueSession();
            Assert.That(session.TryBegin(Graph(Node("start", "end"), Node("end")), 2, 5, "npc.name"), Is.True);
            var first = session.Current;
            Assert.That(session.TryChoose(first.SessionId, first.Revision, null, out var action), Is.True);
            Assert.That(action, Is.EqualTo(global::DialogueOptionAction.GoToNode));
            Assert.That(session.Current.Node.Id, Is.EqualTo("end"));
            Assert.That(session.TryChoose(first.SessionId, first.Revision, null, out _), Is.False);
            Assert.That(session.TryChoose(session.Current.SessionId, session.Current.Revision, null, out _), Is.True);
            Assert.That(session.Current, Is.Null);
        }

        [Test]
        public void Branch_ReturnsToTopicsWithNewRevision()
        {
            var session = new DialogueSession();
            session.TryBegin(Graph(Node("start", "", Option("ask", "answer")), Node("answer", "start")), 2, 5, "npc.name");
            var first = session.Current;
            Assert.That(session.TryChoose(first.SessionId, first.Revision, "ask", out _), Is.True);
            Assert.That(session.Current.Node.Id, Is.EqualTo("answer"));
            session.TryChoose(session.Current.SessionId, session.Current.Revision, null, out _);
            Assert.That(session.Current.Node.Id, Is.EqualTo("start"));
            Assert.That(session.Current.Revision, Is.GreaterThan(first.Revision));
            Assert.That(session.TryChoose(first.SessionId, first.Revision, "ask", out _), Is.False);
        }

        [Test]
        public void FunctionChoice_ClosesBeforeDispatchAndCannotRepeat()
        {
            var session = new DialogueSession();
            session.TryBegin(Graph(Node("start", "", Option("function", "", global::DialogueOptionAction.OpenNpcFunction))), 2, 5, "npc.name");
            var current = session.Current;
            Assert.That(session.TryChoose(current.SessionId, current.Revision, "function", out var action), Is.True);
            Assert.That(action, Is.EqualTo(global::DialogueOptionAction.OpenNpcFunction));
            Assert.That(session.Current, Is.Null);
            Assert.That(session.TryChoose(current.SessionId, current.Revision, "function", out _), Is.False);
        }

        [Test]
        public void ReopenedConversation_RejectsOldCloseAndOldChoice()
        {
            var session = new DialogueSession(); var graph = Graph(Node("start"));
            session.TryBegin(graph, 2, 5, "npc.name"); var old = session.Current;
            Assert.That(session.Close(old.SessionId), Is.True);
            session.TryBegin(graph, 3, 6, "npc.other"); var current = session.Current;
            Assert.That(session.Close(old.SessionId), Is.False);
            Assert.That(session.TryChoose(old.SessionId, old.Revision, null, out _), Is.False);
            Assert.That(session.Current, Is.SameAs(current));
        }

        [Test]
        public void InvalidChoiceAndContinueOnBranch_DoNotMutate()
        {
            var session = new DialogueSession();
            session.TryBegin(Graph(Node("start", "", Option("end", "", global::DialogueOptionAction.End))), 2, 5, "npc.name");
            var current = session.Current;
            Assert.That(session.TryChoose(current.SessionId, current.Revision, "missing", out _), Is.False);
            Assert.That(session.TryChoose(current.SessionId, current.Revision, null, out _), Is.False);
            Assert.That(session.Current, Is.SameAs(current));
        }

        [Test]
        public void Begin_RejectsInvalidIdentityAndExistingConversation()
        {
            var session = new DialogueSession();var graph = Graph(Node("start"));
            Assert.That(session.TryBegin(graph, 0, 5, "npc.name"), Is.False);
            Assert.That(session.TryBegin(graph, 2, 0, "npc.name"), Is.False);
            Assert.That(session.TryBegin(graph, 2, 5, "npc.name"), Is.True);
            var current = session.Current;
            Assert.That(session.TryBegin(graph, 3, 6, "other"), Is.False);
            Assert.That(session.Current, Is.SameAs(current));
        }

        [Test]
        public void Graph_RejectsMissingTarget()
        { Assert.Throws<ArgumentException>(() => Graph(Node("start", "missing"))); }
        [Test]
        public void Graph_RejectsAmbiguousAdvance()
        { Assert.Throws<ArgumentException>(() => Graph(Node("start", "start", Option("end", "", global::DialogueOptionAction.End)))); }
        [Test]
        public void Graph_RejectsUnreachableNode()
        { Assert.Throws<ArgumentException>(() => Graph(Node("start"), Node("orphan"))); }
        [Test]
        public void Graph_RejectsDuplicateOrderAndIllegalFunctionTarget()
        {
            Assert.Throws<ArgumentException>(() => Graph(Node("start", "", Option("a", "start"), Option("b", "start"))));
            Assert.Throws<ArgumentException>(() => Graph(Node("start", "", Option("a", "start", global::DialogueOptionAction.OpenNpcFunction))));
        }

        [Test]
        public void RealConfig_GuideHasDemoAndOtherNpcKeepsDirectFunction()
        {
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name => new ByteBuf(File.ReadAllBytes("Assets/Game/Config/" + name + ".bytes"))));
            Assert.That(provider.TryGetNpcDialogue("npc.base.map-guide", out var graph, out var error), Is.True, error);
            Assert.That(graph, Is.Not.Null);
            Assert.That(graph.GetNode(graph.StartNodeId).Options.Count, Is.EqualTo(4));
            Assert.That(provider.TryGetNpcDialogue("npc.base.merchant", out var merchant, out error), Is.True, error);
            Assert.That(merchant, Is.Null);
            foreach (var language in new[] { "zh-CN", "en-US" })
            {
                var texts = JObject.Parse(File.ReadAllText("Assets/Game/Config/Localization/" + language + ".json"));
                foreach (var key in graph.TextKeys) Assert.That(texts[key], Is.Not.Null, language + ": " + key);
            }
        }

        [Test]
        public void DialogueCamera_FocusesNpcAndRestoresNormalShoulderLens()
        {
            var cameraGo = new GameObject("DialogueTestCamera", typeof(Camera));var player = new GameObject("DialogueTestPlayer");
            var top = ScriptableObject.CreateInstance<TopDownCameraProfile>();var shoulder = ScriptableObject.CreateInstance<ShoulderCameraProfile>();
            var ctrl = new PlayerCameraCtrl(cameraGo.GetComponent<Camera>(), player.transform, top, shoulder);
            try
            {
                ctrl.ApplyMode(PlayerCameraMode.Shoulder, 0, 15);
                Assert.That(ctrl.TrySetDialogueFocus(new Vector3(2, 0, 3)), Is.True);
                ctrl.LateUpdate(PlayerCameraMode.Shoulder, 0, 15);
                Assert.That(cameraGo.GetComponent<Camera>().fieldOfView, Is.EqualTo(shoulder.DialogueFieldOfView));
                Assert.That(Quaternion.Angle(cameraGo.transform.rotation, Quaternion.LookRotation(new Vector3(2,0,3))), Is.LessThan(.1f));
                Assert.That(ctrl.TrySetDialogueFocus(null), Is.True);
                ctrl.ApplyMode(PlayerCameraMode.Shoulder, 0, 15);
                Assert.That(cameraGo.GetComponent<Camera>().fieldOfView, Is.EqualTo(shoulder.FieldOfView));
                Assert.That(Quaternion.Angle(cameraGo.transform.rotation, Quaternion.Euler(15,0,0)), Is.LessThan(.1f));
            }
            finally { ctrl.Dispose(); UnityEngine.Object.DestroyImmediate(cameraGo); UnityEngine.Object.DestroyImmediate(player); UnityEngine.Object.DestroyImmediate(top); UnityEngine.Object.DestroyImmediate(shoulder); }
        }

        [Test]
        public void DialoguePrefab_HasGeneratedViewAndExternalChoicePrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Dialogue/Dialogue.prefab");
            Assert.That(prefab, Is.Not.Null, "通用对话必须有正式可加载的 Prefab");
            Assert.That(prefab.GetComponent<ReferenceCollector>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<BaseUI>(), Is.Not.Null);
            var view = prefab.GetComponent<DialogueUI>();
            Assert.That(view, Is.Not.Null);
            foreach (var field in typeof(DialogueUI).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                Assert.That(field.GetValue(view) as UnityEngine.Object, Is.Not.Null, field.Name);
            foreach (var scroll in prefab.GetComponentsInChildren<UnityEngine.UI.ScrollRect>(true))
                Assert.That(scroll.viewport.GetComponent<UnityEngine.UI.Image>().raycastTarget, Is.True);
            var list = prefab.GetComponentInChildren<CustomListView>(true);
            Assert.That(list, Is.Not.Null);
            Assert.That(list.itemPrefab, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(list.itemPrefab), Does.Contain("/Dialogue/Box/"));
            Assert.That(list.itemPrefab.GetComponent<BaseBoxUI>(), Is.Not.Null);
            var box = list.itemPrefab.GetComponent<DialogueChoiceBoxUI>();
            Assert.That(box.BtnChoice, Is.Not.Null);
            Assert.That(box.ChoiceText, Is.Not.Null);
        }
    }
}
