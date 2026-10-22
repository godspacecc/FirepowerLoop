using System;
using System.Linq;
using UnityEngine;
using FirepowerLoop.Arcade;

namespace Game
{
    /// <summary>牛纯表现，位置与局部动作只读取地图快照，不计算伤害或计时。</summary>
    public sealed class ArcadeHerdVisual:MonoBehaviour
    {
        [SerializeField] private Transform bodyPose,headPose,tailPose;
        [SerializeField] private Transform[] hips=Array.Empty<Transform>(),knees=Array.Empty<Transform>();
        /// <summary>仅缓存当前租用身份；不会从View反写地图或牛的权威状态。</summary>
        private long boundMap,boundId;
        public void Bind(long map,long id)
        {
            boundMap=boundId=0;
            if(map<=0||id<=0||!Validate(out _)){gameObject.SetActive(false);return;}
            boundMap=map;boundId=id;Pose(false,0);gameObject.SetActive(true);
        }
        public bool Project(ArcadeHerdSnapshot snapshot)
        {
            if(boundMap<=0||snapshot.MapInstanceId!=boundMap||snapshot.Id!=boundId||
                !Finite(snapshot.X)||!Finite(snapshot.Z)||!Finite(snapshot.DirectionX)||!Finite(snapshot.DirectionZ)||
                double.IsNaN(snapshot.PhaseSeconds)||double.IsInfinity(snapshot.PhaseSeconds)||snapshot.PhaseSeconds<0)return false;
            if(snapshot.Phase==ArcadeHerdPhase.Expired){Release();return true;}
            if(snapshot.Phase!=ArcadeHerdPhase.Windup&&snapshot.Phase!=ArcadeHerdPhase.Charging)return false;
            var direction=new Vector3(snapshot.DirectionX,0,snapshot.DirectionZ);if(direction.sqrMagnitude<.000001f)return false;
            transform.localPosition=new Vector3(snapshot.X,0,snapshot.Z);transform.localRotation=Quaternion.LookRotation(direction);
            Pose(snapshot.Phase==ArcadeHerdPhase.Charging,(float)(snapshot.PhaseSeconds%1000));return true;
        }
        /// <summary>与已确认候选同源的局部采样；同一快照重复投影保持完全相同姿态。</summary>
        private void Pose(bool charging,float seconds)
        {
            bodyPose.localPosition=new Vector3(0,charging?Mathf.Sin(seconds*18)*.025f:0,0);
            headPose.localRotation=Quaternion.Euler(charging?7:Mathf.Sin(seconds*4)*3+12,0,0);
            for(int i=0;i<hips.Length;i++)
            {
                bool diagonal=(hips[i].localPosition.x>0)==(hips[i].localPosition.z>0);float phase=seconds*15+(diagonal?0:Mathf.PI);
                hips[i].localRotation=Quaternion.Euler(charging?Mathf.Sin(phase)*25:0,0,0);
                knees[i].localRotation=Quaternion.Euler(charging?Mathf.Max(0,Mathf.Sin(phase+.6f))*24:0,0,0);
            }
            tailPose.localRotation=Quaternion.Euler(0,0,Mathf.Sin(seconds*5)*8);
        }
        public bool Validate(out string error)
        {
            bool valid=bodyPose!=null&&headPose!=null&&tailPose!=null&&hips!=null&&knees!=null&&hips.Length==4&&knees.Length==4&&hips.All(t=>t!=null)&&knees.All(t=>t!=null);
            error=valid?null:"牛模型缺少显式身体、头尾或四腿引用";return valid;
        }
        public void Release(){boundMap=boundId=0;gameObject.SetActive(false);}
        private void OnDisable(){boundMap=boundId=0;}
        private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
    }
}
