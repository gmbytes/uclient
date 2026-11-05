using System;
using System.Collections.Generic;
using System.Globalization;
using Client.Framework.UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Client.Game.MainCity
{
    /// <summary>Replace this snapshot from the role/resource service when server data becomes available.</summary>
    public sealed class MainCityPlayerInfo
    {
        public string Name="初入三国";
        public int Level=1,NobleLevel;
        public long CombatPower,Gold=120,Silver=6800,Iron=12800,Wood=15600,Food=24000;
        public long Experience=20,ExperienceToNextLevel=100;
    }

    public sealed class MainCityHudPresenter:IDisposable
    {
        readonly UIBindings bindings;
        readonly GameObject menu,dialog;
        readonly Text dialogTitle,dialogBody;
        readonly List<KeyValuePair<Button,UnityAction>> listeners=new List<KeyValuePair<Button,UnityAction>>();
        MainCityPlayerInfo info;
        bool disposed;
        public MainCityHudPresenter(UIBindings source)
        {
            bindings=source??throw new ArgumentNullException(nameof(source));
            menu=Get<RectTransform>("MainUIFunctionsMenu").gameObject;dialog=Get<RectTransform>("MainUIDialog").gameObject;
            dialogTitle=Get<Text>("MainUIDialogTitle");dialogBody=Get<Text>("MainUIDialogBody");
            Listen("MainUIPlayerButton",ShowPlayer);Listen("MainUIFunctions",ToggleMenu);Listen("MainUIDialogClose",HidePanels);
            foreach(string id in new[]{"Heroes","Bag","Alliance","Territory"})
            {string caption=id=="Heroes"?"武将":id=="Bag"?"背包":id=="Alliance"?"联盟":"领地";Listen("MainUI"+id,()=>Notice(caption,"本地体验模式\n"+caption+"功能暂未开放。"));}
            HidePanels();
        }
        T Get<T>(string id)where T:Component=>(T)bindings.Resolve(id);
        void Listen(string id,UnityAction action){var button=Get<Button>(id);button.onClick.AddListener(action);listeners.Add(new KeyValuePair<Button,UnityAction>(button,action));}
        public void Apply(MainCityPlayerInfo value)
        {
            if(disposed)throw new ObjectDisposedException(nameof(MainCityHudPresenter));
            if(value==null)throw new ArgumentNullException(nameof(value));
            info=new MainCityPlayerInfo{Name=string.IsNullOrWhiteSpace(value.Name)?"主公":value.Name,Level=Math.Max(1,value.Level),NobleLevel=Math.Max(0,value.NobleLevel),
                CombatPower=Math.Max(0,value.CombatPower),Gold=Math.Max(0,value.Gold),Silver=Math.Max(0,value.Silver),Iron=Math.Max(0,value.Iron),Wood=Math.Max(0,value.Wood),Food=Math.Max(0,value.Food),
                ExperienceToNextLevel=Math.Max(1,value.ExperienceToNextLevel),Experience=Math.Min(Math.Max(0,value.Experience),Math.Max(1,value.ExperienceToNextLevel))};
            Get<Text>("MainUIName").text=info.Name;Get<Text>("MainUILevel").text="等级 "+info.Level;Get<Text>("MainUINoble").text="贵族 "+info.NobleLevel;
            Get<Text>("MainUIPower").text=Amount(info.CombatPower);
            Get<Text>("MainUIExperience").text="经验 "+Amount(info.Experience)+" / "+Amount(info.ExperienceToNextLevel);
            Get<Image>("MainUIExperienceFill").fillAmount=(float)((double)info.Experience/info.ExperienceToNextLevel);
            string[] ids={"Gold","Silver","Iron","Wood","Food"};long[] amounts={info.Gold,info.Silver,info.Iron,info.Wood,info.Food};
            for(int i=0;i<ids.Length;i++){string text=Amount(amounts[i]);Get<Text>("MainUI"+ids[i]).text=text;Get<Text>("Detail"+ids[i]+"Stock").text=text;}
        }
        static string Amount(long value)=>value.ToString("N0",CultureInfo.InvariantCulture);
        void ShowPlayer()
        {if(info!=null)Notice("主公信息",info.Name+"\n等级  "+info.Level+"     贵族  "+info.NobleLevel+"\n战力  "+Amount(info.CombatPower)+"\n经验  "+Amount(info.Experience)+" / "+Amount(info.ExperienceToNextLevel)+"\n本地体验角色");}
        void ToggleMenu(){if(disposed)return;dialog.SetActive(false);menu.SetActive(!menu.activeSelf);}
        void Notice(string title,string body){if(disposed)return;menu.SetActive(false);dialogTitle.text=title;dialogBody.text=body;dialog.SetActive(true);}
        public void HidePanels(){if(menu!=null)menu.SetActive(false);if(dialog!=null)dialog.SetActive(false);}
        public void Dispose()
        {if(disposed)return;HidePanels();foreach(var pair in listeners)if(pair.Key!=null)pair.Key.onClick.RemoveListener(pair.Value);listeners.Clear();disposed=true;}
    }
}
