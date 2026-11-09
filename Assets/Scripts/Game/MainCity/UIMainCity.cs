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
        Button outskirtsButton;
        CityViewport.ViewState savedView;
        public event Action OutskirtsRequested;
        MainCityPlayerInfo playerInfo=new MainCityPlayerInfo();
        public void Bind(IViewHandle handle,Action onReturn)
        {
            view=handle;
            var bindings=handle.Root.GetComponent<UIBindings>();
            map=(CityViewport)bindings.Resolve("Map");
            map.BuildingSelected+=Selected;
            details=new BuildingDetailPresenter(bindings);
            hud=new MainCityHudPresenter(bindings);hud.Apply(playerInfo);
            outskirtsButton = handle.Root.transform.Find("UINode/SafeArea/Static/OutskirtsButton")?.GetComponent<Button>();
            if (outskirtsButton != null) outskirtsButton.onClick.AddListener(OpenOutskirts);
        }

        void OpenOutskirts() => OutskirtsRequested?.Invoke();

        public void SuspendForOutskirts()
        {
            savedView = map.CaptureView();
            view.Root.transform.Find("UINode").gameObject.SetActive(false);
        }

        public void ResumeFromOutskirts()
        {
            if (view == null) return;
            view.Root.transform.Find("UINode").gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            map.ApplyView(savedView);
            SetOutskirtsRetry(false);
        }

        public void SetOutskirtsRetry(bool retry)
        {
            if (outskirtsButton != null)
                outskirtsButton.GetComponentInChildren<Text>().text = retry ? "城外 · 重试" : "城外";
        }
        public void Unbind()
        {
            if (outskirtsButton != null) outskirtsButton.onClick.RemoveListener(OpenOutskirts);
            outskirtsButton = null;
            OutskirtsRequested = null;
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
