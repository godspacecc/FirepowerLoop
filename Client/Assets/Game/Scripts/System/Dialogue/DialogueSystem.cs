using QFramework;

namespace Game
{
    /// <summary>对话快照变化；null 表示会话结束，不携带 Unity 对象。</summary>
    public readonly struct DialogueChangedEvent
    {
        public DialogueChangedEvent(DialogueSnapshot snapshot) { Snapshot = snapshot; }
        public DialogueSnapshot Snapshot { get; }
    }

    /// <summary>对话确认后的功能意图，NPC Controller 再验证活动身份后调用既有入口。</summary>
    public readonly struct DialogueNpcFunctionRequestedEvent
    {
        public DialogueNpcFunctionRequestedEvent(int npcUnitId, long mapInstanceId) { NpcUnitId = npcUnitId; MapInstanceId = mapInstanceId; }
        public int NpcUnitId { get; }
        public long MapInstanceId { get; }
    }

    /// <summary>拥有临时对话会话和选项裁决；不保存剧情、执行奖励、创建 NPC 或直接操作 UI。</summary>
    public sealed class DialogueSystem : AbstractSystem
    {
        private readonly DialogueSession session = new DialogueSession();
        private bool released;
        public DialogueSnapshot Current => session.Current;

        protected override void OnInit()
        {
            released = false;
            this.RegisterEvent<MapLoadingStartedEvent>(_ => CloseCurrent());
            this.RegisterEvent<MapCurrentReleasedEvent>(_ => CloseCurrent());
            this.RegisterEvent<UnitRemovedEvent>(e => { if (IsParticipant(e.Snapshot.UnitId)) CloseCurrent(); });
            this.RegisterEvent<UnitChangedEvent>(e => { if (IsParticipant(e.Current.UnitId) && !e.Current.IsActive) CloseCurrent(); });
            this.RegisterEvent<CombatantDiedEvent>(e => { if (IsParticipant(e.UnitId)) CloseCurrent(); });
            this.RegisterEvent<AppStateChangedEvent>(_ =>
            {
                var state = this.GetModel<AppStateModel>().CurrentState.Value;
                // 暂停保留当前节点；加载、退出等状态继续结束会话和临时镜头。
                if (state != AppState.Playing && state != AppState.Paused) CloseCurrent();
            });
            LifeCycle.RegisterLateUpdate(ValidateCurrent);
        }

        protected override void OnDeinit()
        {
            released = true;
            LifeCycle.UnregisterLateUpdate(ValidateCurrent);
            CloseCurrent();
        }

        /// <summary>NPC 已在范围内交互后调用；准备相机失败会撤销会话，不改变旧 NPC 功能。</summary>
        public bool TryBegin(DialogueDefinition definition, int npcUnitId, long mapInstanceId, string nameKey, out string error)
        {
            error = null;
            if (released || Current != null || UIHelper.HasUIOpen || !CanUseNpc(npcUnitId, mapInstanceId, out var npc) || npc.ConfigId != definition?.NpcId)
            { error = "当前无法开始交谈"; return false; }
            var localization = this.GetUtility<LocalizationUtility>();
            // 入口配置只用于既有 NPC；当前显示的文本缺失必须明确失败，不能把 Key 当作台词。
            if (localization != null)
            {
                localization.InitializeConfiguredTables();
                var keys = new System.Collections.Generic.HashSet<string>(localization.GetKeys(LocalizationLanguages.Default.Code));
                foreach (var key in definition.TextKeys)
                    if (!keys.Contains(key)) { error = "对话台词本地化缺失：" + key; return false; }
            }
            if (!session.TryBegin(definition, npcUnitId, mapInstanceId, nameKey)) { error = "对话入口无效"; return false; }
            var snapshot = Current;
            if (this.GetSystem<PlayerCameraSystem>()?.TryBeginDialogueCamera(snapshot.SessionId, npcUnitId, mapInstanceId) != true)
            {
                session.Close(snapshot.SessionId); error = "交谈镜头不可用"; return false;
            }
            this.SendEvent(new DialogueChangedEvent(Current));
            return true;
        }

        /// <summary>每次选择重验地图与玩家；提交后旧 Revision 立即失效，业务入口只发出一次。</summary>
        public bool Choose(long sessionId, long revision, string optionId)
        {
            var previous = Current;
            // 暂停时拒绝旧按钮回调，不把无法操作误判为 NPC 失效而关闭会话。
            if (released || previous == null || this.GetModel<AppStateModel>().CurrentState.Value != AppState.Playing) return false;
            if (!CanUseNpc(previous.NpcUnitId, previous.MapInstanceId, out _)) { Close(previous.SessionId); return false; }
            if (!session.TryChoose(sessionId, revision, optionId, out var action)) return false;
            if (Current == null) this.GetSystem<PlayerCameraSystem>()?.EndDialogueCamera(previous.SessionId);
            this.SendEvent(new DialogueChangedEvent(Current));
            if (action == global::DialogueOptionAction.OpenNpcFunction)
                this.SendEvent(new DialogueNpcFunctionRequestedEvent(previous.NpcUnitId, previous.MapInstanceId));
            return true;
        }

        public void Close(long sessionId)
        {
            if (!session.Close(sessionId)) return;
            this.GetSystem<PlayerCameraSystem>()?.EndDialogueCamera(sessionId);
            this.SendEvent(new DialogueChangedEvent(null));
        }
        private void CloseCurrent() { if (Current != null) Close(Current.SessionId); }
        private bool IsParticipant(int id) => Current != null && (id == Current.NpcUnitId || id == PlayerModel.LocalPlayerUnitId);
        private void ValidateCurrent()
        {
            if (!released && Current != null && !CanUseNpc(Current.NpcUnitId, Current.MapInstanceId, out _, allowPaused: true)) CloseCurrent();
        }

        /// <summary>暂停只允许校验既有会话参与者；开始和选择仍要求游戏中状态。</summary>
        private bool CanUseNpc(int id, long mapInstanceId, out UnitSnapshot npc, bool allowPaused = false)
        {
            npc = default;
            var units = this.GetSystem<UnitSystem>();
            var state = this.GetModel<AppStateModel>().CurrentState.Value;
            return (state == AppState.Playing || allowPaused && state == AppState.Paused) &&
                this.GetModel<MapSceneModel>().CurrentMode != MapPreparationMode.OnlinePlayersOnly &&
                this.GetModel<CombatModel>().PlayerHP.Value > 0 && units != null &&
                units.TryGet(id, out npc) && npc.IsActive && npc.UnitType == UnitType.Npc && npc.MapInstanceId == mapInstanceId &&
                units.TryGet(PlayerModel.LocalPlayerUnitId, out var player) && player.IsActive && player.MapInstanceId == mapInstanceId;
        }
    }

}
