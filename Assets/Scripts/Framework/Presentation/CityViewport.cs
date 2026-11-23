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
        [SerializeField] private float referenceDefaultDistance;
        [SerializeField] private bool pickGround;
        [SerializeField] private Vector3 groundFocus;
        [SerializeField] private Bounds groundBounds;
        [SerializeField] private Material postProcessMaterial;

        public void ConfigurePostProcessing(Material value) => postProcessMaterial = value;
        private IOutskirtsClock clock;
        public void SetClock(IOutskirtsClock value)
        {
            clock = value ?? throw new ArgumentNullException(nameof(value));
            if (WorldRoot != null) WorldRoot.GetComponentInChildren<OutskirtsLightingCycle>()?.SetClock(clock);
        }

        public event Action<Vector3> GroundClicked;

        public void ConfigureReferenceDistance(float distance) => referenceDefaultDistance = distance;

        public void ConfigureGroundView(Vector3 focus, Bounds bounds)
        {
            pickGround = true;
            groundFocus = focus;
            groundBounds = bounds;
        }

        public void ShowOverview()
        {
            if (!IsReady) return;
            center = CityBounds.center;
            ViewCamera.orthographicSize = initialZoom;
            UpdateCamera();
            if (pickGround)
            {
                // Perspective enlarges the nearest row; include the whole ground rectangle.
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    bool fits = true;
                    for (int x = 0; x < 2; x++)
                    for (int z = 0; z < 2; z++)
                    {
                        var corner = new Vector3(x == 0 ? CityBounds.min.x : CityBounds.max.x,
                            WorldRoot.transform.position.y, z == 0 ? CityBounds.min.z : CityBounds.max.z);
                        var point = ViewCamera.WorldToViewportPoint(corner);
                        fits &= point.z > 0 && point.x >= .025f && point.x <= .975f && point.y >= .025f && point.y <= .975f;
                    }
                    if (fits) break;
                    float next = Mathf.Min(MaximumZoom, ViewCamera.orthographicSize * 1.1f);
                    if (Mathf.Approximately(next, ViewCamera.orthographicSize)) break;
                    ViewCamera.orthographicSize = next;
                    UpdateCamera();
                }
            }
        }

        public const float OutskirtsSpritePitch = 42f;
        private const float GroundPitch = OutskirtsSpritePitch;
        private float GroundDepthScale => pickGround ? 1f / Mathf.Sin(GroundPitch * Mathf.Deg2Rad) : 1f;
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
        private Scene renderScene;
        private Vector3 center;
        private float initialZoom, cameraDistance, previousPinchDistance;
        private int sceneGeneration;

        public event Action<int, string> BuildingSelected;
        public int SelectedBuildingId { get; private set; }
        public bool IsReady => ViewCamera != null && WorldRoot != null && texture != null;
        public Camera ViewCamera { get; private set; }
        public GameObject WorldRoot { get; private set; }
        public Bounds CityBounds { get; private set; }
        public readonly struct ViewState
        {
            public readonly Vector3 Center;
            public readonly float OrthographicSize;
            public ViewState(Vector3 center,float size){Center=center;OrthographicSize=size;}
        }
        public ViewState CaptureView()=>new ViewState(center,IsReady?ViewCamera.orthographicSize:0);
        public bool TryGetBuildingCenter(int id,out Vector3 value)
        {
            foreach(var building in buildings)if(building.Id==id){value=building.Bounds.center;return true;}
            value=default;return false;
        }
        public void ApplyView(ViewState value)
        {
            if(!IsReady)return;
            center=value.Center;ViewCamera.orthographicSize=Mathf.Clamp(value.OrthographicSize,MinimumZoom,MaximumZoom);UpdateCamera();
        }
        public bool TryGetFocusView(int id,out ViewState value)
        {
            foreach(var building in buildings)
                if(building.Id==id)
                {
                    float zoom=Mathf.Clamp(Mathf.Max(building.Bounds.extents.z,building.Bounds.extents.x/ViewCamera.aspect)*1.8f,MinimumZoom,Mathf.Min(initialZoom,MaximumZoom));
                    value=new ViewState(building.Bounds.center,zoom);return true;
                }
            value=default;return false;
        }

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
            if (clock != null) city.GetComponent<OutskirtsLightingCycle>()?.SetClock(clock);
            foreach (var item in WorldRoot.GetComponentsInChildren<Transform>(true)) item.gameObject.layer = CityLayer;
            var framing = city.transform.Find("BuildingRoot") ?? city.transform;
            CityBounds = RendererBounds(framing, out var found);
            if (!found) CityBounds = new Bounds(WorldRoot.transform.position, Vector3.one * 10);
            if (pickGround && groundBounds.size.sqrMagnitude > 0)
                CityBounds = new Bounds(WorldRoot.transform.TransformPoint(groundBounds.center), groundBounds.size);

            var cameraObject = new GameObject("CityViewport.Camera");
            cameraObject.transform.SetParent(WorldRoot.transform, false);
            ViewCamera = cameraObject.AddComponent<Camera>();
