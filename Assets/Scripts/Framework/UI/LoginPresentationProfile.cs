using UnityEngine;

namespace Client.Framework.UI
{
    public enum LoginPresentationMode { LocalSimulation, TokDevelopment }

    // Native serializable presentation data; credentials and transport settings stay in N3.
    public sealed class LoginPresentationProfile : ScriptableObject
    {
        public LoginPresentationMode Mode;
        public string Label;
        public string Version = "Framework demo v1";
    }
}
