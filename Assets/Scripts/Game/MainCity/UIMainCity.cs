using System;
using Client.Contracts;
using Client.Framework.Presentation;
using Client.Framework.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Client.Game.MainCity
{
    public sealed partial class UIMainCity
    {
        IViewHandle view;
        CityViewport map;
        Text title,description;
        GameObject details;
        Button back,close,focus;
        Action returnToLogin;
        public void Bind(IViewHandle handle,Action onReturn)
        {
            view=handle;returnToLogin=onReturn;
            var bindings=handle.Root.GetComponent<UIBindings>();
            map=(CityViewport)bindings.Resolve("Map");
            title=(Text)bindings.Resolve("BuildingTitle");description=(Text)bindings.Resolve("BuildingDescription");
            details=((RectTransform)bindings.Resolve("BuildingDetails")).gameObject;
            back=(Button)bindings.Resolve("ReturnButton");close=(Button)bindings.Resolve("CloseDetails");focus=(Button)bindings.Resolve("FocusBuilding");
            back.onClick.AddListener(Return);close.onClick.AddListener(Hide);focus.onClick.AddListener(Focus);
            map.BuildingSelected+=Selected;details.SetActive(false);
        }
        public void Unbind()
        {
            if(map!=null)map.BuildingSelected-=Selected;
            if(back!=null)back.onClick.RemoveListener(Return);
            if(close!=null)close.onClick.RemoveListener(Hide);
            if(focus!=null)focus.onClick.RemoveListener(Focus);
            view=null;map=null;back=close=focus=null;returnToLogin=null;
        }
        void Selected(int id,string name)
        {
            if(view==null)return;
            title.text=name+"  ·  1级";
            description.text="点击地图中的建筑可查看与定位。\n当前为本地主城展示，建设业务尚未接入。";
            details.SetActive(true);
        }
        void Hide(){if(details!=null)details.SetActive(false);}
        void Focus(){if(map!=null)map.FocusBuilding(map.SelectedBuildingId);}
        void Return()=>returnToLogin?.Invoke();
    }
}
