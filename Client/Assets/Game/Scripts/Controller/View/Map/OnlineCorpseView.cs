using QFramework;
using UnityEngine;

namespace Game
{
    public sealed class OnlineCorpseView : MonoBehaviour, IPlayerInteractable, IPlayerSearchable
    {
        public const float SearchRadius = 1.5f;
        private long corpseId;
        private long mapInstanceId;
        private SphereCollider searchTrigger;
        private PlayerInteractionSystem interaction;
        private IPlayerRuntimePortProvider playerPorts;

        public string DisplayName => "[E] 搜索";
        public bool CanInteract => false;
        public bool CanSearch => corpseId > 0 && Game.Interface.GetSystem<OnlineWorldInventorySystem>() != null;

        public void Configure(long id, long instanceId)
        {
            corpseId = id;
            mapInstanceId = instanceId;
            interaction = Game.Interface.GetSystem<PlayerInteractionSystem>();
            playerPorts = Game.Interface.GetUtility<IPlayerRuntimePortProvider>();
            searchTrigger = gameObject.AddComponent<SphereCollider>();
            searchTrigger.isTrigger = true;
            searchTrigger.radius = SearchRadius;
        }

        public void Interact() { }
        public void Search()
        {
            if (CanSearch) Game.Interface.GetSystem<OnlineWorldInventorySystem>().TryBeginCorpseSearch(corpseId, out _);
        }

        public void SetFocused(bool focused) { }

        public void ClearOwnership()
        {
            interaction?.Blur(this);
            Game.Interface.GetSystem<OnlineWorldInventorySystem>()?.CancelSearch();
            if (Game.Interface.GetSystem<OnlineWorldInventorySystem>()?.TryGetOpenCorpse(out var opened) == true &&
                opened.Corpse.CorpseId == corpseId)
            {
                Game.Interface.GetSystem<OnlineWorldInventorySystem>().CloseOpenCorpse();
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (IsLocalPlayer(other)) interaction?.Focus(this);
        }

        private void OnTriggerExit(Collider other)
        {
            if (IsLocalPlayer(other)) interaction?.Blur(this);
        }

        private bool IsLocalPlayer(Collider other) => mapInstanceId > 0 && other != null && playerPorts != null &&
            playerPorts.IsPlayerCollider(PlayerModel.LocalPlayerUnitId, mapInstanceId, other);

        private void OnDisable() => ClearOwnership();
        private void OnDestroy() => ClearOwnership();
    }
}
