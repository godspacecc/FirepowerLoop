using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>
    /// 锁定游戏 Prefab 的材质兼容性规则，防止空材质和旧管线 Shader 再次进入资源包。
    /// </summary>
    public sealed class PrefabMaterialValidationTests
    {
        private readonly List<UnityEngine.Object> transientObjects = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = transientObjects.Count - 1; i >= 0; i--)
            {
                if (transientObjects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(transientObjects[i]);
                }
            }

            transientObjects.Clear();
        }

        [Test]
        public void GamePrefabsHaveNoIncompatibleRendererMaterials()
        {
            Type validator = RequireValidatorType();
            MethodInfo method = validator.GetMethod("TryValidateGamePrefabs", BindingFlags.Static | BindingFlags.Public);
            Assert.That(method, Is.Not.Null);

            var arguments = new object[] { null };
            bool success = (bool)method.Invoke(null, arguments);
            Assert.That(success, Is.True, arguments[0] as string);
        }

        [Test]
        public void ValidatorRejectsNullAndStandardMaterials()
        {
            Type validator = RequireValidatorType();
            MethodInfo method = validator.GetMethod("ScanGameObject", BindingFlags.Static | BindingFlags.Public);
            Assert.That(method, Is.Not.Null);

            var root = Track(new GameObject("MaterialValidationRoot"));
            var nullMaterialObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nullMaterialObject.name = "NullMaterial";
            nullMaterialObject.transform.SetParent(root.transform, false);
            nullMaterialObject.GetComponent<Renderer>().sharedMaterial = null;

            Shader standardShader = Shader.Find("Standard");
            Assert.That(standardShader, Is.Not.Null, "测试环境必须能够定位内置 Standard Shader。 ");
            var standardMaterial = Track(new Material(standardShader) { name = "ForbiddenStandard" });
            var standardObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            standardObject.name = "StandardMaterial";
            standardObject.transform.SetParent(root.transform, false);
            standardObject.GetComponent<Renderer>().sharedMaterial = standardMaterial;

            var issues = method.Invoke(null, new object[] { root, "Assets/Test/MaterialValidation.prefab" }) as IEnumerable;
            Assert.That(issues, Is.Not.Null);

            var messages = new List<string>();
            foreach (object issue in issues)
            {
                messages.Add(issue?.ToString() ?? string.Empty);
            }

            Assert.That(messages, Has.Count.EqualTo(2));
            Assert.That(messages, Has.Some.Contains("NullMaterial").And.Contains("材质槽为空"));
            Assert.That(messages, Has.Some.Contains("StandardMaterial").And.Contains("Standard"));
        }

        private T Track<T>(T value) where T : UnityEngine.Object
        {
            transientObjects.Add(value);
            return value;
        }

        private static Type RequireValidatorType()
        {
            Type type = Type.GetType("Game.Editor.PrefabMaterialValidator, Assembly-CSharp-Editor");
            Assert.That(type, Is.Not.Null, "未找到 PrefabMaterialValidator Editor 类型。");
            return type;
        }
    }
}
