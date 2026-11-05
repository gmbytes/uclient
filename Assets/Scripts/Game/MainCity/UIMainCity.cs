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
        BuildingDetailPresenter details;
        MainCityHudPresenter hud;
        MainCityPlayerInfo playerInfo=new MainCityPlayerInfo();
        public void Bind(IViewHandle handle,Action onReturn)
        {
            view=handle;
            var bindings=handle.Root.GetComponent<UIBindings>();
            map=(CityViewport)bindings.Resolve("Map");
            map.BuildingSelected+=Selected;
            details=new BuildingDetailPresenter(bindings);
            hud=new MainCityHudPresenter(bindings);hud.Apply(playerInfo);
        }
        public void Unbind()
        {
            hud?.Dispose();hud=null;details?.Dispose();details=null;
            if(map!=null)map.BuildingSelected-=Selected;
            view=null;map=null;
        }
        void Selected(int id,string name)
        {
            if(view==null)return;
            hud.HidePanels();details.Show(id,name);
        }
        public void SetPlayerInfo(MainCityPlayerInfo value)
        {playerInfo=value??throw new ArgumentNullException(nameof(value));hud?.Apply(playerInfo);}
    }
}
