using System.Collections;
using Client.Framework.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Client.Tests
{
    [Category("CityViewport")]
    public sealed class CityViewportTests
    {
        private GameObject canvasRoot, city, events;
        private CityViewport viewport;
        private RectTransform region;

        [SetUp]
        public void SetUp()
        {
            events = new GameObject("City pointer events", typeof(EventSystem));
            canvasRoot = new GameObject("City canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            canvasRoot.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var image = new GameObject("Map", typeof(RectTransform), typeof(RawImage));
            image.transform.SetParent(canvasRoot.transform, false);
            region = image.GetComponent<RectTransform>();
            region.sizeDelta = new Vector2(600, 400);
            city = new GameObject("Fixture city");
            var buildings = new GameObject("BuildingRoot"); buildings.transform.SetParent(city.transform, false);
            Building(buildings.transform, 1, -4);
            Building(buildings.transform, 2, 4);
            var wall = new GameObject("buildParent10"); wall.transform.SetParent(buildings.transform, false);
            WallSection(wall.transform, -10); WallSection(wall.transform, 10);
            city.SetActive(false);
            viewport = image.AddComponent<CityViewport>();
            viewport.ConfigureLabels(new[]{"","城主府","仓库"});
            Canvas.ForceUpdateCanvases();
            viewport.Configure(city, region);
            Physics.SyncTransforms();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(canvasRoot); Object.Destroy(city); Object.Destroy(events);
            yield return null;
        }

        [Test]
        public void ConfigureOwnsAnOrthographicCameraAndViewportTexture()
        {
            Assert.That(viewport.IsReady, Is.True);
            Assert.That(viewport.ViewCamera.orthographic, Is.True);
            Assert.That(Vector3.Dot(viewport.ViewCamera.transform.forward, Vector3.down), Is.GreaterThan(.999f));
            Assert.That(viewport.ViewCamera.targetTexture, Is.SameAs(region.GetComponent<RawImage>().texture));
            Assert.That(viewport.WorldRoot, Is.Not.Null);
            Assert.That(viewport.ViewCamera.scene, Is.EqualTo(viewport.WorldRoot.scene));
            Assert.That(viewport.CityBounds.size.x, Is.GreaterThan(10));
        }

        [Test]
        public void PointerClickSelectsTheBuildingAndCreatesVisibleOutline()
        {
            int selected = 0; string name = null;
            viewport.BuildingSelected += (id, label) => { selected = id; name = label; };
            var point = BuildingScreenPoint(1);
            var pointer = Pointer(0, point);
            ExecuteEvents.Execute(region.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(region.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            Assert.That(selected, Is.EqualTo(1)); Assert.That(name, Is.EqualTo("城主府"));
            Assert.That(viewport.SelectedBuildingId, Is.EqualTo(1));
            Assert.That(viewport.WorldRoot.GetComponentInChildren<LineRenderer>().enabled, Is.True);
        }

        [Test]
        public void DragAndDistantReleaseNeverSelectABuilding()
        {
            var pointer = Pointer(0, BuildingScreenPoint(1));
            viewport.OnPointerDown(pointer);
            pointer.position += new Vector2(80, 0); viewport.OnDrag(pointer);
            viewport.OnPointerUp(pointer);
            Assert.That(viewport.SelectedBuildingId, Is.Zero);
            pointer = Pointer(0, BuildingScreenPoint(1));
            viewport.OnPointerDown(pointer);
            pointer.position += new Vector2(80, 0); viewport.OnPointerUp(pointer);
            Assert.That(viewport.SelectedBuildingId, Is.Zero, "A release beyond threshold must be rejected even if a drag event was skipped.");
        }

        [Test]
        public void FocusSelectsKnownBuildingAndIgnoresUnknownIds()
        {
            viewport.FocusBuilding(2);
            Assert.That(viewport.SelectedBuildingId, Is.EqualTo(2));
            viewport.FocusBuilding(999);
            Assert.That(viewport.SelectedBuildingId, Is.EqualTo(2));
        }

        [Test]
        public void PointersOutsideTheImageCannotSelectOrZoom()
        {
            var pointer = Pointer(0, new Vector2(-1000, -1000));
            float size = viewport.ViewCamera.orthographicSize;
            pointer.scrollDelta = Vector2.up * 5;
            viewport.OnPointerDown(pointer); viewport.OnPointerUp(pointer); viewport.OnScroll(pointer);
            Assert.That(viewport.SelectedBuildingId, Is.Zero);
            Assert.That(viewport.ViewCamera.orthographicSize, Is.EqualTo(size));
        }

        [Test]
        public void WheelAndTwoFingerPinchZoomWithoutSelecting()
        {
            var center = RectTransformUtility.WorldToScreenPoint(null, region.TransformPoint(region.rect.center));
            float initial = viewport.ViewCamera.orthographicSize;
            var wheel = Pointer(-1, center); wheel.scrollDelta = Vector2.up;
            viewport.OnScroll(wheel);
            Assert.That(viewport.ViewCamera.orthographicSize, Is.LessThan(initial));
            viewport.ResetView();
            var first = Pointer(0, center + Vector2.left * 30);
            var second = Pointer(1, center + Vector2.right * 30);
            viewport.OnPointerDown(first); viewport.OnPointerDown(second);
            second.position += Vector2.right * 40; viewport.OnDrag(second);
            Assert.That(viewport.ViewCamera.orthographicSize, Is.LessThan(initial));
            viewport.OnPointerUp(second); viewport.OnPointerUp(first);
            Assert.That(viewport.SelectedBuildingId, Is.Zero);
        }

        [Test]
        public void DefaultViewCanDragWithinTheCityAndResetRestoresTheInitialView()
        {
            var center = RectTransformUtility.WorldToScreenPoint(null, region.TransformPoint(region.rect.center));
            var initial = viewport.ViewCamera.transform.position;
            var pointer = Pointer(0, center); viewport.OnPointerDown(pointer);
            pointer.position += new Vector2(100, 40); viewport.OnDrag(pointer); viewport.OnPointerUp(pointer);
            Assert.That(Vector3.Distance(viewport.ViewCamera.transform.position, initial), Is.GreaterThan(.01f));
            var position = viewport.ViewCamera.transform.position;
            Assert.That(position.x, Is.InRange(viewport.CityBounds.min.x, viewport.CityBounds.max.x));
            Assert.That(position.z, Is.InRange(viewport.CityBounds.min.z, viewport.CityBounds.max.z));
            viewport.ResetView();
            Assert.That(viewport.ViewCamera.transform.position.x, Is.EqualTo(viewport.CityBounds.center.x).Within(.01f));
        }

        [UnityTest]
        public IEnumerator PortraitFillKeepsTheMapAcrossTheWholeViewportEvenWhenZoomingOut()
        {
            JsonUtility.FromJsonOverwrite("{\"fillViewport\":true}", viewport);
            region.sizeDelta = new Vector2(1080, 1920);
            Canvas.ForceUpdateCanvases();
            yield return null;
            viewport.ResetView();
            AssertViewportInsideMap();
            var center = RectTransformUtility.WorldToScreenPoint(null, region.TransformPoint(region.rect.center));
            var wheel = Pointer(-1, center); wheel.scrollDelta = Vector2.down * 100;
            viewport.OnScroll(wheel);
            AssertViewportInsideMap();

            void AssertViewportInsideMap()
            {
                var camera = viewport.ViewCamera;
                Assert.That(camera.orthographicSize, Is.LessThanOrEqualTo(viewport.CityBounds.extents.z));
                Assert.That(camera.orthographicSize * camera.aspect, Is.LessThanOrEqualTo(viewport.CityBounds.extents.x));
            }
        }

        [UnityTest]
        public IEnumerator DisableReleasesOwnedResourcesAndEnableRebuilds()
        {
            var oldCamera = viewport.ViewCamera;
            var oldRoot = viewport.WorldRoot;
            var oldTexture = oldCamera.targetTexture;
            viewport.enabled = false;
            Assert.That(viewport.IsReady, Is.False);
            Assert.That(region.GetComponent<RawImage>().texture, Is.Null);
            Assert.That(oldTexture.IsCreated(), Is.False);
            yield return null;
            Assert.That(oldCamera == null, Is.True); Assert.That(oldRoot == null, Is.True); Assert.That(oldTexture == null, Is.True);
            viewport.enabled = true;
            Assert.That(viewport.IsReady, Is.True);
            Assert.That(viewport.ViewCamera, Is.Not.SameAs(oldCamera));
            oldRoot = viewport.WorldRoot; oldTexture = viewport.ViewCamera.targetTexture;
            Object.Destroy(canvasRoot); canvasRoot = null;
            yield return null;
            Assert.That(oldRoot == null, Is.True); Assert.That(oldTexture == null, Is.True);
        }

        private PointerEventData Pointer(int id, Vector2 position)
        {
            return new PointerEventData(events.GetComponent<EventSystem>()) { pointerId = id, position = position, button = PointerEventData.InputButton.Left };
        }

        private Vector2 BuildingScreenPoint(int id)
        {
            var building = viewport.WorldRoot.transform.Find("Fixture city/BuildingRoot/buildParent" + id);
            var uv = viewport.ViewCamera.WorldToViewportPoint(building.GetComponentInChildren<Renderer>().bounds.center);
            var rect = region.rect;
            return RectTransformUtility.WorldToScreenPoint(null, region.TransformPoint(new Vector3(rect.xMin + uv.x * rect.width, rect.yMin + uv.y * rect.height)));
        }

        private static void Building(Transform parent, int id, float x)
        {
            var root = new GameObject("buildParent" + id); root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(x, 0, 0);
            var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube); mesh.transform.SetParent(root.transform, false);
            mesh.transform.localScale = new Vector3(4, 1, 6);
            Object.DestroyImmediate(mesh.GetComponent<Collider>());
        }

        private static void WallSection(Transform parent, float x)
        {
            var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube); mesh.transform.SetParent(parent, false);
            mesh.transform.localPosition = new Vector3(x, 5, 0);
            mesh.transform.localScale = new Vector3(2, 1, 12);
            Object.DestroyImmediate(mesh.GetComponent<Collider>());
        }
    }
}
