using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

/// <summary>将已采用的飞散隐藏覆盖写入指定正式Prefab；保留对象、材质和meta，不保存其他脏资源。</summary>
public static class AllExplosionsAuthoring
{
    public static string Run()
    {
        if(EditorApplication.isPlaying||EditorApplication.isCompiling||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")throw new Exception("仅本工程编辑态接入");
        var entries=new[]{
            new[]{"Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab","Content/VortexBarrelBlast","Sparks,BarrelFragments"},
            new[]{"Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab","Content/BombBlast","Sparks,Shards,Rays"},
            new[]{"Assets/Game/Prefabs/SceneObjects/ArcadeDroneExplosion.prefab","","Sparks"},
            new[]{"Assets/Game/Prefabs/SceneObjects/ArcadeWeaponGrenadePrimaryBlast.prefab","","blast-sparks"},
            new[]{"Assets/Game/Prefabs/SceneObjects/ArcadeWeaponGrenadeChildBlast.prefab","","blast-sparks"},
            new[]{"Assets/Game/Art/Arcade/VFX/EnemyExplosionDeath/EnemyExplosionDeath.prefab","","SmallFragments"}};
        string Hash(string path){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)));}
        int changed=0;
        foreach(string path in entries.Select(e=>e[0]).Distinct())
        {
            string beforeMeta=Hash(path+".meta");var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach(var row in entries.Where(e=>e[0]==path))
                {
                    var scope=string.IsNullOrEmpty(row[1])?root.transform:root.transform.Find(row[1]);if(scope==null)throw new Exception("Missing scope "+row[1]);
                    foreach(string name in row[2].Split(','))
                    {
                        var renderer=scope.GetComponentsInChildren<Renderer>(true).Single(r=>r.name==name);
                        renderer.enabled=false;PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);changed++;
                    }
                }
                if(PrefabUtility.SaveAsPrefabAsset(root,path)==null)throw new Exception("Save failed "+path);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
            if(beforeMeta!=Hash(path+".meta"))throw new Exception("Meta changed "+path);
        }
        return "Applied "+changed+" renderer overrides in five prefabs; all metadata hashes preserved.";
    }
}
