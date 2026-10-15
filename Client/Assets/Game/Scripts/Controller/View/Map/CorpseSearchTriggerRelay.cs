using UnityEngine;

namespace Game
{
    [DisallowMultipleComponent]
    public sealed class CorpseSearchTriggerRelay : MonoBehaviour
    {
        private EnemyView owner;

        internal void Configure(EnemyView configuredOwner)
        {
            owner = configuredOwner;
        }

        private void OnTriggerEnter(Collider other)
        {
            owner?.HandleCorpseSearchTriggerEnter(other);
        }

        private void OnTriggerExit(Collider other)
        {
            owner?.HandleCorpseSearchTriggerExit(other);
        }
    }
}
