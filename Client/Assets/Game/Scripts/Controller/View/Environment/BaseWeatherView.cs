using System.Collections.Generic;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>基地雨、雨声和湿地的地图所有者；只消费天气快照，不推进业务时间，不修改共享材质或玩家数值。</summary>
    [DisallowMultipleComponent]
    public sealed class BaseWeatherView : MonoBehaviour
    {
        [SerializeField] private Renderer[] wetSurfaces = new Renderer[0];
        [SerializeField] private Collider[] rainBlockers = new Collider[0];
        // 仅用于降雨表现的无碰撞屋顶；采用保守包围范围，不新增或改变玩家物理碰撞。
        [SerializeField] private Renderer[] rainRoofs = new Renderer[0];
        [SerializeField] private Material rainMaterial;
        [SerializeField] private Material splashMaterial;
        [SerializeField] private AudioClip rainClip;
        // 范围单位米；雨粒子只在当前玩家附近生成，世界空间模拟避免移动时已有雨线瞬移。
        [SerializeField, Min(4)] private float radius = 18;
        private readonly List<Surface> surfaces = new List<Surface>();
        private readonly System.Random random = new System.Random(8037);
        private IUnRegister unregister;
        private ParticleSystem rain;
        private ParticleSystem splashes;
        private AudioSource audioSource;
        private bool initialized;
        private float emissionRemainder;
        private BaseWeatherSnapshot snapshot;
        private sealed class Surface
        {
            public Renderer Renderer;
            public Material[] Originals;
            public Material[] Copies;
            public Color[] Colors;
            public float[] Smoothness;
        }
        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            Initialize();
            unregister = Game.Interface.RegisterEvent<BaseWeatherChangedEvent>(evt => Apply(evt.Snapshot));
            Apply(Game.Interface.GetSystem<BaseWeatherSystem>().GetSnapshot());
        }
        private void OnDisable() => Release();
        private void OnDestroy() => Release();
        /// <summary>地图启用时准备独立材质与粒子；重复调用不再次分配。</summary>
        internal void Initialize()
        {
            if (initialized) return;
            initialized = true;
            foreach (var renderer in wetSurfaces)
            {
                if (!renderer) continue;
                var original = renderer.sharedMaterials;
                var surface = new Surface { Renderer=renderer, Originals=original, Copies=new Material[original.Length], Colors=new Color[original.Length], Smoothness=new float[original.Length] };
                for (var i=0;i<original.Length;i++)
                {
                    if (!original[i]) continue;
                    surface.Copies[i]=new Material(original[i]);
                    surface.Colors[i]=original[i].GetColor("_BaseColor");
                    surface.Smoothness[i]=original[i].GetFloat("_Smoothness");
                }
                renderer.sharedMaterials=surface.Copies; surfaces.Add(surface);
            }
            if (rainMaterial) rain = CreateParticles("BaseRain",rainMaterial,false);
            if (splashMaterial) splashes = CreateParticles("BaseRainSplash",splashMaterial,true);
            if (rainClip)
            {
                var audioObject=new GameObject("BaseRainAudio");audioObject.transform.SetParent(transform,false);
                audioSource=audioObject.AddComponent<AudioSource>();audioSource.clip=rainClip;audioSource.loop=true;
                audioSource.spatialBlend=0;audioSource.playOnAwake=false;audioSource.volume=0;
            }
        }
        private ParticleSystem CreateParticles(string name,Material material,bool splash)
        {
            var go=new GameObject(name);go.transform.SetParent(transform,false);
            var particles=go.AddComponent<ParticleSystem>();particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=particles.main;main.playOnAwake=false;main.loop=true;main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.maxParticles=splash?400:3000;main.startSpeed=0;main.startSize=splash?.20f:.085f;
            main.startColor=splash?new Color(.7f,.8f,.9f,.35f):new Color(.72f,.82f,.92f,.4f);
            var emission=particles.emission;emission.enabled=false;var shape=particles.shape;shape.enabled=false;
            var renderer=particles.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;
            renderer.renderMode=splash?ParticleSystemRenderMode.HorizontalBillboard:ParticleSystemRenderMode.Stretch;
            renderer.lengthScale=3;renderer.velocityScale=.025f;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            particles.Play();return particles;
        }
        internal void Apply(BaseWeatherSnapshot value)
        {
            snapshot=value;
            foreach (var surface in surfaces)
                for (var i=0;i<surface.Copies.Length;i++) if (surface.Copies[i])
                {
                    surface.Copies[i].SetColor("_BaseColor",surface.Colors[i]*Mathf.Lerp(1f,.58f,value.Wetness));
                    surface.Copies[i].SetFloat("_Smoothness",Mathf.Lerp(surface.Smoothness[i],.58f,value.Wetness));
                }
            if (!Application.isPlaying) return;
            var paused=Game.Interface.GetModel<AppStateModel>().CurrentState.Value!=AppState.Playing;
            var anchor=transform.position;
            var playerId=Game.Interface.GetModel<PlayerModel>().PlayerUnitId;
            if(Game.Interface.GetSystem<UnitSystem>().TryGet(playerId,out var unit)&&unit.IsActive) anchor=unit.Position;
            StepVisual(Time.deltaTime,anchor,paused || !value.IsActive);
        }
        /// <summary>由天气生命周期事件驱动表现；编辑器预览可显式推进，绝不改变天气状态。</summary>
        internal void StepVisual(float seconds,Vector3 anchor,bool paused)
        {
            if (rain) { if(paused) rain.Pause();else if(!rain.isPlaying) rain.Play(); }
            if (splashes) { if(paused) splashes.Pause();else if(!splashes.isPlaying) splashes.Play(); }
            if (audioSource)
            {
                audioSource.volume=snapshot.RainAmount*.45f;
                if(paused || snapshot.RainAmount<=0) audioSource.Pause();
                else if(!audioSource.isPlaying) audioSource.Play();
            }
            if(paused || !rain || seconds<=0 || float.IsNaN(seconds)||float.IsInfinity(seconds)) return;
            emissionRemainder+=Mathf.Min(seconds,.1f)*900*snapshot.RainAmount;
            var count=Mathf.FloorToInt(emissionRemainder);emissionRemainder-=count;
            for(var i=0;i<count;i++)
            {
                var x=anchor.x+((float)random.NextDouble()*2-1)*radius;
                var z=anchor.z+((float)random.NextDouble()*2-1)*radius;
                var top=new Vector3(x,anchor.y+12,z);var ray=new Ray(top,Vector3.down);
                var nearest=28f;var hitGround=false;var point=top+Vector3.down*nearest;
                foreach(var collider in rainBlockers)
                {
                    if(!collider || !collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger) continue;
                    var bounds=collider.bounds;if(x<bounds.min.x||x>bounds.max.x||z<bounds.min.z||z>bounds.max.z)continue;
                    if(collider.Raycast(ray,out var hit,nearest))
                    {
                        nearest=hit.distance;point=hit.point;hitGround=false;
                        foreach(var surface in surfaces)if(surface.Renderer && collider.gameObject==surface.Renderer.gameObject){hitGround=true;break;}
                    }
                }
                foreach(var roof in rainRoofs)
                {
                    if(!roof || !roof.enabled || !roof.gameObject.activeInHierarchy)continue;
                    var bounds=roof.bounds;
                    if(x<bounds.min.x||x>bounds.max.x||z<bounds.min.z||z>bounds.max.z)continue;
                    var distance=Mathf.Max(0,top.y-bounds.max.y);
                    if(distance<nearest){nearest=distance;hitGround=false;}
                }
                if(nearest<=0)continue;
                rain.Emit(new ParticleSystem.EmitParams{position=top,velocity=Vector3.down*19,startLifetime=nearest/19},1);
                if(splashes && hitGround && random.NextDouble()<.08)
                    splashes.Emit(new ParticleSystem.EmitParams{position=point+Vector3.up*.025f,startLifetime=.18f,startSize=.16f+(float)random.NextDouble()*.12f},1);
            }
            // 预览场景中引擎不自动模拟粒子，显式推进只用于静态截图。
            if(!Application.isPlaying){rain.Simulate(seconds,false,false);if(splashes)splashes.Simulate(seconds,false,false);}
        }
        /// <summary>地图禁用时恢复原材质并销毁唯一所有的临时资源；重复释放无副作用。</summary>
        internal void Release()
        {
            unregister?.UnRegister();unregister=null;
            foreach(var surface in surfaces)
            {
                if(surface.Renderer && surface.Copies.Length>0 && surface.Renderer.sharedMaterial==surface.Copies[0]) surface.Renderer.sharedMaterials=surface.Originals;
                foreach(var material in surface.Copies)if(material) DestroyOwned(material);
            }
            surfaces.Clear();if(rain)DestroyOwned(rain.gameObject);if(splashes)DestroyOwned(splashes.gameObject);if(audioSource)DestroyOwned(audioSource.gameObject);
            rain=null;splashes=null;audioSource=null;initialized=false;emissionRemainder=0;
        }
        private static void DestroyOwned(Object value) { if(Application.isPlaying) Destroy(value);else DestroyImmediate(value); }
    }
}
