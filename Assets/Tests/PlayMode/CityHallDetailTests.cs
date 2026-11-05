#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using Client.Contracts;
using Client.Framework.Presentation;
using Client.Framework.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object=UnityEngine.Object;

namespace Client.Tests
{
    [Category("C1")]
    public sealed class CityHallDetailTests
    {
        sealed class View:IViewHandle {public GameObject Root{get;set;}public void Close(){}}
        GameObject canvas,root,events;
        object presenter;
        UIBindings bindings;
        CityViewport map;
        BuildingDetailTransition Transition=>Get<BuildingDetailTransition>("DetailTransition");
        static readonly Type Presenter=Type.GetType("Client.Game.MainCity.UIMainCity, Client.Game");
        T Get<T>(string id)where T:Component=>(T)bindings.Resolve(id);
        void Select(int id)=>Presenter.GetMethod("Selected",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(presenter,new object[]{id,id==1?"城主府":"仓库"});
        [UnitySetUp]
        public IEnumerator Setup()
        {
            Assert.That(Presenter,Is.Not.Null);
            events=new GameObject("City Detail Events",typeof(EventSystem),typeof(StandaloneInputModule));
            canvas=new GameObject("City Detail Canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1080,1920);scaler.matchWidthOrHeight=.5f;
            root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Content/UI/MainCity/UIMainCity.prefab"),canvas.transform,false);
            bindings=root.GetComponent<UIBindings>();map=Get<CityViewport>("Map");
            presenter=Activator.CreateInstance(Presenter);Presenter.GetMethod("Bind").Invoke(presenter,new object[]{new View{Root=root},(Action)(()=>{})});
            Canvas.ForceUpdateCanvases();yield return null;yield return null;
        }
        [UnityTearDown]
        public IEnumerator Cleanup()
        {if(presenter!=null)Presenter.GetMethod("Unbind").Invoke(presenter,null);if(root!=null)Object.Destroy(root);if(canvas!=null)Object.Destroy(canvas);if(events!=null)Object.Destroy(events);yield return null;yield return null;}

        [UnityTest]
        public IEnumerator CityHallClickOpensFullPageAndCloseKeepsViewportAlive()
        {
            Assert.That(map.IsReady,Is.True);
            Assert.That(map.TryGetFocusView(1,out var focus),Is.True);map.ApplyView(focus);Canvas.ForceUpdateCanvases();yield return null;
            var region=(RectTransform)map.transform;
            var hitCenter=map.WorldRoot.GetComponentsInChildren<BoxCollider>().First(c=>c.name=="CityViewport.Building.1").bounds.center;
            var uv=map.ViewCamera.WorldToViewportPoint(hitCenter);
            var local=new Vector3(Mathf.Lerp(region.rect.xMin,region.rect.xMax,uv.x),Mathf.Lerp(region.rect.yMin,region.rect.yMax,uv.y),0);
            var point=RectTransformUtility.WorldToScreenPoint(null,region.TransformPoint(local));
            var pointer=new PointerEventData(EventSystem.current){pointerId=0,position=point,button=PointerEventData.InputButton.Left};
            map.OnPointerDown(pointer);map.OnPointerUp(pointer);
            Assert.That(map.SelectedBuildingId,Is.EqualTo(1),"The real city collider must dispatch the selection.");
            Assert.That(map.WorldRoot.transform.Find("CityViewport.Selection"),Is.Null,"Clicking buildings must not create a yellow selection frame.");
            Assert.That(Get<RectTransform>("CityDetailPage").GetComponent<CanvasGroup>().alpha,Is.GreaterThan(0),"Details should begin appearing immediately, without a focus delay.");
            Assert.That(Transition.IsTransitioning,Is.True);yield return new WaitForSecondsRealtime(.70f);
            Assert.That(Get<RectTransform>("CityDetailPage").gameObject.activeSelf,Is.True);
            Assert.That(Get<RectTransform>("CityDetailBackdrop").gameObject.activeSelf,Is.True);
            Assert.That(Get<RectTransform>("CityHud").gameObject.activeSelf,Is.False);
            var camera=map.ViewCamera;var position=camera.transform.position;
            Assert.That(map.GetComponent<RawImage>().raycastTarget,Is.False);
            Get<Button>("CloseCityDetails").onClick.Invoke();yield return new WaitForSecondsRealtime(.40f);
            Assert.That(Get<RectTransform>("CityDetailPage").gameObject.activeSelf,Is.False);
            Assert.That(map.IsReady,Is.True);Assert.That(map.ViewCamera,Is.SameAs(camera));
            Assert.That(map.ViewCamera.transform.position,Is.EqualTo(position));
            Assert.That(map.GetComponent<RawImage>().raycastTarget,Is.True);
        }
        [UnityTest]
        public IEnumerator AllianceTerritoryAndUpgradeShowDistinctLocalModeFeedback()
        {
            Select(1);yield return new WaitForSecondsRealtime(.70f);
            foreach(var pair in new[]{new[]{"AllianceButton","联盟"},new[]{"TerritoryButton","领地"},new[]{"UpgradeCityHall","城主府升级"}})
            {
                Get<Button>(pair[0]).onClick.Invoke();
                Assert.That(Get<RectTransform>("CityDetailDialog").gameObject.activeSelf,Is.True);
                Assert.That(Get<Text>("CityDialogTitle").text,Is.EqualTo(pair[1]));
                Assert.That(Get<Text>("CityDialogBody").text,Does.Contain("本地"));
                Get<Button>("DismissCityDialog").onClick.Invoke();
                Assert.That(Get<RectTransform>("CityDetailDialog").gameObject.activeSelf,Is.False);
                Assert.That(Get<RectTransform>("CityDetailPage").gameObject.activeSelf,Is.True);
            }
        }
        [UnityTest]
        public IEnumerator RedesignedControlsRemainHitTestable()
        {
            Select(1);yield return new WaitForSecondsRealtime(.4f);Canvas.ForceUpdateCanvases();
            foreach(string id in new[]{"AllianceButton","TerritoryButton","UpgradeCityHall"})
            {
                ClickVisible(Get<Button>(id));
                Assert.That(Get<RectTransform>("CityDetailDialog").gameObject.activeSelf,Is.True);
                yield return null;
                ClickVisible(Get<Button>("DismissCityDialog"));
                Assert.That(Get<RectTransform>("CityDetailDialog").gameObject.activeSelf,Is.False);
                yield return null;
            }
            ClickVisible(Get<Button>("CloseCityDetails"));yield return new WaitForSecondsRealtime(.4f);
            Assert.That(Transition.IsOpen,Is.False);
            void ClickVisible(Button button)
            {
                Canvas.ForceUpdateCanvases();
                var rect=(RectTransform)button.transform;
                var point=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center));
                var pointer=new PointerEventData(EventSystem.current){position=point,button=PointerEventData.InputButton.Left};
                var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(pointer,hits);
                Assert.That(hits.Count,Is.GreaterThan(0),button.name+" has no hit surface.");
                Assert.That(hits[0].gameObject.GetComponentInParent<Button>(),Is.SameAs(button),button.name+" is blocked by "+hits[0].gameObject.name+"; expected graphic depth="+button.targetGraphic.depth);
                ExecuteEvents.Execute(button.gameObject,pointer,ExecuteEvents.pointerClickHandler);
            }
        }
        [UnityTest]
        public IEnumerator OtherBuildingsUseTheSameFullPageAndClosingRestoresTheCamera()
        {
            var original=map.CaptureView();
            var originalPosition=map.ViewCamera.transform.position;
            foreach(int id in new[]{1,2,3,7,16})
            {
                Select(id);
                Assert.That(Get<RectTransform>("CityDetailPage").GetComponent<CanvasGroup>().alpha,Is.GreaterThan(0));
                float until=Time.unscaledTime+.4f;
                while(Time.unscaledTime<until){AssertMapUnchanged();yield return null;}
                Assert.That(Get<RectTransform>("CityDetailPage").gameObject.activeSelf,Is.True);
                var preview=Get<BuildingPreviewViewport>("BuildingPreview");
                Assert.That(preview.BuildingId,Is.EqualTo(id));Assert.That(preview.IsReady,Is.True);
                Assert.That(preview.ModelRoot.name,Is.EqualTo("BuildingPreview.Model."+id));
                var previewRect=(RectTransform)preview.transform;
                var information=(RectTransform)Get<RectTransform>("CityDetailPage").Find("UpgradeInformation");
                float gap=-information.anchoredPosition.y-(-previewRect.anchoredPosition.y+previewRect.rect.height);
                Assert.That(gap,Is.EqualTo(24).Within(1),"Upgrade details should directly follow the actual building preview height.");
                Assert.That(Get<Button>("AllianceButton").gameObject.activeSelf,Is.EqualTo(id==1));
                Assert.That(Get<Text>("DetailDescription").text,Is.Not.Empty);
                Assert.That(Get<Text>("DetailBenefits").text,Is.Not.Empty);
                Get<Button>("CloseCityDetails").onClick.Invoke();until=Time.unscaledTime+.4f;
                while(Time.unscaledTime<until){AssertMapUnchanged();yield return null;}
                Assert.That(Transition.IsOpen,Is.False);
                Assert.That(preview.IsReady,Is.False,"Closing details must release the isolated building renderer.");
                Assert.That(Vector3.Distance(map.CaptureView().Center,original.Center),Is.LessThan(.001f));
                Assert.That(map.CaptureView().OrthographicSize,Is.EqualTo(original.OrthographicSize).Within(.001f));
            }
            void AssertMapUnchanged()
            {
                Assert.That(Vector3.Distance(map.ViewCamera.transform.position,originalPosition),Is.LessThan(.001f),"Opening and closing details must not center the map on the building.");
                Assert.That(Vector3.Distance(map.CaptureView().Center,original.Center),Is.LessThan(.001f));
                Assert.That(map.CaptureView().OrthographicSize,Is.EqualTo(original.OrthographicSize).Within(.001f));
            }
        }
        [UnityTest]
        public IEnumerator UnbindDuringOpeningCancelsMotionAndRestoresInput()
        {
            Select(1);yield return new WaitForSecondsRealtime(.10f);
            Assert.That(Transition.IsTransitioning,Is.True);
            Presenter.GetMethod("Unbind").Invoke(presenter,null);presenter=null;
            Get<Button>("AllianceButton").onClick.Invoke();
            yield return new WaitForSecondsRealtime(.65f);
            Assert.That(Get<RectTransform>("CityDetailDialog").gameObject.activeSelf,Is.False);
            Assert.That(Get<RectTransform>("CityDetailPage").gameObject.activeSelf,Is.False);
            Assert.That(Transition.IsTransitioning,Is.False);
            Assert.That(map.GetComponent<RawImage>().raycastTarget,Is.True);
        }
        [Test]
        public void RemovedTaskAndReturnLoginControlsAreAbsent()
        {
            Assert.That(root.transform.Find("UINode/SafeArea/Static/Tasks"),Is.Null);
            Assert.That(root.transform.Find("UINode/SafeArea/Static/ReturnButton"),Is.Null);
        }
        [Test]
        public void DetailBackgroundIsFullscreenThemeWithTopWallet()
        {
            var backdrop=Get<RectTransform>("CityDetailBackdrop");
            var wash=(RectTransform)backdrop.Find("SlateBackground");
            Assert.That(wash.anchorMin.x,Is.Zero);Assert.That(wash.anchorMax.x,Is.EqualTo(1));
            Assert.That(wash.anchorMin.y,Is.Zero);Assert.That(wash.anchorMax.y,Is.EqualTo(1));
            var page=Get<RectTransform>("CityDetailPage");
            var stock=(RectTransform)page.Find("ResourceStock");
            Assert.That(stock.anchorMin.y,Is.GreaterThan(.93f));Assert.That(stock.anchorMax.y,Is.LessThanOrEqualTo(1));
            Assert.That(stock.offsetMax.y,Is.LessThanOrEqualTo(-60),"Resource text needs extra inset below the device safe-area edge.");
            var frame=Get<RectTransform>("DetailResourceFrame");Assert.That(frame.offsetMax.y,Is.EqualTo(-36));
            Assert.That(frame.GetComponent<Graphic>(),Is.Null,"Resources should reveal the original mountain background without a solid plate.");
            Assert.That(frame.GetComponentsInChildren<Image>().Length,Is.EqualTo(1),"Resources keep only their lower rule.");
            Assert.That(frame.Find("Top"),Is.Null);
            var mainResourceFrame=Get<RectTransform>("MainUIResourceFrame");
            Assert.That(frame.offsetMin,Is.EqualTo(mainResourceFrame.offsetMin));Assert.That(frame.offsetMax,Is.EqualTo(mainResourceFrame.offsetMax));
            var info=(RectTransform)page.Find("UpgradeInformation");
            Assert.That(info.anchorMin.x,Is.Zero);Assert.That(info.anchorMax.x,Is.EqualTo(1));
            var source=(Texture2D)wash.GetComponent<RawImage>().texture;
            Assert.That(source.name,Is.EqualTo("BuildingDetailThemeBorderless"));
            var texture=new Texture2D(2,2);
            try
            {
                texture.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(source)));
                var bottom=texture.GetPixel(texture.width/2,0);var top=texture.GetPixel(texture.width/2,texture.height-1);
                Assert.That(bottom.a,Is.GreaterThan(.95f));Assert.That(top.a,Is.GreaterThan(.9f));
                var center=texture.GetPixel(texture.width/2,texture.height/3);
                Assert.That(center.grayscale,Is.GreaterThan(.65f),"Upgrade text needs a quiet light background.");
            }
            finally{Object.DestroyImmediate(texture);}
        }
        [UnityTest]
        public IEnumerator SelectedBuildingPreviewHasVisiblePixelsAndTransparentMargins()
        {
            Select(3);yield return new WaitForSecondsRealtime(.4f);
            var preview=Get<BuildingPreviewViewport>("BuildingPreview");Assert.That(preview.IsReady,Is.True);preview.RenderNow();
            var target=(RenderTexture)preview.Texture;var previous=RenderTexture.active;RenderTexture.active=target;
            var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
            try
            {
                image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
                var pixels=image.GetPixels32();int visible=pixels.Count(p=>p.a>128),clear=pixels.Count(p=>p.a<10);
                Assert.That(visible,Is.GreaterThan(pixels.Length*.05f),"The selected building must actually render.");
                Assert.That(clear,Is.GreaterThan(pixels.Length*.05f),"The preview must not include the surrounding city or an opaque rectangle.");
            }
            finally{RenderTexture.active=previous;Object.Destroy(image);}
        }
        [UnityTest]
        public IEnumerator DestroyedWindowCanStillBeUnboundSafely()
        {
            Select(1);yield return new WaitForSecondsRealtime(.10f);
            Object.Destroy(root);yield return null;root=null;
            Assert.DoesNotThrow(()=>Presenter.GetMethod("Unbind").Invoke(presenter,null));presenter=null;
        }
        [UnityTest]
        public IEnumerator MainUIUsesFullscreenRootWithTopStatusAndBottomActions()
        {
            var main=Get<RectTransform>("MainUI");
            Assert.That(main.anchorMin,Is.EqualTo(Vector2.zero));Assert.That(main.anchorMax,Is.EqualTo(Vector2.one));
            Assert.That(main.GetComponent<Graphic>(),Is.Null,"Fullscreen MainUI must leave city input available.");
            var dock=(RectTransform)main.Find("Top");Assert.That(dock.sizeDelta.y,Is.EqualTo(160));Assert.That(dock.anchorMin.y,Is.EqualTo(1));
            Assert.That(Get<Button>("MainUIPlayerButton").transform.parent,Is.SameAs(main.Find("Bottom")));
            Assert.That(Get<Text>("MainUIExperience").transform.parent,Is.SameAs(main.Find("Bottom")));
            var playerFrame=Get<RectTransform>("MainUIPlayerFrame");
            Assert.That(playerFrame.offsetMin.x,Is.Zero);Assert.That(playerFrame.offsetMax.x,Is.Zero);
            Assert.That(playerFrame.GetComponentsInChildren<Image>().Length,Is.EqualTo(1),"Player area keeps only a full-width top separator.");
            Assert.That(playerFrame.Find("GoldRule"),Is.Not.Null);
            var point=RectTransformUtility.WorldToScreenPoint(null,dock.TransformPoint(dock.rect.center));
            var pointer=new PointerEventData(EventSystem.current){position=point};var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(pointer,hits);
            Assert.That(hits.Count,Is.GreaterThan(0));Assert.That(hits[0].gameObject.transform.IsChildOf(main),Is.True);
            point=RectTransformUtility.WorldToScreenPoint(null,main.TransformPoint(main.rect.center));
            pointer.position=point;hits.Clear();EventSystem.current.RaycastAll(pointer,hits);
            Assert.That(hits.Any(h=>h.gameObject.transform.IsChildOf(main)),Is.False,"The city between top status and bottom actions must remain draggable.");
            Get<Button>("MainUIFunctions").onClick.Invoke();Assert.That(Get<RectTransform>("MainUIFunctionsMenu").gameObject.activeSelf,Is.True);
            Select(3);yield return new WaitForSecondsRealtime(.4f);
            Assert.That(main.gameObject.activeInHierarchy,Is.False);Assert.That(Get<RectTransform>("MainUIFunctionsMenu").gameObject.activeSelf,Is.False);
            Get<Button>("CloseCityDetails").onClick.Invoke();yield return new WaitForSecondsRealtime(.4f);
            Assert.That(main.gameObject.activeInHierarchy,Is.True);
        }
        [Test]
        public void MainUIDataRefreshUpdatesRoleAndBothResourceDisplays()
        {
            var type=Presenter.Assembly.GetType("Client.Game.MainCity.MainCityPlayerInfo");var info=Activator.CreateInstance(type);
            type.GetField("Name").SetValue(info,"江东主公");type.GetField("Level").SetValue(info,12);type.GetField("NobleLevel").SetValue(info,3);
            type.GetField("CombatPower").SetValue(info,34567L);type.GetField("Gold").SetValue(info,99999L);
            type.GetField("Experience").SetValue(info,300L);type.GetField("ExperienceToNextLevel").SetValue(info,500L);
            Presenter.GetMethod("SetPlayerInfo").Invoke(presenter,new[]{info});
            Assert.That(Get<Text>("MainUIName").text,Is.EqualTo("江东主公"));Assert.That(Get<Text>("MainUILevel").text,Is.EqualTo("等级 12"));
            Assert.That(Get<Text>("MainUINoble").text,Is.EqualTo("贵族 3"));Assert.That(Get<Text>("MainUIPower").text,Is.EqualTo("34,567"));
            Assert.That(Get<Text>("MainUIExperience").text,Is.EqualTo("经验 300 / 500"));Assert.That(Get<Image>("MainUIExperienceFill").fillAmount,Is.EqualTo(.6f).Within(.001f));
            Assert.That(Get<Text>("MainUIGold").text,Is.EqualTo("99,999"));Assert.That(Get<Text>("DetailGoldStock").text,Is.EqualTo("99,999"));
        }
        [UnityTest]
        public IEnumerator MainUIProfileAndFunctionsUseRealButtonsWithoutMovingMap()
        {
            var position=map.ViewCamera.transform.position;
            Hit("MainUIPlayerButton");yield return null;
            Assert.That(Get<RectTransform>("MainUIDialog").gameObject.activeSelf,Is.True);Assert.That(Get<Text>("MainUIDialogTitle").text,Is.EqualTo("主公信息"));
            Hit("MainUIDialogClose");yield return null;
            Hit("MainUIFunctions");yield return null;
            foreach(string id in new[]{"MainUIHeroes","MainUIBag","MainUIAlliance","MainUITerritory"})
            {
                Hit(id);yield return null;Assert.That(Get<Text>("MainUIDialogBody").text,Does.Contain("暂未开放"));
                Hit("MainUIDialogClose");yield return null;Hit("MainUIFunctions");yield return null;
            }
            Hit("MainUIFunctions");Assert.That(Get<RectTransform>("MainUIFunctionsMenu").gameObject.activeSelf,Is.False);
            Assert.That(map.ViewCamera.transform.position,Is.EqualTo(position));
            void Hit(string id)
            {
                Canvas.ForceUpdateCanvases();var button=Get<Button>(id);var rect=(RectTransform)button.transform;
                var pointer=new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center)),button=PointerEventData.InputButton.Left};
                var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(pointer,hits);
                Assert.That(hits.Count,Is.GreaterThan(0));Assert.That(hits[0].gameObject.GetComponentInParent<Button>(),Is.SameAs(button),id+" is blocked");
                ExecuteEvents.Execute(button.gameObject,pointer,ExecuteEvents.pointerClickHandler);
            }
        }
        [UnityTest]
        public IEnumerator NativeAnimationRendersToOwnedPreviewFrames()
        {
            var cameraObject=new GameObject("Building transition capture",typeof(Camera));
            var camera=cameraObject.GetComponent<Camera>();camera.orthographic=true;camera.transform.position=new Vector3(0,0,-10);
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.scene=canvas.scene;
            var target=new RenderTexture(540,960,24);target.Create();camera.targetTexture=target;
            var rootCanvas=canvas.GetComponent<Canvas>();rootCanvas.renderMode=RenderMode.ScreenSpaceCamera;rootCanvas.worldCamera=camera;rootCanvas.planeDistance=1;
            string directory="Builds/CityHallDetails/AnimationFrames";Directory.CreateDirectory(directory);
            try
            {
                yield return null;Canvas.ForceUpdateCanvases();
                Select(1);
                for(int i=0;i<20;i++){yield return new WaitForSecondsRealtime(.05f);Capture(i);}
                Assert.That(Transition.IsTransitioning,Is.False);
                Assert.That(Get<RectTransform>("CityDetailPage").localScale,Is.EqualTo(Vector3.one));
                Get<Button>("CloseCityDetails").onClick.Invoke();
                for(int i=20;i<29;i++){yield return new WaitForSecondsRealtime(.05f);Capture(i);}
                Assert.That(Transition.IsOpen,Is.False);
            }
            finally{camera.targetTexture=null;target.Release();Object.Destroy(target);Object.Destroy(cameraObject);}
            void Capture(int frame)
            {
                Canvas.ForceUpdateCanvases();if(map.IsReady)map.ViewCamera.Render();camera.Render();
                var previous=RenderTexture.active;RenderTexture.active=target;
                var picture=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
                try{picture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);picture.Apply();File.WriteAllBytes(directory+"/"+frame.ToString("D3")+".png",picture.EncodeToPNG());}
                finally{RenderTexture.active=previous;Object.Destroy(picture);}
            }
        }
    }
}
#endif
