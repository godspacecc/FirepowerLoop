using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 敌人移动表现端口，只服务寻路、追击和攻击距离判断。
    /// </summary>
    public interface IEnemyNavigationPort
    {
        int UnitId { get; }
        long MapInstanceId { get; }
        bool IsValid { get; }
        Vector3 Position { get; }
        Quaternion Rotation { get; }
        bool IsOnNavigableSurface(float sampleRadius);
        bool TryGetProjectileOrigin(Vector3 direction, out Vector3 origin);
        bool TrySetRetreatDestination(Vector3 requestedPosition, float sampleRadius, out Vector3 destination);
        void PlayAttackAnimation();
        void SetTelegraphVisible(bool visible);
        void SetDestination(Vector3 position);
        void Stop();
    }

    /// <summary>街机仅复用敌人 Prefab 与表现生命周期；生命、移动和死亡结论仍由 ArcadeCombatSystem 权威维护。</summary>
    public interface IArcadeEnemyPresentationPort : IUtility
    {
        /// <summary>模式实例体型；默认不改共用表现，回收必须恢复原比例。</summary>
        void SetPresentationScale(int runtimeId,Vector3 scale) { }
        /// <summary>仅返回当前地图仍存活的表现根，供地图拥有的背桶附属物绑定；不公开敌人业务状态。</summary>
        bool TryGetEnemyPresentationRoot(int runtimeId,out Transform root){root=null;return false;}
        bool TrySetEnemy(int runtimeId, string configId, Vector3 position, Vector3 facing,
            int currentHealth, int maximumHealth, out string error, bool telegraph = false,
            int attackSequence = 0, bool persistentHealthBar = false);
        void RemoveEnemy(int runtimeId, bool playDeath = false);
        /// <summary>幸存者远场休眠仅归还表现实例，不通知死亡或释放预加载配置。</summary>
        void SuspendEnemy(int runtimeId) { }
        /// <summary>加载阶段每次最多预热一个闲置角色；不注册战斗实体。</summary>
        bool TryPrewarmEnemy(string configId,int desired,out bool complete,out string error){complete=true;error=null;return true;}
        /// <summary>只携带已经裁决的死亡方向与爆炸类别，旧端口保留动画降级。</summary>
        void RemoveEnemy(int runtimeId, bool playDeath, Vector3 direction, bool explosive) => RemoveEnemy(runtimeId, playDeath);
        void SetEnemyBurning(int runtimeId,float remainingSeconds) { }
        /// <summary>采样权威前摇和收招，不在表现层计算命中。</summary>
        void SetMeleeAttack(int runtimeId,float duration,float remaining,float recovery,Vector3 direction) { }
        /// <summary>只投影牛撞快照，不从模型位置反推战斗位移。</summary>
        void SetHerdImpact(int runtimeId,FirepowerLoop.Arcade.ArcadeHerdImpactState state,Vector3 direction) { }
        /// <summary>同一地图中采样海中靠近/抓边/登台，方向由地图局部轴转换。</summary>
        void SetDockSeaEntry(int runtimeId,FirepowerLoop.Arcade.ArcadeDockSeaEntryState state,Vector3 forward,Vector3 up) { }
        /// <summary>幸存者独立爬出状态，仍由现有地图敌人池管理实例。</summary>
        void SetGroundEntry(int runtimeId,SurvivorGroundEntryState state,Vector3 forward,Vector3 up) { }
        /// <summary>联机断线等待时冻结牛撞尸体的表现时钟。</summary>
        void SetHerdActivityPaused(bool paused) { }
        /// <summary>专属轮廓复用已预载敌人中的纯粒子模板，克隆由请求方地图根持有。</summary>
        bool TryCreateFlameFeedback(Transform parent,out EnemyFlameFeedback feedback){feedback=null;return false;}
        /// <summary>已裁决死亡的外部正式演员交给现有尸体预算；成功后所有权归端口，拒绝时调用方仍负责销毁。</summary>
        bool TryPlayExternalEnemyDeath(int runtimeId,EnemyView view,Vector3 direction,bool explosive,bool flameKill,int impactKind)=>false;
        void RemoveEnemy(int runtimeId,bool playDeath,Vector3 direction,bool explosive,bool flameKill)=>RemoveEnemy(runtimeId,playDeath,direction,explosive);
        /// <summary>表现分层由最终伤害确定，旧实现仍可回退到既有死亡入口。</summary>
        void RemoveEnemy(int runtimeId,bool playDeath,Vector3 direction,bool explosive,bool flameKill,int impactKind)=>RemoveEnemy(runtimeId,playDeath,direction,explosive,flameKill);
        void ClearArcadeEnemies();
    }
}
