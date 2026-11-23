using UnityEngine;

namespace Client.Framework.Presentation
{
    [DisallowMultipleComponent]
    public sealed class OutskirtsResourceIdentity : MonoBehaviour
    {
        [SerializeField] private string resourceKey;
        public string ResourceKey => resourceKey;
        public void Configure(string value) => resourceKey = value;
    }
}