#if UNITY_EDITOR
            ViewCamera.scene = renderScene;
#endif
            ViewCamera.orthographic = !pickGround;
            ViewCamera.fieldOfView = 29.5f;
            // Keep the HYGD reference world-map framing: 42-degree pitch and 29.5-degree perspective FOV.
            ViewCamera.transform.rotation = Quaternion.Euler(pickGround ? GroundPitch : 90, 0, 0);
            if (pickGround)
            {
                // A stale prefab must never flatten a pre-rendered sprite when camera framing changes.
                foreach (var sprite in city.GetComponentsInChildren<SpriteRenderer>(true))
                    sprite.transform.rotation = ViewCamera.transform.rotation;
                foreach (var text in city.GetComponentsInChildren<TextMesh>(true))
                    text.transform.rotation = ViewCamera.transform.rotation;
            }
            ViewCamera.cullingMask = 1 << CityLayer;
            ViewCamera.clearFlags = CameraClearFlags.SolidColor;
            ViewCamera.backgroundColor = new Color(.07f, .11f, .12f, 1);
            ViewCamera.allowHDR = pickGround && postProcessMaterial != null;
            if (ViewCamera.allowHDR)
                cameraObject.AddComponent<OutskirtsPostProcess>().Configure(postProcessMaterial);
            if (pickGround) cameraObject.AddComponent<OutskirtsShadowQuality>();
            ViewCamera.allowMSAA = false;
            cameraDistance = pickGround ? Mathf.Max(CityBounds.size.magnitude * 1.45f, 14) :
                Mathf.Max(CityBounds.size.magnitude * 2, 100);
            ViewCamera.nearClipPlane = .1f;
            ViewCamera.farClipPlane = cameraDistance * 4;
            if (!pickGround) CollectBuildings(framing);
            foreach (var light in city.GetComponentsInChildren<Light>()) light.cullingMask = 1 << CityLayer;
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
            bool preserveReferenceDistance = pickGround && referenceDefaultDistance > 0 && initialZoom > 0;
            float previousZoom = ViewCamera.orthographicSize;
            var zoomRatio = initialZoom > 0 ? previousZoom / initialZoom : 1;
            var oldTexture = texture;
            var format = pickGround && postProcessMaterial != null && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
                ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;
            texture = new RenderTexture(width, height, 24, format)
            {
                name = "CityViewport.Texture." + GetInstanceID(), hideFlags = HideFlags.DontSave
            };
            texture.Create();
            ViewCamera.targetTexture = texture;
            ViewCamera.aspect = (float)width / height;
            output.texture = texture;
            output.uvRect = new Rect(0, 0, 1, 1);
            initialZoom = Mathf.Max(.01f, CityBounds.extents.z, CityBounds.extents.x / ViewCamera.aspect) * 1.08f;
            ViewCamera.orthographicSize = Mathf.Clamp(preserveReferenceDistance ? previousZoom : initialZoom * zoomRatio, MinimumZoom, MaximumZoom);
            if (oldTexture != null) { oldTexture.Release(); DestroyOwned(oldTexture); }
            UpdateCamera();
        }

        public void ResetView()
        {
            if (!IsReady) return;
            center = pickGround ? WorldRoot.transform.TransformPoint(groundFocus) : CityBounds.center;
            float defaultSize = pickGround && referenceDefaultDistance > 0
                ? referenceDefaultDistance * Mathf.Tan(ViewCamera.fieldOfView * .5f * Mathf.Deg2Rad)
                : initialZoom * defaultZoomRatio;
            ViewCamera.orthographicSize = fillViewport ? MaximumZoom * .9f : Mathf.Clamp(defaultSize, MinimumZoom, MaximumZoom);
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
                if (pickGround && TryGroundPoint(previous, out var from) && TryGroundPoint(current, out var to))
                    center += from - to;
                else
                    center += new Vector3((previous.x - current.x) * ViewCamera.orthographicSize * 2 * ViewCamera.aspect, 0,
                        (previous.y - current.y) * ViewCamera.orthographicSize * 2 * GroundDepthScale);
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
            var pointerUv = Uv(position, eventCamera);
            var uv = pointerUv - Vector2.one * .5f;
            var oldSize = ViewCamera.orthographicSize;
            var newSize = Mathf.Clamp(oldSize * ratio, MinimumZoom, MaximumZoom);
            if (pickGround && TryGroundPoint(pointerUv, out var anchor))
            {
                ViewCamera.orthographicSize = newSize;
                UpdateCamera();
                if (TryGroundPoint(pointerUv, out var after)) center += anchor - after;
                UpdateCamera();
                return;
            }
            center += new Vector3(uv.x * (oldSize - newSize) * 2 * ViewCamera.aspect, 0, uv.y * (oldSize - newSize) * 2 * GroundDepthScale);
            ViewCamera.orthographicSize = newSize;
            UpdateCamera();
        }

        private void UpdateCamera()
        {
            var halfHeight = ViewCamera.orthographicSize;
            var halfWidth = halfHeight * ViewCamera.aspect;
            center.x = pickGround ? Mathf.Clamp(center.x, CityBounds.min.x, CityBounds.max.x) :
                ClampAxis(center.x, CityBounds.min.x, CityBounds.max.x, halfWidth);
            center.z = pickGround ? Mathf.Clamp(center.z, CityBounds.min.z, CityBounds.max.z) :
                ClampAxis(center.z, CityBounds.min.z, CityBounds.max.z, halfHeight * GroundDepthScale);
            center.y = pickGround ? WorldRoot.transform.position.y : CityBounds.center.y;
            float distance = !ViewCamera.orthographic ? halfHeight / Mathf.Tan(ViewCamera.fieldOfView * .5f * Mathf.Deg2Rad) : cameraDistance;
            ViewCamera.transform.position = center - ViewCamera.transform.forward * distance;
            if (pickGround) ViewCamera.GetComponent<OutskirtsShadowQuality>()?.Configure(distance);
            if (pickGround) ViewCamera.farClipPlane = Mathf.Max(cameraDistance * 4, distance * 4);
        }

        private bool TryGroundPoint(Vector2 uv, out Vector3 point)
        {
            if (ViewCamera.orthographic)
            {
                point = center + new Vector3((uv.x * 2 - 1) * ViewCamera.orthographicSize * ViewCamera.aspect,
                    0, (uv.y * 2 - 1) * ViewCamera.orthographicSize * GroundDepthScale);
                point.y = WorldRoot.transform.position.y;
                return true;
            }
            // Inverse view-projection loses precision at the isolated render scene's Y=-10000.
            // Build the perspective direction in camera space before adding its world origin.
            float tangent = Mathf.Tan(ViewCamera.fieldOfView * .5f * Mathf.Deg2Rad);
            var direction = new Vector3((uv.x * 2 - 1) * tangent * ViewCamera.aspect,
                (uv.y * 2 - 1) * tangent, 1);
            var ray = new Ray(ViewCamera.transform.position, ViewCamera.transform.TransformDirection(direction));
            var ground = new Plane(Vector3.up, WorldRoot.transform.position);
            if (ground.Raycast(ray, out var distance)) { point = ray.GetPoint(distance); return true; }
            point = default;
            return false;
        }

        private static float ClampAxis(float value, float min, float max, float extent)
        {
            return max - min <= extent * 2 ? (min + max) * .5f : Mathf.Clamp(value, min + extent, max - extent);
        }

        private void SelectAt(Vector2 uv)
        {
            if (pickGround)
            {
                if (TryGroundPoint(uv, out var point))
                    GroundClicked?.Invoke(WorldRoot.transform.InverseTransformPoint(point));
                return;
            }
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
            if (Application.isPlaying && renderScene.IsValid() && renderScene.isLoaded) SceneManager.UnloadSceneAsync(renderScene);
            renderScene = default;
            ViewCamera = null; WorldRoot = null; texture = null;
            output = null; previousTexture = null; initialZoom = 0;
        }

        private static void DestroyOwned(UnityEngine.Object value)
        {
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
