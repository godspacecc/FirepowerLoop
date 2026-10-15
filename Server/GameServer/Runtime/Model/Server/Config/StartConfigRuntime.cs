using System.Net;

namespace ET
{
    [EnableClass]
    public sealed partial class StartProcessConfig
    {
        private IPEndPoint ipEndPoint;

        public int MachineId => this.MachineKey_Ref.Id;

        public string InnerIP => this.MachineKey_Ref.InnerIP;

        public string OuterIP => this.MachineKey_Ref.OuterIP;

        public IPEndPoint IPEndPoint
        {
            get
            {
                return this.ipEndPoint ??= NetworkHelper.ToIPEndPoint(this.InnerIP, this.Port);
            }
        }

        public StartMachineConfig StartMachineConfig => this.MachineKey_Ref;
    }

    [EnableClass]
    public sealed partial class StartSceneConfig
    {
        private IPEndPoint innerIPPort;
        private IPEndPoint outerIPPort;

        public int Process => this.ProcessKey_Ref.Id;

        public int Zone => this.ZoneKey_Ref.Id;

        public ActorId ActorId => new(this.Process, this.Id, 1);

        public SceneType Type => EnumHelper.FromString<SceneType>(this.SceneType);

        public StartProcessConfig StartProcessConfig => this.ProcessKey_Ref;

        public StartZoneConfig StartZoneConfig => this.ZoneKey_Ref;

        public IPEndPoint InnerIPPort
        {
            get
            {
                return this.innerIPPort ??= NetworkHelper.ToIPEndPoint($"{this.StartProcessConfig.InnerIP}:{this.Port}");
            }
        }

        public IPEndPoint OuterIPPort
        {
            get
            {
                return this.outerIPPort ??= NetworkHelper.ToIPEndPoint($"{this.StartProcessConfig.OuterIP}:{this.Port}");
            }
        }
    }
}
