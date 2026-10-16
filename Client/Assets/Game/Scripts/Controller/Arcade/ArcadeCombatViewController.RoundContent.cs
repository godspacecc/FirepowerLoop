using System;
using System.Collections.Generic;
using System.Linq;
using FirepowerLoop.Arcade;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        /// <summary>轮级环境、事件及专属敌人的唯一资源拥有者；切图和 End 成对清理。</summary>
        private GameObject roundContentRoot, roundEnvironmentRoot, roundAreaRoot, roundEnemyRoot;
        private ArcadeRoundLayoutParameters roundLayout;
        private readonly List<Material> roundOwnedMaterials = new();
        private readonly List<Mesh> roundOwnedMeshes = new();
        private readonly Dictionary<uint,Material> roundPalette = new();
        private readonly Dictionary<int,RoundEnemyVisual> roundEnemies = new();
        private readonly Dictionary<int,Vector2> roundEnemyPositions = new();
        private readonly Dictionary<int,GameObject> roundAreas = new();
        private readonly Dictionary<int,string> roundAreaIdentities = new();
        private readonly Dictionary<Renderer,bool> roundBaseRendererVisibility = new();
        private string roundLayoutLoadError;
        private bool roundLayoutReady;
        /// <summary>正式 Begin 传入的边界与配置半径；零值表示尚未准备，不设置业务默认值。</summary>
        private float roundHalfWidth,roundHalfHeight,roundPlayerRadius;
        private bool roundBoundsConfigured;
        private sealed class RoundEnemyVisual
        {
            public int SpecialKind, BossKind;
            public GameObject Root, Warning, HealthFill;
            public Vector2 Position;
        }
        public bool IsRoundLayoutReady => roundLayoutReady && root != null;
        public string RoundLayoutLoadError => roundLayoutLoadError;

        /// <summary>只记录当前战场权威尺寸；尺寸变化使旧候选失效，下一次 SetRoundLayout 重新准备。</summary>
        public void ConfigureRoundBounds(float halfWidth,float halfHeight,float radius)
        {
            if(float.IsNaN(halfWidth)||float.IsInfinity(halfWidth)||float.IsNaN(halfHeight)||float.IsInfinity(halfHeight)||
                float.IsNaN(radius)||float.IsInfinity(radius)||halfWidth<=0||halfHeight<=0||radius<=0)
                throw new ArgumentException("轮级表现边界必须为正有限值");
            if(roundHalfWidth!=halfWidth||roundHalfHeight!=halfHeight||roundPlayerRadius!=radius)roundLayoutReady=false;
            roundHalfWidth=halfWidth;roundHalfHeight=halfHeight;roundPlayerRadius=radius;roundBoundsConfigured=true;
        }

        /// <summary>同步准备候选表现，成功后才公开就绪；不创建规则碰撞体或改变岛岸。</summary>
        public void SetRoundLayout(ArcadeRoundLayoutParameters layout)
        {
            if(layout==null){roundLayoutLoadError="轮级布局配置缺失";roundLayoutReady=false;return;}
            if(roundLayout?.Id==layout.Id && roundContentRoot!=null && roundContentRoot.transform.parent==root?.transform && roundLayoutReady)return;
            ReleaseRoundContent();roundLayout=layout;
            if(root==null){roundLayoutLoadError="战斗动态根未准备";return;}
            try
            {
                if(!roundBoundsConfigured)throw new InvalidOperationException("轮级表现尚未接收权威场地边界");
                if(!layout.FitsArena(roundHalfWidth,roundHalfHeight,roundPlayerRadius))throw new InvalidOperationException("轮级地图通路校验失败："+layout.Id);
                roundContentRoot=RoundGroup(root.transform,"RoundContent_"+layout.Id);
                roundEnvironmentRoot=RoundGroup(roundContentRoot.transform,"Environment_"+layout.ThemeKey);
                roundAreaRoot=RoundGroup(roundContentRoot.transform,"AuthoritativeAreas");
                roundEnemyRoot=RoundGroup(roundContentRoot.transform,"DedicatedEnemies");
                if(layout.ArenaConfigNumber!=1)BuildRoundEnvironment(layout);
                BuildRoundEntryMarkers(layout);
                roundLayoutReady=true;roundLayoutLoadError=null;SyncRoundContentEnvironment();
            }
            catch(Exception e)
            {
                ReleaseRoundContent();roundLayout=layout;roundLayoutLoadError=e.Message;
                Log.Error("[ArcadeCombatViewController] 轮级专用表现准备失败："+e.Message);
            }
        }
        private static GameObject RoundGroup(Transform parent,string name)
        {
            var go=new GameObject(name);go.transform.SetParent(parent,false);return go;
        }
        /// <summary>所有程序材质从可用正式 Shader 建立，按颜色共享且由当前轮级根唯一释放。</summary>
        private Material RoundMaterial(uint rgba)
        {
            if(roundPalette.TryGetValue(rgba,out var existing))return existing;
            var shader=Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Unlit");
            if(shader==null)throw new InvalidOperationException("轮级表现缺少可用 Shader");
            var color=new Color(((rgba>>24)&255)/255f,((rgba>>16)&255)/255f,((rgba>>8)&255)/255f,(rgba&255)/255f);
            var material=new Material(shader){name="RoundOwned_"+rgba.ToString("X8"),color=color};
            if(material.HasProperty("_BaseColor"))material.SetColor("_BaseColor",color);
            roundPalette.Add(rgba,material);roundOwnedMaterials.Add(material);return material;
        }
        private GameObject RoundPart(Transform parent,string name,PrimitiveType shape,Vector3 position,Vector3 scale,uint color,float angle=0)
        {
            var go=GameObject.CreatePrimitive(shape);go.name=name;go.transform.SetParent(parent,false);
            var collider=go.GetComponent<Collider>();if(collider!=null){collider.enabled=false;DestroyObject(collider);}
            go.transform.localPosition=position;go.transform.localScale=scale;go.transform.localRotation=Quaternion.Euler(0,angle,0);
            go.GetComponent<Renderer>().sharedMaterial=RoundMaterial(color);return go;
        }
        private GameObject RoundBox(Transform parent,string name,float x,float y,float z,float w,float h,float d,uint color,float angle=0)
            =>RoundPart(parent,name,PrimitiveType.Cube,new Vector3(x,y,z),new Vector3(w,h,d),color,angle);
        private GameObject RoundCylinder(Transform parent,string name,float x,float y,float z,float diameter,float height,uint color)
            =>RoundPart(parent,name,PrimitiveType.Cylinder,new Vector3(x,y,z),new Vector3(diameter,height*.5f,diameter),color);
        /// <summary>地面与装饰只是同源几何的投影；障碍轮廓完全按配置尺寸，边缘地标置于可行走范围外。</summary>
        private void BuildRoundEnvironment(ArcadeRoundLayoutParameters p)
        {
            var t=RoundGroup(roundEnvironmentRoot.transform,"ThemeDecor").transform;
            // 只缩放地面、道路和边缘装饰；实际障碍与入口仍逐项使用配置的米制坐标。
            t.localScale=new Vector3(roundHalfWidth/19.5f,1,roundHalfHeight/19.5f);
            uint soil=0x777267FFu,stone=0x9D9C8BFFu,trim=0xD4C9AAFFu;
            switch(p.ThemeKey)
            {
                case "babylon":soil=0x9D845FFFu;stone=0xC1AA7DFFu;break;
                case "barrage":soil=0x52684CFFu;stone=0x6E5841FFu;break;
                case "graveyard":soil=0x3D494AFFu;stone=0x81918FFFu;break;
                case "nuketown":soil=0x647179FFu;stone=0xCFB258FFu;break;
                case "henge":soil=0x67714DFFu;stone=0xB0B09EFFu;break;
                case "volcano":soil=0x3E3537FFu;stone=0x713A35FFu;break;
                case "fort":soil=0x777260FFu;stone=0x9D8A69FFu;break;
                case "church":soil=0x626169FFu;stone=0xA3A4A3FFu;break;
                case "motel":soil=0x6D716DFFu;stone=0xA9B5A0FFu;break;
                case "museum":soil=0xB2AAA0FFu;stone=0x768E9CFFu;break;
                case "garden":soil=0x435A43FFu;stone=0x698649FFu;break;
                case "koi":soil=0x9B9982FFu;stone=0x756D61FFu;break;
                case "homestead":soil=0x9BB6BAFFu;stone=0xA97B62FFu;break;
                case "shiver":soil=0x6F9AAEFFu;stone=0xC8DFDFFFu;break;
                case "piazza":soil=0xBBA48AFFu;stone=0xE2CCAEFFu;break;
                case "estate":soil=0x968478FFu;stone=0xAA9E8CFFu;break;
                case "swamp":soil=0x465348FFu;stone=0x797B58FFu;break;
                case "spore":soil=0x49495AFFu;stone=0x8D6B8BFFu;break;
                case "papaback":soil=0x464D58FFu;stone=0x73808EFFu;break;
                default:throw new InvalidOperationException("未知主题表现："+p.ThemeKey);
            }
            RoundBox(t,"ThemeGround",0,-.15f,0,39,.22f,39,soil);
            // 每轮路径落脚使用障碍真实空隙，不以可见水池遮盖不可达地形。
            if(p.ThemeKey=="nuketown"||p.ThemeKey=="motel")
            {
                RoundBox(t,"Road",0,-.025f,0,8,.025f,38,0x41474BFFu);
                for(int z=-16;z<=16;z+=4)RoundBox(t,"RoadStripe",0,0,z,.18f,.02f,1.5f,trim);
            }
            else if(p.ThemeKey=="koi"||p.ThemeKey=="homestead"||p.ThemeKey=="shiver"||p.ThemeKey=="swamp")
            {
                for(int x=-16;x<=16;x+=8)RoundBox(t,"WetGroundChannels",x,-.022f,0,3,.03f,38,p.ThemeKey=="swamp"?0x3A665AFFu:0x74AABFFFu);
                for(int x=-16;x<=16;x+=4)RoundBox(t,"CrossingPlanks",x,.005f,-11,3.8f,.04f,1.6f,p.ThemeKey=="koi"?0x9B7050FFu:trim);
            }
            else if(p.ThemeKey=="museum"||p.ThemeKey=="piazza"||p.ThemeKey=="estate"||p.ThemeKey=="church")
            {
                for(int x=-18;x<19;x+=3)for(int z=-18;z<19;z+=3)if(((x+z)/3&1)==0)RoundBox(t,"CourtyardTile",x,-.02f,z,2.85f,.03f,2.85f,trim);
            }
            else if(p.ThemeKey=="papaback")
            {
                foreach(float x in new[]{-4f,4f})RoundBox(t,"IndustrialRail",x,.01f,0,.25f,.07f,38,0xB1B6B9FFu);
                for(int z=-18;z<=18;z+=3)RoundBox(t,"RailSleeper",0,-.015f,z,9,.05f,.4f,0x30343CFFu);
            }
            else if(p.ThemeKey=="volcano"||p.ThemeKey=="henge")
            {
                for(int i=0;i<8;i++){float a=i*Mathf.PI/4;RoundBox(t,"MagmaSeam",Mathf.Sin(a)*14,-.01f,Mathf.Cos(a)*14,.35f,.04f,5,0xBF683AFFu,-i*45);}
            }
            else
            {
                RoundBox(t,"MainWalk",0,-.025f,0,5,.035f,38,trim);
                RoundBox(t,"CrossWalk",0,-.02f,-11,38,.035f,3,trim);
            }
            int index=0;
            foreach(var o in p.Obstacles)BuildRoundObstacle(roundEnvironmentRoot.transform,p.ThemeKey,o,stone,index++);
            // 地标横跨北侧外围，与实际战斗范围分离，避免假障碍挡住并行通路。
            BuildRoundLandmark(t,p.ThemeKey,stone,p.RoundNumber);
        }
        private void BuildRoundObstacle(Transform parent,string theme,ArcadeLayoutObstacle o,uint stone,int index)
        {
            var holder=RoundGroup(parent,"Obstacle_"+index).transform;
            float w=o.HalfWidth*2,d=o.HalfHeight*2,x=o.X,z=o.Y;
            switch(theme)
            {
                case "graveyard":
                    RoundBox(holder,"BurialPlinth",x,.22f,z,w,.45f,d,stone);
                    RoundBox(holder,"Headstone",x,1.1f,z,w*.6f,1.6f,Mathf.Min(d,.55f),stone);
                    RoundBox(holder,"StoneCross",x,1.8f,z,w*.85f,.25f,.6f,0xBEC8BEFFu);break;
                case "nuketown":case "motel":
                    RoundBox(holder,"VehicleBody",x,.7f,z,w,1.4f,d,stone);
                    RoundBox(holder,"VehicleRoof",x,1.7f,z,w*.82f,.65f,d*.7f,0xD5D9CDFFu);
                    RoundBox(holder,"Windshield",x,1.7f,z+d*.36f,w*.7f,.5f,.04f,0x446A7EFFu);break;
                case "henge":case "fort":case "church":
                    RoundBox(holder,"MasonryBase",x,.65f,z,w,1.3f,d,stone);
                    RoundBox(holder,"GatePillarA",x-w*.35f,1.9f,z,w*.22f,2,d*.65f,stone);
                    RoundBox(holder,"GatePillarB",x+w*.35f,1.9f,z,w*.22f,2,d*.65f,stone);
                    RoundBox(holder,"Lintel",x,2.8f,z,w,.35f,d*.7f,0xD1CFB4FFu);break;
                case "garden":case "barrage":
                    RoundBox(holder,"Planter",x,.3f,z,w,.6f,d,0x78634BFFu);
                    RoundBox(holder,"Hedge",x,1.05f,z,w*.92f,1.2f,d*.92f,stone);
                    for(int i=0;i<3;i++)RoundPart(holder,"Foliage",PrimitiveType.Sphere,new Vector3(x+(i-1)*w*.22f,1.5f,z),new Vector3(w*.45f,1,d*.8f),0x7E9B52FFu);break;
                case "museum":case "piazza":case "estate":
                    RoundBox(holder,"ExhibitPlinth",x,.45f,z,w,.9f,d,stone);
                    RoundPart(holder,"Sculpture",PrimitiveType.Capsule,new Vector3(x,1.7f,z),new Vector3(Mathf.Min(w,1),1.1f,Mathf.Min(d,1)),0xD8CBB2FFu);
                    if(theme=="museum")RoundBox(holder,"DisplayGlass",x,1,z,w*.9f,.1f,d*.9f,0x88B7C0FFu);break;
                case "spore":
                    RoundBox(holder,"RootBed",x,.2f,z,w,.4f,d,0x65566DFFu);
                    RoundCylinder(holder,"MushroomStalk",x,1,z,Mathf.Min(w,d)*.5f,2,0xB9B49DFFu);
                    RoundPart(holder,"MushroomCap",PrimitiveType.Sphere,new Vector3(x,2,z),new Vector3(w*.95f,.8f,d*.95f),stone);break;
                case "swamp":
                    RoundBox(holder,"PeatBank",x,.4f,z,w,.8f,d,stone);
                    RoundBox(holder,"FallenTrunk",x,1,z,w*.9f,.6f,d*.65f,0x5A493DFFu);
                    for(int i=0;i<3;i++)RoundCylinder(holder,"Reed",x+(i-1)*w*.25f,1.3f,z,.15f,2,0xB1A36AFFu);break;
                case "koi":
                    RoundBox(holder,"PondBank",x,.35f,z,w,.7f,d,stone);
                    RoundBox(holder,"PondSurface",x,.72f,z,w*.72f,.025f,d*.72f,0x4D9EADFFu);
                    RoundCylinder(holder,"StoneLantern",x,.95f,z,Mathf.Min(w,d)*.4f,1.5f,0xD0CEB6FFu);break;
                case "homestead":
                    RoundBox(holder,"BarnBase",x,.7f,z,w,1.4f,d,stone);
                    RoundBox(holder,"BarnEaves",x,1.6f,z,w*1.02f,.4f,d*1.02f,0xDCE9DFFFu);break;
                case "papaback":
                    RoundBox(holder,"IndustrialHousing",x,.8f,z,w,1.6f,d,stone);
                    RoundBox(holder,"WarningBand",x,1.65f,z,w,.12f,d,0xD1A13BFFu);
                    RoundCylinder(holder,"Exhaust",x,2.3f,z,Mathf.Min(w,d)*.65f,1.3f,0xA9B2AFFFu);break;
                case "shiver":case "volcano":
                    RoundBox(holder,"RockFootprint",x,.5f,z,w,1,d,stone);
                    RoundPart(holder,"RockCrown",PrimitiveType.Sphere,new Vector3(x,1,z),new Vector3(w*.9f,1.8f,d*.9f),theme=="shiver"?0xB7DADFFFu:0x754E43FFu);break;
                default:
                    RoundBox(holder,"RuinFoundation",x,.4f,z,w,.8f,d,stone);
                    RoundBox(holder,"BrokenWall",x,1.3f,z,w*.9f,1.4f,d*.75f,stone);break;
            }
        }
        private void BuildRoundLandmark(Transform t,string theme,uint color,int round)
        {
            float z=20.7f;
            switch(theme)
            {
                case "babylon":case "fort":case "church":case "estate":
                    foreach(float x in new[]{-10f,10f}){RoundBox(t,"BoundaryTower",x,2,z,3,4,2.5f,color);RoundBox(t,"TowerCrown",x,4.2f,z,3.5f,.4f,3,color);}
                    RoundBox(t,"Gateway",0,4.1f,z,18,.5f,1.8f,color);
                    if(theme=="church"){RoundBox(t,"BellTower",0,4.5f,z+1,3,8,3,color);RoundBox(t,"ChurchCross",0,9,z+1,3,.4f,.4f,color);RoundBox(t,"CrossStem",0,8.7f,z+1,.4f,3,.4f,color);}
                    if(theme=="babylon")for(int i=0;i<4;i++)RoundBox(t,"ZigguratTier",0,.5f+i*.6f,z+2,12-i*2,.6f,4-i*.6f,color);
                    if(theme=="fort")for(int x=-12;x<=12;x+=3)RoundBox(t,"Battlement",x,4.8f,z,1.3f,1.3f,1.8f,color);
                    if(theme=="estate"){RoundBox(t,"ManorBalcony",0,2.7f,z-1,15,.35f,3,color);for(int x=-6;x<=6;x+=3)RoundCylinder(t,"BalconyColumn",x,1.4f,z-2,.35f,2.8f,color);}
                    break;
                case "barrage":
                    RoundBox(t,"BoundaryBuilding",0,2,z,15,4,3,color);RoundBox(t,"BuildingRoof",0,4.3f,z,16,.6f,4,0x79685CFFu);
                    for(int x=-6;x<=6;x+=3)RoundBox(t,"BuildingWindow",x,2,z-1.53f,1.4f,1.4f,.04f,0x334854FFu);
                    foreach(float x in new[]{-14f,14f}){RoundCylinder(t,"PineTrunk",x,2,z,.7f,4,0x675844FFu);RoundPart(t,"PineCrown",PrimitiveType.Capsule,new Vector3(x,4,z),new Vector3(3,3,3),0x4B6B48FFu);}break;
                case "motel":
                    RoundBox(t,"MotelRoomRow",0,1.6f,z,25,3.2f,3,color);RoundBox(t,"MotelCanopy",0,3.4f,z-1,27,.3f,5,0xD5D1B1FFu);
                    for(int x=-10;x<=10;x+=4){RoundBox(t,"RoomDoor",x,1.2f,z-1.53f,1,2.4f,.05f,0x37484FFFu);RoundCylinder(t,"WalkwayPost",x,1.5f,z-2.5f,.16f,3,0xD5D1B1FFu);}
                    RoundBox(t,"RoadsideMotelSign",-16,4,z,4,2,.4f,0xD87A68FFu);RoundCylinder(t,"SignPole",-16,2,z,.2f,4,color);break;
                case "nuketown":
                    foreach(float x in new[]{-10f,10f}){RoundBox(t,"SuburbanHouse",x,2,z,7,4,4,color);RoundBox(t,"SlopedRoofA",x-1.8f,4.5f,z,4.2f,.5f,5,0x975B4AFFu,0);RoundBox(t,"GarageDoor",x,1.4f,z-2.03f,4,2.8f,.05f,0xD3DBD1FFu);}
                    RoundCylinder(t,"WaterTank",0,4,z+2,4,3,0xA8B5B9FFu);foreach(float x in new[]{-1.4f,1.4f})RoundCylinder(t,"TankLeg",x,2,z+2,.25f,4,color);break;
                case "homestead":
                    RoundBox(t,"FarmBarn",0,2.2f,z,13,4.4f,5,color);RoundBox(t,"BarnDoor",0,1.8f,z-2.53f,4,3.6f,.05f,0x553F38FFu);
                    var roofA=RoundBox(t,"BarnRoofA",-3.2f,5,z,7.3f,.4f,6,0xE1E9DEFFu);roofA.transform.localRotation=Quaternion.Euler(0,0,25);
                    var roofB=RoundBox(t,"BarnRoofB",3.2f,5,z,7.3f,.4f,6,0xE1E9DEFFu);roofB.transform.localRotation=Quaternion.Euler(0,0,-25);
                    RoundCylinder(t,"GrainSilo",11,3,z,4,6,0xB7C2B8FFu);break;
                case "graveyard":
                    RoundBox(t,"Mausoleum",0,2,z,8,4,3,color);RoundBox(t,"MausoleumDoor",0,1.4f,z-1.55f,2,2.8f,.08f,0x222D31FFu);
                    RoundBox(t,"MausoleumCross",0,5,z,3,.4f,.4f,color);RoundBox(t,"CrossStem",0,4.8f,z,.4f,3,.4f,color);break;
                case "museum":case "piazza":
                    for(int x=-12;x<=12;x+=6)RoundCylinder(t,"Colonnade",x,2,z,1,4,color);
                    RoundBox(t,"Pediment",0,4.4f,z,28,.7f,2.5f,color);break;
                case "henge":
                    for(int x=-12;x<=12;x+=6)RoundBox(t,"StandingStone",x,2+(x%4)*.1f,z,2,4,1.5f,color,round*7);break;
                case "volcano":
                    RoundPart(t,"VolcanicMassif",PrimitiveType.Sphere,new Vector3(0,1.5f,23),new Vector3(20,9,8),color);
                    RoundCylinder(t,"CraterGlow",0,5.8f,22,6,.3f,0xE98842FFu);break;
                case "koi":
                    foreach(float x in new[]{-7f,7f})RoundCylinder(t,"ToriiPillar",x,2.8f,z,.75f,5.6f,0xAA584BFFu);
                    RoundBox(t,"ToriiTop",0,5.6f,z,17,.6f,1.3f,0xAA584BFFu);RoundBox(t,"ToriiLintel",0,4.5f,z,15,.4f,.7f,color);break;
                case "garden":case "spore":case "swamp":
                    for(int x=-15;x<=15;x+=5){RoundCylinder(t,"BoundaryTrunk",x,2,z,.8f,4,0x675844FFu);RoundPart(t,theme=="spore"?"GiantCap":"TreeCanopy",PrimitiveType.Sphere,new Vector3(x,4,z),new Vector3(4,theme=="spore"?1:3.5f,4),color);}break;
                case "shiver":
                    for(int x=-15;x<=15;x+=5)RoundPart(t,"IceSpire",PrimitiveType.Capsule,new Vector3(x,2,z),new Vector3(3,3+((x+15)/5%3),2),color);break;
                case "papaback":
                    foreach(float x in new[]{-10f,10f}){RoundCylinder(t,"RocketBody",x,3,z,2.5f,6,color);RoundPart(t,"RocketNose",PrimitiveType.Sphere,new Vector3(x,6,z),new Vector3(2.5f,2,2.5f),0xA3534BFFu);}
                    RoundBox(t,"Gantry",0,5.5f,z,22,.5f,2,0xB9A055FFu);break;
            }
        }
        private void BuildRoundEntryMarkers(ArcadeRoundLayoutParameters p)
        {
            int i=0;foreach(var entry in p.Entries)
            {
                var go=RoundBox(roundEnvironmentRoot.transform,"Entry_"+i++,entry.X,.025f,entry.Y,1.6f,.06f,1.6f,0xBBAF75FFu,45);
                go.transform.localScale=new Vector3(1,.06f,1);
            }
        }
        /// <summary>宝藏房覆盖期间隐藏轮级主题；恢复时仍使用同一根，不刷新任何规则对象。</summary>
        private void SyncRoundContentEnvironment()
        {
            if(roundContentRoot==null)return;
            roundContentRoot.SetActive(!contentInRoom);
            if(roundLayout?.ArenaConfigNumber!=1 && !contentInRoom && roundLayoutReady)
            {
                HideIslandBaseArena();if(contentEnvironment!=null)contentEnvironment.SetActive(false);
                foreach(Transform child in root.transform)
                {
                    if(!child.name.StartsWith("Obstacle_",StringComparison.Ordinal))continue;
                    foreach(var renderer in child.GetComponentsInChildren<Renderer>(true))
                    {
                        if(!roundBaseRendererVisibility.ContainsKey(renderer))roundBaseRendererVisibility.Add(renderer,renderer.enabled);
                        renderer.enabled=false;
                    }
                }
            }
        }
        /// <summary>用权威快照替换危险标记；无快照时立刻移除，不自行推进计时或伤害。</summary>
        public void SetRoundContent(double elapsed,IReadOnlyList<ArcadeContentAreaSnapshot> areas)
        {
            if(!IsRoundLayoutReady||roundAreaRoot==null)return;
            var active=new HashSet<int>();
            foreach(var area in areas??Array.Empty<ArcadeContentAreaSnapshot>())
            {
                if(float.IsNaN(area.X)||float.IsNaN(area.Y)||float.IsNaN(area.Radius)||float.IsInfinity(area.X)||float.IsInfinity(area.Y)||float.IsInfinity(area.Radius)||area.Radius<=0 ||
                    (area.Segment&&(float.IsNaN(area.EndX)||float.IsInfinity(area.EndX)||float.IsNaN(area.EndY)||float.IsInfinity(area.EndY))))continue;
                var delta=area.Segment ? new Vector2(area.EndX-area.X,area.EndY-area.Y) : Vector2.zero;
                float length=delta.magnitude;
                active.Add(area.Id);string identity=area.Kind+":"+area.Warning+":"+area.Radius+":"+area.Segment+":"+length;
                if(roundAreaIdentities.TryGetValue(area.Id,out var old)&&old!=identity){DestroyRoundVisual(roundAreas[area.Id]);roundAreas.Remove(area.Id);}
                if(!roundAreas.TryGetValue(area.Id,out var go))
                {
                    go=RoundGroup(roundAreaRoot.transform,"Area_"+area.Id+"_Kind_"+area.Kind);roundAreas[area.Id]=go;roundAreaIdentities[area.Id]=identity;
                    uint color=area.Warning?0xEBCB5AFFu:area.Kind==7||area.Kind==8?0x97B43BFFu:area.Kind==5||area.Kind==6?0xA9DDEAFFu:0xDE6255FFu;
                    if(area.Segment)
                    {
                        RoundCapsuleBoundary(go.transform,area.Radius,length,.15f,color);
                        if(!area.Warning&&length>0)RoundBox(go.transform,"AuthoritativeBeam",0,.1f,0,area.Radius*.2f,.08f,length,color);
                    }
                    else
                    {
                        RoundRing(go.transform,"ExactDamageBoundary",area.Radius,.15f,color);
                        if(!area.Warning)RoundCylinder(go.transform,"DangerCore",0,.035f,0,area.Radius*.4f,.04f,color);
                    }
                }
                go.transform.localPosition=new Vector3(area.X+delta.x*.5f,.03f,area.Y+delta.y*.5f);
                go.transform.localRotation=area.Segment&&length>.00001f ? Quaternion.LookRotation(new Vector3(delta.x,0,delta.y)) : Quaternion.identity;
            }
            foreach(int id in roundAreas.Keys.Where(id=>!active.Contains(id)).ToArray()){DestroyRoundVisual(roundAreas[id]);roundAreas.Remove(id);roundAreaIdentities.Remove(id);}
        }
        /// <summary>平面空心边界保留地面和掉落可见性；每个生成网格由轮级拥有者释放。</summary>
        private GameObject RoundRing(Transform parent,string name,float radius,float width,uint color)
        {
            const int count=48;var vertices=new Vector3[count*2];var triangles=new int[count*6];
            for(int i=0;i<count;i++){float a=i*Mathf.PI*2/count;var direction=new Vector3(Mathf.Sin(a),0,Mathf.Cos(a));vertices[i*2]=direction*Mathf.Max(0,radius-width);vertices[i*2+1]=direction*radius;int j=(i+1)%count;int t=i*6;triangles[t]=i*2;triangles[t+1]=i*2+1;triangles[t+2]=j*2;triangles[t+3]=i*2+1;triangles[t+4]=j*2+1;triangles[t+5]=j*2;}
            var mesh=new Mesh{name="RoundOwnedRing"};mesh.vertices=vertices;mesh.triangles=triangles;mesh.RecalculateNormals();roundOwnedMeshes.Add(mesh);
            var go=RoundGroup(parent,name);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=RoundMaterial(color);return go;
        }
        /// <summary>按线段两端和真实半径建立胶囊边界；局部 Z 轴对准权威射线，端帽不随线长拉伸。</summary>
        private GameObject RoundCapsuleBoundary(Transform parent,float radius,float length,float width,uint color)
        {
            const int halfSamples=24,count=(halfSamples+1)*2;
            var vertices=new Vector3[count*2];var triangles=new int[count*6];
            float inner=Mathf.Max(0,radius-Mathf.Min(width,radius));
            for(int cap=0;cap<2;cap++)for(int sample=0;sample<=halfSamples;sample++)
            {
                int i=cap*(halfSamples+1)+sample;
                float angle=-Mathf.PI*.5f+sample*Mathf.PI/halfSamples+cap*Mathf.PI;
                float z=cap==0 ? length*.5f : -length*.5f;
                var direction=new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle));
                vertices[i*2]=new Vector3(0,0,z)+direction*inner;
                vertices[i*2+1]=new Vector3(0,0,z)+direction*radius;
                int j=(i+1)%count,t=i*6;
                triangles[t]=i*2;triangles[t+1]=i*2+1;triangles[t+2]=j*2;
                triangles[t+3]=i*2+1;triangles[t+4]=j*2+1;triangles[t+5]=j*2;
            }
            var mesh=new Mesh{name="RoundOwnedCapsuleBoundary"};mesh.vertices=vertices;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();roundOwnedMeshes.Add(mesh);
            var go=RoundGroup(parent,"ExactSegmentBoundary");go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=RoundMaterial(color);return go;
        }
        /// <summary>记录主表现刚接收的权威坐标；专用敌人不从场景搜索或积分位移。</summary>
        public void RecordRoundEnemyPosition(int id,Vector2 position)
        {
            roundEnemyPositions[id]=position;
            if(roundEnemies.TryGetValue(id,out var visual))
            {
                var delta=position-visual.Position;visual.Root.transform.localPosition=new Vector3(position.x,0,position.y);
                if(delta.sqrMagnitude>.00001f)visual.Root.transform.localRotation=Quaternion.LookRotation(new Vector3(delta.x,0,delta.y));visual.Position=position;
            }
        }
        public bool HasRoundEnemyContent(int id)=>roundEnemies.ContainsKey(id);
        /// <summary>只组合纯规则给出的专用轮廓；首帧撤下旧模型，后续由 SetEnemy hook 跳过旧资源请求。</summary>
        public void SetEnemyContent(int id,int specialKind,int bossKind,bool warning)
        {
            if(specialKind==0&&bossKind==0){RemoveRoundEnemyContent(id);return;}
            if(!IsRoundLayoutReady || roundEnemyRoot==null)return;
            if(roundEnemies.TryGetValue(id,out var old)&&(old.SpecialKind!=specialKind||old.BossKind!=bossKind))
            {
                bool positioned=roundEnemyPositions.TryGetValue(id,out var previousPosition);
                RemoveRoundEnemyContent(id);if(positioned)roundEnemyPositions[id]=previousPosition;old=null;
            }
            if(old==null)
            {
                var profile=ArcadeRoundContentRules.GetVisualProfile(specialKind,bossKind);
                var holder=RoundGroup(roundEnemyRoot.transform,profile.Name+"_"+id);
                var visual=new RoundEnemyVisual{Root=holder,SpecialKind=specialKind,BossKind=bossKind};
                foreach(var part in profile.Parts)
                {
                    if(!Enum.TryParse(part.Primitive,out PrimitiveType shape))throw new InvalidOperationException("未知专用轮廓零件："+part.Primitive);
                    RoundPart(holder.transform,part.Primitive,shape,new Vector3(part.X,part.Y,part.Z),new Vector3(part.ScaleX,part.ScaleY,part.ScaleZ),part.Color);
                }
                // 头顶图标仅表示技能预警；实际伤害范围只使用权威 Area 快照，避免假造范围圈。
                visual.Warning=RoundBox(holder.transform,"SkillWarning",0,bossKind==0?3.2f:4.7f,0,.5f,.5f,.5f,0xF6CF60FFu,45);
                float height=bossKind==0?2.7f:4.1f;
                RoundBox(holder.transform,"HealthBackground",0,height,0,1.2f,.1f,.15f,0x293B3FFFu);
                visual.HealthFill=RoundBox(holder.transform,"HealthFill",0,height+.012f,0,1.15f,.1f,.17f,0xD55B58FFu);
                roundEnemies.Add(id,visual);old=visual;
                enemyPresentation?.RemoveEnemy(id,false);
                if(roundEnemyPositions.TryGetValue(id,out var pos)){holder.transform.localPosition=new Vector3(pos.x,0,pos.y);visual.Position=pos;}
            }
            old.Warning.SetActive(warning);
        }
        /// <summary>生命条只读取权威健康值；不借用已经释放的旧敌人模型。</summary>
        public void SetRoundEnemyHealth(int id,int current,int maximum)
        {
            if(!roundEnemies.TryGetValue(id,out var visual)||visual.HealthFill==null)return;
            float ratio=maximum<=0?0:Mathf.Clamp01((float)current/maximum);
            visual.HealthFill.transform.localScale=new Vector3(1.15f*ratio,.1f,.17f);
            visual.HealthFill.transform.localPosition=new Vector3((ratio-1)*.575f,visual.HealthFill.transform.localPosition.y,0);
        }
        public void RemoveRoundEnemyContent(int id)
        {
            if(roundEnemies.TryGetValue(id,out var visual)){DestroyRoundVisual(visual.Root);roundEnemies.Remove(id);}
            roundEnemyPositions.Remove(id);
        }
        /// <summary>事件消失或敌人移除即释放独占环网格，防止长局积累历史标记资源。</summary>
        private void DestroyRoundVisual(GameObject visual)
        {
            if(visual==null)return;
            visual.SetActive(false);
            foreach(var filter in visual.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh=filter.sharedMesh;
                if(mesh!=null && roundOwnedMeshes.Remove(mesh))DestroyObject(mesh);
            }
            DestroyObject(visual);
        }
        /// <summary>根先失活，再释放所有自有网格和材质；共享 Primitive 网格不在释放列表中。</summary>
        private void ReleaseRoundContent()
        {
            roundLayoutReady=false;roundLayoutLoadError=null;
            foreach(var entry in roundBaseRendererVisibility)if(entry.Key!=null)entry.Key.enabled=entry.Value;
            roundBaseRendererVisibility.Clear();
            if(roundContentRoot!=null)roundContentRoot.SetActive(false);
            DestroyObject(roundContentRoot);roundContentRoot=roundEnvironmentRoot=roundAreaRoot=roundEnemyRoot=null;
            foreach(var mesh in roundOwnedMeshes)DestroyObject(mesh);roundOwnedMeshes.Clear();
            foreach(var material in roundOwnedMaterials)DestroyObject(material);roundOwnedMaterials.Clear();roundPalette.Clear();
            roundEnemies.Clear();roundEnemyPositions.Clear();roundAreas.Clear();roundAreaIdentities.Clear();roundLayout=null;
        }
    }
}
