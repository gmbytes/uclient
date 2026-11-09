using System;
using Client.Contracts;
using Client.Framework.Presentation;
using Client.Framework.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Client.Game.CityOutskirts
{
    public sealed partial class UICityOutskirts : IDisposable
    {
        private readonly CityViewport map;
        private readonly Button back;
        private readonly Button overview;
        private readonly Text detail;
        private readonly OutskirtsSnapshot snapshot;
        private readonly Action returnToCity;

        internal UICityOutskirts(IViewHandle view, OutskirtsSnapshot snapshot, Action returnToCity)
        {
            this.snapshot = snapshot;
            this.returnToCity = returnToCity;
            var bindings = view.Root.GetComponent<UIBindings>();
            BindComponents(bindings);
            map = Map;
            back = Back;
            overview = Overview;
            detail = Detail;
            map.GroundClicked += Select;
            back.onClick.AddListener(OnBack);
            overview.onClick.AddListener(map.ShowOverview);
        }

        private void OnBack() => returnToCity();

        private void Select(Vector3 point)
        {
            int position = OutskirtsHex.Pick(point);
            if (position == 0) return;
            var cell = snapshot.cells[position - 1];
            switch (cell.kind)
            {
                case 5: detail.text = "我的主城 · 第 " + position + " 格"; break;
                case 15: detail.text = "新手 NPC 据点 · 第 " + position + " 格"; break;
                case 16: detail.text = "县城 · 第 " + position + " 格"; break;
                default: detail.text = ResourceName(cell.resource) + " · 第 " + position + " 格" +
                    (cell.level > 0 ? " · 等级 " + cell.level : string.Empty); break;
            }
        }

        private static string ResourceName(OutskirtsResourceKind resource)
        {
            switch (resource)
            {
                case OutskirtsResourceKind.Wood: return "木材地";
                case OutskirtsResourceKind.Stone: return "石料地";
                case OutskirtsResourceKind.Iron: return "铁矿地";
                case OutskirtsResourceKind.Food: return "粮田";
                default: return "新手城外";
            }
        }

        public void Dispose()
        {
            map.GroundClicked -= Select;
            back.onClick.RemoveListener(OnBack);
            overview.onClick.RemoveListener(map.ShowOverview);
            ClearBindings();
        }
    }
}
