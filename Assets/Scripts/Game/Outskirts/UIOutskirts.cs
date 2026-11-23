using System;
using Client.Contracts;
using Client.Framework.Presentation;
using Client.Framework.UI;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace Client.Game.Outskirts
{
    public sealed partial class UIOutskirts : IDisposable
    {
        private readonly CityViewport map;
        private readonly Button back;
        private readonly Button overview;
        private readonly Text detail;
        private readonly Button[] timeButtons = new Button[4];
        private readonly UnityAction[] timeActions = new UnityAction[4];
        private static readonly string[] TimeCaptions = { "清晨", "白天", "黄昏", "夜晚" };
        private static readonly int[] PreviewHours = { 9, 13, 17, 21 };
        private int selectedTime = -1;
        private readonly OutskirtsSnapshot snapshot;
        private readonly Action returnToCity;

        internal UIOutskirts(IViewHandle view, OutskirtsSnapshot snapshot, Action returnToCity)
        {
            this.snapshot = snapshot;
            this.returnToCity = returnToCity;
            var bindings = view.Root.GetComponent<UIBindings>();
            BindComponents(bindings);
            map = Map;
            back = Back;
            overview = Overview;
            detail = Detail;
            var timeIds = new[] { "TimeDawn", "TimeDay", "TimeDusk", "TimeNight" };
            for (int i = 0; i < timeButtons.Length; i++)
            {
                int index = i;
                timeButtons[i] = (Button)bindings.Resolve(timeIds[i]);
                timeActions[i] = () => PreviewTime(index);
                timeButtons[i].onClick.AddListener(timeActions[i]);
            }
            map.GroundClicked += Select;
            back.onClick.AddListener(OnBack);
            overview.onClick.AddListener(map.ShowOverview);
        }

        private void PreviewTime(int index)
        {
            selectedTime = selectedTime == index ? -1 : index;
            map.SetClock(selectedTime < 0 ? (IOutskirtsClock)new LocalOutskirtsClock() : new PreviewClock(PreviewHours[selectedTime]));
            for (int i = 0; i < timeButtons.Length; i++)
            {
                bool active = selectedTime == i;
                timeButtons[i].targetGraphic.color = active ? new Color(.25f, .30f, .15f, .98f) : new Color(.10f, .15f, .11f, .96f);
                timeButtons[i].GetComponentInChildren<Text>().text = TimeCaptions[i] + (active ? " · 预览" : string.Empty);
            }
        }

        private sealed class PreviewClock : IOutskirtsClock
        {
            public DateTimeOffset GameTime { get; }
            public PreviewClock(int hour) => GameTime = new DateTimeOffset(2026, 1, 1, hour, 0, 0, TimeSpan.FromHours(8));
        }

        private void OnBack() => returnToCity();

        private void Select(Vector3 point)
        {
            int position = OutskirtsHex.Pick(point);
            if (position == 0) return;
            var center = OutskirtsHex.Center(position);
            int gridRow = OutskirtsHex.Rows - Mathf.RoundToInt(center.z / OutskirtsHex.RowStep);
            int gridColumn = Mathf.RoundToInt(center.x - (Mathf.RoundToInt(center.z / OutskirtsHex.RowStep) & 1) * .5f) + 1;
            int gridNumber = (gridRow - 1) * OutskirtsHex.Columns + gridColumn;
            var tile = map.WorldRoot.transform.Find("BeginnerOutskirts/BuildingRoot/Tile_" + position);
            var identity = tile.GetComponent<OutskirtsResourceIdentity>();
            var sprite = tile.GetComponentInChildren<SpriteRenderer>();
            string key = identity != null ? identity.ResourceKey : sprite != null ? sprite.sprite.name : "地块";
            string name = tile.Find("OwnCityLabel") != null ? "我的主城 · " + key : key;
            detail.text = name + " · 第 " + gridNumber + " 格（第 " + gridRow + " 行，第 " + gridColumn + " 列）";
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
            for (int i = 0; i < timeButtons.Length; i++) timeButtons[i].onClick.RemoveListener(timeActions[i]);
            ClearBindings();
        }
    }
}
