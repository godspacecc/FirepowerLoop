using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Game;

/// <summary>通过实际装备页预览呈现器留存画面；只创建临时对象，不修改正式 Prefab 或存档。</summary>
public static class CaptureClothingPreview
{
    public static string Run()
    {
        var target=new GameObject("CaptureTarget",typeof(RectTransform),typeof(RawImage));
        var type=typeof(Game.Game).Assembly.GetType("Game.ClothingPreviewPresenter");
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Art/Player/OutfitPrototype/ClothingCharacterPreview.prefab");
        var presenter=(IDisposable)Activator.CreateInstance(type,new object[]{prefab,target.GetComponent<RawImage>()});
        var folder=Path.GetFullPath("../Tools/Blender/OutfitPreview/Runtime");Directory.CreateDirectory(folder);
        try
        {
            var pairs=new[]{new[]{"",""},new[]{"Top_RedHoodie","Pants_BlueDenim"},new[]{"Top_RedHoodie","Pants_TanCargo"},new[]{"Top_OliveUtility","Pants_BlueDenim"},new[]{"Top_OliveUtility","Pants_TanCargo"}};
            var previewRoot=(GameObject)type.GetField("root",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(presenter);
            var animator=previewRoot.GetComponentInChildren<Animator>();
            foreach(var skin in previewRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))skin.forceMatrixRecalculationPerRender=true;
            for(int i=0;i<pairs.Length*2;i++)
            {
                bool walking=i>=pairs.Length;int outfit=i%pairs.Length;
                animator.SetFloat("MoveSpeed",walking?3f:0f);animator.Update(.3f);
                type.GetMethod("Refresh").Invoke(presenter,new object[]{pairs[outfit][0],pairs[outfit][1]});
                var previous=RenderTexture.active;RenderTexture.active=(RenderTexture)target.GetComponent<RawImage>().texture;
                var image=new Texture2D(384,768,TextureFormat.RGB24,false);
                try{image.ReadPixels(new Rect(0,0,384,768),0,0);image.Apply();File.WriteAllBytes(Path.Combine(folder,(walking?"Clothing_Walk_":"Clothing_")+outfit+".png"),image.EncodeToPNG());}
                finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);}
            }
            return folder;
        }
        finally{presenter.Dispose();UnityEngine.Object.DestroyImmediate(target);}
    }
}
