using System;
using TMPro;
using UnityEngine;
namespace Game
{
    /// <summary>旧 NPC 资源只保留原提示引用，不发送交互业务请求。</summary>
    [RequireComponent(typeof(Collider))]
    public sealed class NpcView : PlayerInteractableView
    {
        [SerializeField] private GameObject promptRoot;
        [SerializeField] private TMP_Text promptText;
        public override void Interact() { }
        protected override void Awake() { base.Awake(); if (promptRoot != null) promptRoot.SetActive(false); }
    }
}
