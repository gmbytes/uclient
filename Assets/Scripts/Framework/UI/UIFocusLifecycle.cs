using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Client.Framework.UI
{
    /// <summary>Preserves one visible window's editing state across application pause/resume.</summary>
    [DisallowMultipleComponent]
    public sealed class UIFocusLifecycle : MonoBehaviour
    {
        struct EditingState
        {
            public InputField Field;
            public int Caret, Anchor, Focus;
            public EditingState(InputField field)
            { Field=field; Caret=field.caretPosition; Anchor=field.selectionAnchorPosition; Focus=field.selectionFocusPosition; }
        }
        EditingState recent, suspended;
        bool paused, restoring;
        int generation;

        bool Eligible(InputField field)
        {
            if(field==null || !isActiveAndEnabled || !field.isActiveAndEnabled || !field.IsInteractable() || !field.transform.IsChildOf(transform))return false;
            var bridge=GetComponent<UIViewBridge>();
            return bridge==null || bridge.Visible;
        }
        static bool Selected(InputField field)
        {
            var current=EventSystem.current!=null?EventSystem.current.currentSelectedGameObject:null;
            return current==null || current==field.gameObject || current.transform.IsChildOf(field.transform);
        }
        void LateUpdate()
        {
            if(paused || restoring)return;
            var selected=EventSystem.current!=null?EventSystem.current.currentSelectedGameObject:null;
            var field=selected!=null?selected.GetComponentInParent<InputField>():null;
            recent=Eligible(field) && field.isFocused?new EditingState(field):default;
        }
        void OnApplicationPause(bool value)
        {
            if(value)
            {
                generation++;restoring=false;paused=true;
                // 平台可能先停用 InputField 再发送 pause；保留上一帧仍在编辑的同一控件。
                suspended=Eligible(recent.Field) && Selected(recent.Field)?recent:default;
            }
            else if(paused)
            {
                paused=false;
                var captured=suspended;suspended=default;
                if(Eligible(captured.Field) && Selected(captured.Field))StartCoroutine(Restore(captured,++generation));
            }
        }
        IEnumerator Restore(EditingState captured,int owner)
        {
            restoring=true;
            // Android 旧键盘可在恢复后的第二帧撤销焦点；短暂观察并重激活，而非假设首次激活已稳定。
            int stable=0;
            for(int frame=0;frame<6;frame++)
            {
                yield return null;
                if(owner!=generation || !Eligible(captured.Field) || !Selected(captured.Field))break;
                if(!captured.Field.isFocused)
                {
                    captured.Field.Select();captured.Field.ActivateInputField();stable=0;continue;
                }
                if(frame<2)continue;
                int length=captured.Field.text?.Length??0;
                captured.Field.caretPosition=Mathf.Clamp(captured.Caret,0,length);
                captured.Field.selectionAnchorPosition=Mathf.Clamp(captured.Anchor,0,length);
                captured.Field.selectionFocusPosition=Mathf.Clamp(captured.Focus,0,length);
                recent=new EditingState(captured.Field);
                if(++stable==2)break;
            }
            restoring=false;
        }
        internal void Discard()
        {
            generation++;StopAllCoroutines();paused=false;restoring=false;recent=default;suspended=default;
        }
        void OnDisable()=>Discard();
    }
}
