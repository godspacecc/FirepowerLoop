using System;
using Game;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

/// <summary>为正式掉落模板制作无碰撞空投箱；箱体随既有拾取物回池，不创建新的资源句柄。</summary>
public static class ArcadeWeaponDeliveryAuthoring
{
    public static string Run()
    {
        if(EditorApplication.isPlaying||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")throw new InvalidOperationException("仅本工程编辑态制作");
        var olive=Mat("DeliveryCrateOlive",new Color(.24f,.3f,.16f));var dark=Mat("DeliveryCrateDark",new Color(.065f,.08f,.075f));var mark=Mat("DeliveryCrateMark",new Color(.95f,.62f,.08f));
        const string path="Assets/Game/Prefabs/SceneObjects/TestResourcePickup.prefab";
        var prefab=PrefabUtility.LoadPrefabContents(path);
        try
        {
            var old=prefab.transform.Find("WeaponDeliveryCrate");if(old!=null)Object.DestroyImmediate(old.gameObject);
            var crate=new GameObject("WeaponDeliveryCrate");crate.transform.SetParent(prefab.transform,false);
            Part(crate.transform,"Base",new Vector3(0,.08f,0),new Vector3(1.45f,.16f,1.05f),dark);
            Part(crate.transform,"Front",new Vector3(0,.4f,-.49f),new Vector3(1.4f,.56f,.1f),olive);
            Part(crate.transform,"Back",new Vector3(0,.4f,.49f),new Vector3(1.4f,.56f,.1f),olive);
            for(int side=-1;side<=1;side+=2)
            {
                Part(crate.transform,"Side",new Vector3(side*.675f,.4f,0),new Vector3(.1f,.56f,.9f),olive);
                Part(crate.transform,"Frame",new Vector3(side*.72f,.4f,-.5f),new Vector3(.09f,.68f,.14f),dark);
                Part(crate.transform,"Latch",new Vector3(side*.43f,.61f,-.555f),new Vector3(.12f,.22f,.045f),mark);
                Part(crate.transform,"Foot",new Vector3(side*.5f,.025f,0),new Vector3(.22f,.05f,1.15f),dark);
            }
            var lid=new GameObject("LidHinge").transform;lid.SetParent(crate.transform,false);lid.localPosition=new Vector3(0,.72f,.53f);
            Part(lid,"Lid",new Vector3(0,0,-.53f),new Vector3(1.5f,.14f,1.13f),olive);
            for(int side=-1;side<=1;side+=2)Part(lid,"LidStrap",new Vector3(side*.43f,.09f,-.53f),new Vector3(.13f,.04f,1.13f),mark);
            var dustRoot=new GameObject("LandingDust",typeof(ParticleSystem));dustRoot.transform.SetParent(crate.transform,false);dustRoot.transform.localPosition=new Vector3(0,.1f,0);
            var dust=dustRoot.GetComponent<ParticleSystem>();dust.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=dust.main;main.loop=false;main.playOnAwake=false;main.startLifetime=new ParticleSystem.MinMaxCurve(.35f,.65f);main.startSize=new ParticleSystem.MinMaxCurve(.2f,.45f);main.startSpeed=0;main.maxParticles=30;main.simulationSpace=ParticleSystemSimulationSpace.World;
            var emission=dust.emission;emission.enabled=false;var shape=dust.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(1.2f,.05f,.85f);
            var velocity=dust.velocityOverLifetime;velocity.enabled=true;velocity.x=new ParticleSystem.MinMaxCurve(-1f,1f);velocity.y=new ParticleSystem.MinMaxCurve(.1f,.4f);velocity.z=new ParticleSystem.MinMaxCurve(-1f,1f);
            var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(new Color(.6f,.53f,.4f),0),new GradientColorKey(new Color(.3f,.27f,.2f),1)},new[]{new GradientAlphaKey(.4f,0),new GradientAlphaKey(0,1)});
            var colors=dust.colorOverLifetime;colors.enabled=true;colors.color=gradient;
            dust.GetComponent<ParticleSystemRenderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Art/Arcade/VFX/FirstBatch/Materials/BlastSmoke.mat");
            var view=crate.AddComponent<ArcadeWeaponDeliveryView>();var serialized=new SerializedObject(view);serialized.FindProperty("lid").objectReferenceValue=lid;serialized.FindProperty("landingDust").objectReferenceValue=dust;serialized.ApplyModifiedPropertiesWithoutUndo();view.ResetPresentation();
            var pickup=new SerializedObject(prefab.GetComponent<ResourcePickupView>());pickup.FindProperty("weaponDelivery").objectReferenceValue=view;pickup.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(prefab,path);AssetDatabase.SaveAssets();return "空投箱、翻盖和落地尘土已接入正式掉落模板";
        }
        finally{PrefabUtility.UnloadPrefabContents(prefab);}
    }
    private static Material Mat(string name,Color color)
    {
        var path="Assets/Game/Art/Arcade/Weapons/"+name+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(mat==null){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,path);}
        mat.SetColor("_BaseColor",color);mat.SetFloat("_Smoothness",.35f);EditorUtility.SetDirty(mat);return mat;
    }
    private static void Part(Transform parent,string name,Vector3 position,Vector3 scale,Material material)
    {
        var part=GameObject.CreatePrimitive(PrimitiveType.Cube);part.name=name;part.transform.SetParent(parent,false);part.transform.localPosition=position;part.transform.localScale=scale;
        part.GetComponent<Renderer>().sharedMaterial=material;Object.DestroyImmediate(part.GetComponent<Collider>());
    }
}
