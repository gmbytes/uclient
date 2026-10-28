using UnityEngine;
using UnityEngine.UI;

namespace Client.Bootstrap
{
    public sealed class UIUpdate : MonoBehaviour
    {
        [SerializeField] private Text m_Status;
        public void SetStatus(string message, bool isError)
        {
            if (m_Status == null) return;
            m_Status.text = message;
            m_Status.color = isError ? Color.red : Color.white;
        }
    }
}
