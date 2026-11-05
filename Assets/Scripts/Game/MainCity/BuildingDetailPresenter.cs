using System;
using Client.Framework.Presentation;
using Client.Framework.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Client.Game.MainCity
{
    /// <summary>Hot-update data and actions; all building motion/lifetime is owned by the common native transition.</summary>
    public sealed class BuildingDetailPresenter:IDisposable
    {
        readonly BuildingDetailTransition transition;
        readonly GameObject dialog,mine,camp;
        readonly BuildingPreviewViewport preview;
        readonly Button close,alliance,territory,upgrade,dismiss;
        readonly Text name,level,description,benefits,dialogTitle,dialogBody;
        string buildingName;
        bool disposed;
        public bool IsOpen=>transition.IsOpen;

        public BuildingDetailPresenter(UIBindings bindings)
        {
            transition=Get<BuildingDetailTransition>("DetailTransition");
            preview=Get<BuildingPreviewViewport>("BuildingPreview");
            mine=Get<RectTransform>("MineCounter").gameObject;camp=Get<RectTransform>("CampCounter").gameObject;
            dialog=Get<RectTransform>("CityDetailDialog").gameObject;
            name=Get<Text>("DetailName");level=Get<Text>("DetailLevel");description=Get<Text>("DetailDescription");benefits=Get<Text>("DetailBenefits");
            close=Get<Button>("CloseCityDetails");alliance=Get<Button>("AllianceButton");territory=Get<Button>("TerritoryButton");
            upgrade=Get<Button>("UpgradeCityHall");dismiss=Get<Button>("DismissCityDialog");
            dialogTitle=Get<Text>("CityDialogTitle");dialogBody=Get<Text>("CityDialogBody");
            close.onClick.AddListener(Hide);alliance.onClick.AddListener(Alliance);territory.onClick.AddListener(Territory);
            upgrade.onClick.AddListener(Upgrade);dismiss.onClick.AddListener(Dismiss);
            transition.ResetImmediately();
            T Get<T>(string id)where T:Component=>(T)bindings.Resolve(id);
        }
        public void Show(int id,string selectedName)
        {
            if(disposed)throw new ObjectDisposedException(nameof(BuildingDetailPresenter));
            if(transition.IsOpen||transition.IsTransitioning)return;
            buildingName=string.IsNullOrWhiteSpace(selectedName)?"建筑 "+id:selectedName;
            var data=BuildingDetailDefinition.For(id);
            name.text=buildingName;level.text="1级  "+(id==1?"城池中枢":"建设详情");
            description.text=data.Description;benefits.text=data.UpgradeEffect;
            bool cityHall=id==1;
            preview.Show(id);
            mine.SetActive(cityHall);camp.SetActive(cityHall);
            alliance.gameObject.SetActive(cityHall);territory.gameObject.SetActive(cityHall);
            dialog.SetActive(false);transition.Open(id);
        }
        public void Hide(){if(dialog!=null)dialog.SetActive(false);transition.Close();}
        void Alliance()=>Notice("联盟","本地体验模式\n联盟服务暂未开放。\n联防、捐献与成员管理将在开放后启用。");
        void Territory()=>Notice("领地","本地演示数据\n野矿  0 / 2     营寨  0 / 1\n领地占领与管理暂未开放。");
        void Upgrade()=>Notice(buildingName+"升级","本地预览\n升级效果与消耗当前为演示数值。\n升级暂未开放，不会扣除资源。");
        void Notice(string title,string body)
        {if(!IsOpen||disposed||transition.IsTransitioning)return;dialogTitle.text=title;dialogBody.text=body;dialog.SetActive(true);}
        void Dismiss()=>dialog.SetActive(false);
        public void Dispose()
        {
            if(disposed)return;
            if(transition!=null)transition.ResetImmediately();if(dialog!=null)dialog.SetActive(false);disposed=true;
            if(close!=null)close.onClick.RemoveListener(Hide);if(alliance!=null)alliance.onClick.RemoveListener(Alliance);if(territory!=null)territory.onClick.RemoveListener(Territory);
            if(upgrade!=null)upgrade.onClick.RemoveListener(Upgrade);if(dismiss!=null)dismiss.onClick.RemoveListener(Dismiss);
        }
    }

    public sealed class BuildingDetailDefinition
    {
        public readonly string Description,UpgradeEffect;
        BuildingDetailDefinition(string description,string effect){Description=description;UpgradeEffect=effect;}
        public static BuildingDetailDefinition For(int id)
        {
            switch(id)
            {
                case 1:return new BuildingDetailDefinition("城池政务与领地管理的中心","野矿上限 2 → 3    营寨上限 1 → 2");
                case 2:return new BuildingDetailDefinition("储备城池物资，保障建设供给","提高铁、木、粮的资源存储上限");
                case 3:return new BuildingDetailDefinition("招募与训练士兵，壮大城池军力","扩大兵力容量，提升训练效率");
                case 4:return new BuildingDetailDefinition("汇聚商贸收益，提供城池建设资金","提高铜钱收益与经营能力");
                case 5:return new BuildingDetailDefinition("集结将领，统筹军务与武将事务","提高可容纳的武将数量");
                case 7:return new BuildingDetailDefinition("耕作良田，持续供应军民粮食","提高城内粮食产量");
                case 8:return new BuildingDetailDefinition("采集木材，为城池建设提供材料","提高城内木材产量");
                case 9:return new BuildingDetailDefinition("开采与冶炼铁料，保障军备生产","提高城内铁料产量");
                case 10:return new BuildingDetailDefinition("守护城池，部署驻军与城防设施","提升城防与驻军防御能力");
                case 11:return new BuildingDetailDefinition("锻造、合成并强化将领装备","提高装备制作与强化能力");
                case 12:return new BuildingDetailDefinition("研习军政技术，推动城池发展","开放更高等级的研究能力");
                case 13:return new BuildingDetailDefinition("协调盟友往来，组织联攻与联防","提升协防与盟友协作能力");
                case 14:return new BuildingDetailDefinition("观察敌情，掌握城外军事动向","提升城池侦察与预警能力");
                case 15:return new BuildingDetailDefinition("主持祭祀，研习城池法术","提升祭祀与法术能力");
                case 16:return new BuildingDetailDefinition("救治伤兵，帮助军队恢复战力","提高伤兵容纳与治疗能力");
                default:return new BuildingDetailDefinition("查看建筑状态与建设信息","具体升级收益将在配置后显示");
            }
        }
    }
}
