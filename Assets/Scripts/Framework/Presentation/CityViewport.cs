using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Client.Framework.Presentation
{
    /// <summary>
    /// A self-owned, orthographic city rendered exclusively into a UGUI RawImage.
    /// Attach to the RawImage, or an ancestor with no intervening pointer handlers;
    /// normal UGUI event bubbling then forwards only events originating in the image.
    /// Configure in the editor stores prefab references without creating runtime objects.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityViewport : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IScrollHandler
    {
        [SerializeField] private GameObject cityPrefab;
        [SerializeField] private RectTransform inputRegion;
        private const int CityLayer = 31;
        private const float DragThresholdSquared = 36f;
        [SerializeField] private string[] buildingNames=Array.Empty<string>();
        [SerializeField] private float defaultZoomRatio=.62f;
        [SerializeField] private bool fillViewport;
        public void ConfigureFillViewport(bool value)
        {
            fillViewport=value;
            if(IsReady)ResetView();
        }
        private float MaximumZoom => fillViewport
            ? Mathf.Max(.01f, Mathf.Min(CityBounds.extents.z, CityBounds.extents.x / ViewCamera.aspect) * .96f)
            : initialZoom * 1.6f;
        private float MinimumZoom => Mathf.Min(initialZoom * .18f, MaximumZoom * .5f);
        public void ConfigureLabels(string[] names)=>buildingNames=names!=null?(string[])names.Clone():Array.Empty<string>();
        public void ConfigureInitialZoom(float ratio)
        {
            if(float.IsNaN(ratio)||float.IsInfinity(ratio)||ratio<.18f||ratio>1.6f)throw new ArgumentOutOfRangeException(nameof(ratio));
            defaultZoomRatio=ratio;
            if(IsReady)ResetView();
        }

        private sealed class PointerState
        {
            public Vector2 Origin, Position;
            public Camera EventCamera;
            public bool SuppressClick;
        }

        private sealed class Building
        {
            public int Id;
            public Bounds Bounds;
            public readonly List<BoxCollider> Colliders = new List<BoxCollider>();
        }

        private readonly Dictionary<int, PointerState> pointers = new Dictionary<int, PointerState>();
        private readonly List<Building> buildings = new List<Building>();
        private readonly Vector3[] regionCorners = new Vector3[4];
        private RawImage output;
        private Texture previousTexture;
        private RenderTexture texture;
        private Material outlineMaterial;
        private Scene renderScene;
        private LineRenderer outline;
        private Vector3 center;
        private float initialZoom, cameraDistance, previousPinchDistance;
        private int sceneGeneration;

        public event Action<int, string> BuildingSelected;
        public int SelectedBuildingId { get; private set; }
        public bool IsReady => ViewCamera != null && WorldRoot != null && texture != null;
        public Camera ViewCamera { get; private set; }
        public GameObject WorldRoot { get; private set; }
        public Bounds CityBounds { get; private set; }

        public void Configure(GameObject prefab, RectTransform region)
        {
            Release();
            cityPrefab = prefab;
            inputRegion = region;
            if (Application.isPlaying && isActiveAndEnabled) Build();
        }

        private void OnEnable()
        {
            if (Application.isPlaying) Build();
        }

        private void OnDisable() => Release();
        private void OnDestroy() => Release();

        private void Build()
        {
            if (IsReady || cityPrefab == null || inputRegion == null) return;
            output = inputRegion.GetComponent<RawImage>();
            if (output == null)
            {
                Debug.LogError("CityViewport inputRegion must contain a RawImage.", this);
                return;
            }
            previousTexture = output.texture;
            renderScene = SceneManager.CreateScene("CityViewport.Scene." + GetInstanceID() + "." + ++sceneGeneration);
            WorldRoot = new GameObject("CityViewport.World." + GetInstanceID());
            WorldRoot.hideFlags = HideFlags.DontSave;
            SceneManager.MoveGameObjectToScene(WorldRoot, renderScene);
            // Keep UI-owned geometry away from any ordinary scene camera/physics.
            WorldRoot.transform.position = new Vector3(0, -10000, 0);
            var city = Instantiate(cityPrefab, WorldRoot.transform, false);
            city.name = cityPrefab.name;
            city.SetActive(true);
            foreach (var item in WorldRoot.GetComponentsInChildren<Transform>(true)) item.gameObject.layer = CityLayer;
            var framing = city.transform.Find("BuildingRoot") ?? city.transform;
            CityBounds = RendererBounds(framing, out var found);
            if (!found) CityBounds = new Bounds(WorldRoot.transform.position, Vector3.one * 10);

            var cameraObject = new GameObject("CityViewport.Camera");
            cameraObject.transform.SetParent(WorldRoot.transform, false);
            ViewCamera = cameraObject.AddComponent<Camera>();
#if UNITY_EDITOR
            ViewCamera.scene = renderScene;
#endif
            ViewCamera.orthographic = true;
            // These assets are flat 2.5D artwork authored for a straight-down camera.
            ViewCamera.transform.rotation = Quaternion.Euler(90, 0, 0);
            ViewCamera.cullingMask = 1 << CityLayer;
            ViewCamera.clearFlags = CameraClearFlags.SolidColor;
            ViewCamera.backgroundColor = new Color(.07f, .11f, .12f, 1);
            ViewCamera.allowHDR = false;
            ViewCamera.allowMSAA = false;
            cameraDistance = Mathf.Max(CityBounds.size.magnitude * 2, 100);
            ViewCamera.nearClipPlane = .1f;
            ViewCamera.farClipPlane = cameraDistance * 4;
            CollectBuildings(framing);
            CreateOutline();
            ResizeTexture();
            ResetView();
        }

        private void CollectBuildings(Transform framing)
        {
            foreach (var child in framing.GetComponentsInChildren<Transform>(true))
            {
                const string prefix = "buildParent";
                if (!child.name.StartsWith(prefix, StringComparison.Ordinal) ||
                    !int.TryParse(child.name.Substring(prefix.Length), out var id) || id < 1 || id > 16) continue;
                var bounds = RendererBounds(child, out var found);
                if (!found) continue;
                var building = new Building { Id = id, Bounds = bounds };
                // Separate wall sections must not become one giant clickable box over the city.
                foreach (var renderer in child.GetComponentsInChildren<Renderer>())
                {
                    if (!renderer.enabled) continue;
                    var hitBounds = renderer.bounds;
                    var proxy = new GameObject("CityViewport.Building." + id);
                    proxy.layer = CityLayer;
                    proxy.transform.SetParent(WorldRoot.transform, false);
                    proxy.transform.position = hitBounds.center;
                    var collider = proxy.AddComponent<BoxCollider>();
                    collider.isTrigger = true;
                    collider.size = new Vector3(Mathf.Max(.05f, hitBounds.size.x), Mathf.Max(.05f, hitBounds.size.y), Mathf.Max(.05f, hitBounds.size.z));
                    building.Colliders.Add(collider);
                }
                buildings.Add(building);
            }
        }

        private static Bounds RendererBounds(Transform root, out bool found)
        {
            var bounds = new Bounds(root.position, Vector3.zero);
            found = false;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return bounds;
        }

        private void CreateOutline()
        {
            var node = new GameObject("CityViewport.Selection");
            node.layer = CityLayer;
            node.transform.SetParent(WorldRoot.transform, false);
            outline = node.AddComponent<LineRenderer>();
            outline.useWorldSpace = true;
            outline.positionCount = 4;
            outline.loop = true;
            outline.startColor = outline.endColor = new Color(1, .82f, .2f, 1);
            outline.numCornerVertices = 3;
            outline.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            outline.receiveShadows = false;
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color") ?? Shader.Find("UI/Default");
            if (shader != null)
            {
                outlineMaterial = new Material(shader) { name = "CityViewport.Selection.Material", hideFlags = HideFlags.DontSave };
                if (outlineMaterial.HasProperty("_Color")) outlineMaterial.color = new Color(1, .82f, .2f, 1);
                outline.sharedMaterial = outlineMaterial;
            }
            outline.enabled = false;
        }

        private void LateUpdate()
        {
            if (IsReady) ResizeTexture();
        }

        private void ResizeTexture()
        {
            inputRegion.GetWorldCorners(regionCorners);
            var canvas = inputRegion.GetComponentInParent<Canvas>();
            var canvasCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            var a = RectTransformUtility.WorldToScreenPoint(canvasCamera, regionCorners[0]);
            var b = RectTransformUtility.WorldToScreenPoint(canvasCamera, regionCorners[1]);
            var c = RectTransformUtility.WorldToScreenPoint(canvasCamera, regionCorners[3]);
            var pixelWidth = Vector2.Distance(a, c);
            var pixelHeight = Vector2.Distance(a, b);
            var resolutionScale = Mathf.Min(1, 4096 / Mathf.Max(1, pixelWidth, pixelHeight));
            var width = Mathf.Max(1, Mathf.RoundToInt(pixelWidth * resolutionScale));
            var height = Mathf.Max(1, Mathf.RoundToInt(pixelHeight * resolutionScale));
            if (texture != null && texture.width == width && texture.height == height) return;
            var zoomRatio = initialZoom > 0 ? ViewCamera.orthographicSize / initialZoom : 1;
            var oldTexture = texture;
            texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "CityViewport.Texture." + GetInstanceID(), hideFlags = HideFlags.DontSave
            };
            texture.Create();
            ViewCamera.targetTexture = texture;
            ViewCamera.aspect = (float)width / height;
            output.texture = texture;
            output.uvRect = new Rect(0, 0, 1, 1);
            initialZoom = Mathf.Max(.01f, CityBounds.extents.z, CityBounds.extents.x / ViewCamera.aspect) * 1.08f;
            ViewCamera.orthographicSize = Mathf.Clamp(initialZoom * zoomRatio, MinimumZoom, MaximumZoom);
            if (oldTexture != null) { oldTexture.Release(); DestroyOwned(oldTexture); }
            UpdateCamera();
        }

        public void ResetView()
        {
            if (!IsReady) return;
            center = CityBounds.center;
            ViewCamera.orthographicSize = fillViewport ? MaximumZoom * .9f : initialZoom*defaultZoomRatio;
            UpdateCamera();
        }

        public void FocusBuilding(int id)
        {
            if (!IsReady) return;
            foreach (var building in buildings)
            {
                if (building.Id != id) continue;
                center = building.Bounds.center;
                ViewCamera.orthographicSize = Mathf.Clamp(Mathf.Max(building.Bounds.extents.z,
                    building.Bounds.extents.x / ViewCamera.aspect) * 1.8f, MinimumZoom, Mathf.Min(initialZoom, MaximumZoom));
                UpdateCamera();
                ShowSelection(building);
                return;
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!IsReady || eventData.button != PointerEventData.InputButton.Left || !Contains(eventData)) return;
            pointers[eventData.pointerId] = new PointerState { Origin = eventData.position, Position = eventData.position, EventCamera = eventData.pressEventCamera };
            if (pointers.Count > 1)
            {
                foreach (var state in pointers.Values) state.SuppressClick = true;
                previousPinchDistance = PinchDistance();
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!IsReady || !pointers.TryGetValue(eventData.pointerId, out var state)) return;
            var before = state.Position;
            state.Position = eventData.position;
            if ((state.Position - state.Origin).sqrMagnitude > DragThresholdSquared) state.SuppressClick = true;
            if (pointers.Count > 1)
            {
                var distance = PinchDistance();
                if (previousPinchDistance > 1 && distance > 1) Zoom(previousPinchDistance / distance, eventData.position, state.EventCamera);
                previousPinchDistance = distance;
            }
            else if (state.SuppressClick)
            {
                var previous = Uv(before, state.EventCamera);
                var current = Uv(eventData.position, state.EventCamera);
                center += new Vector3((previous.x - current.x) * ViewCamera.orthographicSize * 2 * ViewCamera.aspect, 0,
                    (previous.y - current.y) * ViewCamera.orthographicSize * 2);
                UpdateCamera();
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!pointers.TryGetValue(eventData.pointerId, out var state)) return;
            var click = IsReady && !state.SuppressClick && pointers.Count == 1 &&
                (eventData.position - state.Origin).sqrMagnitude <= DragThresholdSquared && Contains(eventData);
            pointers.Remove(eventData.pointerId);
            previousPinchDistance = 0;
            if (click) SelectAt(Uv(eventData.position, state.EventCamera));
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (IsReady && Contains(eventData)) Zoom(Mathf.Exp(-eventData.scrollDelta.y * .12f), eventData.position, eventData.enterEventCamera);
        }

        private bool Contains(PointerEventData data) => RectTransformUtility.RectangleContainsScreenPoint(inputRegion, data.position, data.pressEventCamera ?? data.enterEventCamera);

        private Vector2 Uv(Vector2 position, Camera eventCamera)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(inputRegion, position, eventCamera, out var local);
            var rect = inputRegion.rect;
            return new Vector2(Mathf.Clamp01((local.x - rect.xMin) / Mathf.Max(1, rect.width)), Mathf.Clamp01((local.y - rect.yMin) / Mathf.Max(1, rect.height)));
        }

        private float PinchDistance()
        {
            var first = Vector2.zero;
            bool found = false;
            foreach (var pointer in pointers.Values)
            {
                if (found) return Vector2.Distance(first, pointer.Position);
                first = pointer.Position; found = true;
            }
            return 0;
        }

        private void Zoom(float ratio, Vector2 position, Camera eventCamera)
        {
            var uv = Uv(position, eventCamera) - Vector2.one * .5f;
            var oldSize = ViewCamera.orthographicSize;
            var newSize = Mathf.Clamp(oldSize * ratio, MinimumZoom, MaximumZoom);
            center += new Vector3(uv.x * (oldSize - newSize) * 2 * ViewCamera.aspect, 0, uv.y * (oldSize - newSize) * 2);
            ViewCamera.orthographicSize = newSize;
            UpdateCamera();
        }

        private void UpdateCamera()
        {
            var halfHeight = ViewCamera.orthographicSize;
            var halfWidth = halfHeight * ViewCamera.aspect;
            center.x = ClampAxis(center.x, CityBounds.min.x, CityBounds.max.x, halfWidth);
            center.z = ClampAxis(center.z, CityBounds.min.z, CityBounds.max.z, halfHeight);
            center.y = CityBounds.center.y;
            ViewCamera.transform.position = center - ViewCamera.transform.forward * cameraDistance;
        }

        private static float ClampAxis(float value, float min, float max, float extent)
        {
            return max - min <= extent * 2 ? (min + max) * .5f : Mathf.Clamp(value, min + extent, max - extent);
        }

        private void SelectAt(Vector2 uv)
        {
            // Collider.Raycast plus an explicit ownership list cannot select scene/HUD objects.
            Physics.SyncTransforms();
            var ray = ViewCamera.ViewportPointToRay(uv);
            Building selected = null;
            float nearest = float.MaxValue;
            foreach (var building in buildings)
                foreach (var collider in building.Colliders)
                    if (collider != null && collider.Raycast(ray, out var hit, ViewCamera.farClipPlane) && hit.distance < nearest)
                    { selected = building; nearest = hit.distance; }
            if (selected == null) return;
            ShowSelection(selected);
        }

        private void ShowSelection(Building selected)
        {
            SelectedBuildingId = selected.Id;
            var bounds = selected.Bounds;
            var padding = Mathf.Max(.05f, Mathf.Min(bounds.size.x, bounds.size.z) * .05f);
            float y = bounds.max.y + Mathf.Max(.05f, bounds.size.y * .02f);
            outline.startWidth = outline.endWidth = Mathf.Max(.035f, Mathf.Min(bounds.size.x, bounds.size.z) * .025f);
            outline.SetPosition(0, new Vector3(bounds.min.x - padding, y, bounds.min.z - padding));
            outline.SetPosition(1, new Vector3(bounds.max.x + padding, y, bounds.min.z - padding));
            outline.SetPosition(2, new Vector3(bounds.max.x + padding, y, bounds.max.z + padding));
            outline.SetPosition(3, new Vector3(bounds.min.x - padding, y, bounds.max.z + padding));
            outline.enabled = true;
            var label=selected.Id<buildingNames.Length&&!string.IsNullOrEmpty(buildingNames[selected.Id])?buildingNames[selected.Id]:"建筑 "+selected.Id;
            BuildingSelected?.Invoke(selected.Id, label);
        }

        private void Release()
        {
            pointers.Clear(); buildings.Clear(); previousPinchDistance = 0; SelectedBuildingId = 0;
            if (ViewCamera != null) { ViewCamera.enabled = false; ViewCamera.targetTexture = null; }
            if (output != null && output.texture == texture) output.texture = previousTexture;
            if (texture != null) { texture.Release(); DestroyOwned(texture); }
            if (WorldRoot != null) { WorldRoot.SetActive(false); DestroyOwned(WorldRoot); }
            if (outlineMaterial != null) DestroyOwned(outlineMaterial);
            if (Application.isPlaying && renderScene.IsValid() && renderScene.isLoaded) SceneManager.UnloadSceneAsync(renderScene);
            renderScene = default;
            ViewCamera = null; WorldRoot = null; texture = null; outlineMaterial = null; outline = null;
            output = null; previousTexture = null; initialZoom = 0;
        }

        private static void DestroyOwned(UnityEngine.Object value)
        {
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
