using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;

namespace PappyHUDMaster
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class PappyHUDMasterPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "pappy.valheim.hudmaster";
        public const string PluginName = "Pappy HUD Master";
        public const string PluginVersion = "1.0.0";

        private static PappyHUDMasterPlugin Instance;
        private Harmony harmony;

        // Unity/IMGUI can consume F7 when a text field has keyboard focus.
        // Poll the Windows virtual key directly so F7 always toggles the
        // editor regardless of GUI focus.
        private const int VK_F7 = 0x76;
        private bool f7WasDown;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private sealed class GraphicTarget
        {
            public Graphic Graphic;
            public Color OriginalColor;
        }

        private sealed class HudElement
        {
            public string Name;
            public string PathSuffix;
            public string VisualBoundsRelativePath;
            public bool DefaultEnabled;
            public string[] BackgroundGraphicRelativePaths;
            public bool UseWholeElementOpacity;
            public bool UseMinimapCompositeOpacity;
            public CanvasGroup OpacityCanvasGroup;
            public float OriginalCanvasGroupAlpha = 1f;

            // Minimap transparency is special. Valheim's Custom/mapshader
            // composites terrain/fog/forest/water itself and ignores normal
            // UI alpha. We leave the native material completely untouched,
            // render its output to our own small RenderTexture, then display
            // that texture through a normal RawImage whose alpha works.
            public RawImage NativeMinimapRawImage;
            public RawImage CompositeMinimapRawImage;
            public RenderTexture CompositeMinimapTexture;
            public GameObject CompositeMinimapObject;

            public GameObject CaptureCameraObject;
            public Camera CaptureCamera;
            public GameObject CaptureCanvasObject;
            public Canvas CaptureCanvas;
            public RawImage CaptureMapRawImage;

            public bool MinimapCompositeReady;
            public bool MinimapCompositeFailureLogged;

            public RectTransform Transform;
            public int TransformInstanceId;
            public Vector2 OriginalAnchoredPosition;
            public Vector3 OriginalLocalScale;
            public Vector3 OriginalLocalEuler;
            public bool OriginalCaptured;

            public ConfigEntry<bool> Enabled;
            public ConfigEntry<float> OffsetX;
            public ConfigEntry<float> OffsetY;
            public ConfigEntry<float> Scale;
            public ConfigEntry<float> Rotation;
            public ConfigEntry<float> BackgroundOpacity;

            public string EditX;
            public string EditY;
            public string EditScale;
            public string EditRotation;
            public string EditOpacityPercent;

            public readonly List<GraphicTarget> BackgroundGraphics =
                new List<GraphicTarget>();
        }

        private readonly List<HudElement> elements = new List<HudElement>();

        private ConfigEntry<KeyCode> editorKey;
        private ConfigEntry<float> nudgeAmount;
        private ConfigEntry<float> largeNudgeAmount;

        private bool editorOpen;
        private Rect editorRect = new Rect(30, 30, 680, 820);
        private Vector2 scroll;
        private int selectedIndex;
        private bool initialResolveComplete;
        private float nextInitialResolveTime;

        private GUIStyle titleStyle;
        private GUIStyle selectedStyle;
        private GUIStyle normalStyle;
        private GUIStyle smallStyle;
        private Texture2D outlineTexture;
        private Texture2D editorBackgroundTexture;

        private bool cursorStateCaptured;
        private bool previousCursorVisible;
        private CursorLockMode previousCursorLockMode;

        private bool draggingSelected;
        private Vector2 dragStartParentLocal;
        private float dragStartOffsetX;
        private float dragStartOffsetY;

        private bool textFieldFocused;

        private void Awake()
        {
            Instance = this;

            harmony = new Harmony(PluginGuid);
            harmony.PatchAll();

            editorKey = Config.Bind(
                "General",
                "EditorKey",
                KeyCode.F7,
                "Open or close the Pappy HUD Master editor.");

            nudgeAmount = Config.Bind(
                "General",
                "NudgeAmount",
                5f,
                "Normal movement amount in UI units.");

            largeNudgeAmount = Config.Bind(
                "General",
                "LargeNudgeAmount",
                25f,
                "Movement amount while holding Shift.");

            RegisterElements();
            StartCoroutine(EditorCursorGuard());

            Logger.LogInfo("[Pappy HUD Master] Loaded " + PluginVersion);
            Logger.LogInfo("[Pappy HUD Master] Press F7 to open the HUD editor.");
        }

        private void RegisterElements()
        {
            Add("Hotbar", "/HUD/hudroot/HotKeyBar", true);
            Add("Stamina", "/HUD/hudroot/staminapanel", true);
            Add("Adrenaline", "/HUD/hudroot/adrenalinepanel", true);
            Add("Eitr", "/HUD/hudroot/eitrpanel", true);
            Add("Health + Food", "/HUD/hudroot/healthpanel", true);
            Add("Crosshair", "/HUD/hudroot/crosshair", true);
            Add("Status Effects", "/HUD/hudroot/StatusEffects", true);

            // The actual terrain uses Valheim's Custom/mapshader and does
            // not honor normal Graphic/CanvasGroup alpha. Use a composited
            // render path instead so only the terrain fades.
            AddMinimapCompositeOpacity(
                "Minimap",
                "/HUD/hudroot/MiniMap/small",
                true);

            Add("Guardian Power", "/HUD/hudroot/GuardianPower", true);
            Add("Event Bar", "/HUD/hudroot/EventBar", true);
            Add("Action Progress", "/HUD/hudroot/action_progress", true);

            // Fade only the panel/background graphics, not inventory icons/text.
            Add("Player Inventory", "/Inventory_screen/root/Player", true,
                new string[] { "Bkg", "Darken" });

            Add("Inventory Info", "/Inventory_screen/root/Info", true,
                new string[] { "Bkg", "Darken" });

            // Fade only Crafting background graphics, leaving recipes/text/buttons opaque.
            Add("Crafting Window", "/Inventory_screen/root/Crafting", true,
                new string[] { "Bkg", "Darken" });

            Add("Crafting Recipe List",
                "/Inventory_screen/root/Crafting/RecipeList", false);

            Add("Crafting Item Details",
                "/Inventory_screen/root/Crafting/Decription", false);

            // Valheim 1.0 BuildUIV2 panel background.
            Add("Build Menu", "/HUD/hudroot/BuildUIV2/bar", true,
                new string[] { "SelectionWindow/Background" });

            Add("Boss Health", "/EnemyHud/HudRoot/HudBaseBoss", true);
            Add("Center Message", "/HudMessage/MessageCenter", true);
            Add("Top Left Message", "/TopLeftMessage/root", true);

            // The Enter-to-type chat UI is a completely separate canvas
            // from Valheim's floating/world dialog bubbles.
            AddWithVisualBounds(
                "Chat Window (Enter)",
                "/Chat_box/root",
                true,
                "bkg");

            Add("Chat Input Field", "/Chat_box/root/ChatInput", false);

            // Keep these separately named so they are not confused with the
            // Enter-to-type chat window.
            Add("World Chat Bubble", "/Chat/DialogBox", false);
            Add("NPC Dialog", "/Chat/NpcDialog", true);
            Add("Large NPC Dialog", "/Chat/NpcDialogLarge", true);
        }

        private void Add(string name, string pathSuffix, bool defaultEnabled)
        {
            Add(name, pathSuffix, defaultEnabled, null);
        }

        private void AddWithVisualBounds(
            string name,
            string pathSuffix,
            bool defaultEnabled,
            string visualBoundsRelativePath)
        {
            Add(name, pathSuffix, defaultEnabled, null);

            HudElement element = elements[elements.Count - 1];
            element.VisualBoundsRelativePath = visualBoundsRelativePath;
        }

        private void Add(
            string name,
            string pathSuffix,
            bool defaultEnabled,
            string[] backgroundGraphicRelativePaths)
        {
            string section = "Element - " + name;

            HudElement element = new HudElement();
            element.Name = name;
            element.PathSuffix = pathSuffix;
            element.DefaultEnabled = defaultEnabled;
            element.BackgroundGraphicRelativePaths = backgroundGraphicRelativePaths;

            element.Enabled = Config.Bind(
                section, "Enabled", defaultEnabled,
                "Allow Pappy HUD Master to control this element.");

            element.OffsetX = Config.Bind(
                section, "OffsetX", 0f,
                "Horizontal offset from Valheim's original position.");

            element.OffsetY = Config.Bind(
                section, "OffsetY", 0f,
                "Vertical offset from Valheim's original position.");

            element.Scale = Config.Bind(
                section, "Scale", 1f,
                "Scale multiplier. 1.0 is Valheim's original size.");

            element.Rotation = Config.Bind(
                section, "Rotation", 0f,
                "Additional Z rotation in degrees.");

            if (backgroundGraphicRelativePaths != null &&
                backgroundGraphicRelativePaths.Length > 0)
            {
                element.BackgroundOpacity = Config.Bind(
                    section, "BackgroundOpacity", 1f,
                    "Background opacity from 0.05 to 1.0. Text/icons remain full opacity.");
            }

            SyncEditStrings(element);
            elements.Add(element);
        }

        private void AddWholeOpacity(
            string name,
            string pathSuffix,
            bool defaultEnabled)
        {
            Add(name, pathSuffix, defaultEnabled, null);

            HudElement element = elements[elements.Count - 1];
            element.UseWholeElementOpacity = true;

            string section = "Element - " + name;
            element.BackgroundOpacity = Config.Bind(
                section,
                "BackgroundOpacity",
                1f,
                "Whole-element opacity from 0.05 to 1.0.");

            SyncEditStrings(element);
        }

        private void AddMinimapCompositeOpacity(
            string name,
            string pathSuffix,
            bool defaultEnabled)
        {
            Add(name, pathSuffix, defaultEnabled, null);

            HudElement element = elements[elements.Count - 1];
            element.UseMinimapCompositeOpacity = true;

            string section = "Element - " + name;
            element.BackgroundOpacity = Config.Bind(
                section,
                "BackgroundOpacity",
                1f,
                "Minimap terrain opacity from 0.05 to 1.0.");

            SyncEditStrings(element);
        }

        private void Update()
        {
            if (EditorTogglePressed())
            {
                SetEditorOpen(!editorOpen);
            }

            if (editorOpen)
            {
                ForceEditorCursor();

                if (!textFieldFocused && elements.Count > 0)
                    ProcessKeyboardNudging();
            }

            // IMPORTANT: 0.2.0 enumerated every RectTransform in the entire
            // loaded game every second. On a live Valheim scene that can cause
            // a visible periodic hitch. 0.2.1 resolves the static UI once and
            // then stops scanning unless the user explicitly clicks Refresh.
            if (!initialResolveComplete &&
                Time.unscaledTime >= nextInitialResolveTime)
            {
                nextInitialResolveTime = Time.unscaledTime + 1f;
                TryInitialResolve();
            }
        }

        private bool EditorTogglePressed()
        {
            // The current test platform is Windows. When F7 is configured,
            // use the native keyboard state so an IMGUI TextField cannot
            // swallow the hotkey. Other configured keys still use Unity.
            if (editorKey.Value == KeyCode.F7 &&
                Application.platform == RuntimePlatform.WindowsPlayer)
            {
                bool down =
                    (GetAsyncKeyState(VK_F7) & 0x8000) != 0;

                bool pressed =
                    down && !f7WasDown;

                f7WasDown = down;
                return pressed;
            }

            return Input.GetKeyDown(editorKey.Value);
        }

        private void LateUpdate()
        {
            for (int i = 0; i < elements.Count; i++)
            {
                ApplyElement(elements[i]);
            }
        }

        private IEnumerator EditorCursorGuard()
        {
            WaitForEndOfFrame wait = new WaitForEndOfFrame();

            while (true)
            {
                yield return wait;

                // Valheim updates its own cursor state during the frame.
                // Applying this after the frame's normal updates makes the
                // editor's cursor state win.
                if (editorOpen)
                    ForceEditorCursor();
            }
        }

        private void OnDisable()
        {
            if (editorOpen)
                SetEditorOpen(false);

            CleanupAllMinimapCompositors();

            if (harmony != null)
                harmony.UnpatchSelf();

            if (Instance == this)
                Instance = null;
        }

        private void SetEditorOpen(bool open)
        {
            if (open == editorOpen)
                return;

            editorOpen = open;
            draggingSelected = false;

            if (open)
            {
                if (!cursorStateCaptured)
                {
                    previousCursorVisible = Cursor.visible;
                    previousCursorLockMode = Cursor.lockState;
                    cursorStateCaptured = true;
                }

                ForceEditorCursor();
                Logger.LogInfo("[Pappy HUD Master] Editor opened.");
            }
            else
            {
                if (cursorStateCaptured)
                {
                    Cursor.visible = previousCursorVisible;
                    Cursor.lockState = previousCursorLockMode;
                    cursorStateCaptured = false;
                }

                Config.Save();
                Logger.LogInfo("[Pappy HUD Master] Editor closed.");
            }
        }

        private static void ForceEditorCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void TryInitialResolve()
        {
            // Do not perform a large UI scan on the main menu.
            GameObject hud =
                GameObject.Find("_GameMain/LoadingGUI/PixelFix/IngameGui/HUD");

            if (hud == null)
                return;

            int found = ResolveAllElementsOnce();

            // All of the vanilla template UI is normally present together.
            // 18+ means the in-game UI hierarchy is ready enough that we can
            // stop scanning permanently for this session.
            if (found >= 18)
            {
                initialResolveComplete = true;
                Logger.LogInfo(
                    "[Pappy HUD Master] Initial UI resolve complete: " +
                    found + "/" + elements.Count +
                    ". Periodic full-scene scanning is disabled.");
            }
        }

        private int ResolveAllElementsOnce()
        {
            RectTransform[] all =
                Resources.FindObjectsOfTypeAll<RectTransform>();

            RectTransform[] best =
                new RectTransform[elements.Count];

            bool[] bestActive =
                new bool[elements.Count];

            for (int r = 0; r < all.Length; r++)
            {
                RectTransform rt = all[r];

                if (rt == null)
                    continue;

                string normalizedName =
                    NormalizeCloneNames(rt.name);

                string path = null;

                for (int i = 0; i < elements.Count; i++)
                {
                    HudElement element = elements[i];
                    string leaf = GetLeafName(element.PathSuffix);

                    if (!string.Equals(
                        normalizedName,
                        leaf,
                        StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (path == null)
                    {
                        path =
                            NormalizeCloneNames(
                                GetTransformPath(rt));
                    }

                    if (!path.EndsWith(
                        element.PathSuffix,
                        StringComparison.Ordinal))
                    {
                        continue;
                    }

                    bool active =
                        rt.gameObject.activeInHierarchy;

                    if (best[i] == null ||
                        (active && !bestActive[i]))
                    {
                        best[i] = rt;
                        bestActive[i] = active;
                    }
                }
            }

            int found = 0;

            for (int i = 0; i < elements.Count; i++)
            {
                if (best[i] == null)
                    continue;

                found++;
                AttachTransform(elements[i], best[i]);
            }

            return found;
        }

        private void RefreshSelectedElement()
        {
            if (elements.Count == 0)
                return;

            HudElement element = elements[selectedIndex];

            RectTransform[] all =
                Resources.FindObjectsOfTypeAll<RectTransform>();

            RectTransform best =
                FindBestMatch(all, element.PathSuffix);

            if (best != null)
            {
                AttachTransform(element, best);
                Logger.LogInfo(
                    "[Pappy HUD Master] Manual refresh resolved " +
                    element.Name + ".");
            }
            else
            {
                Logger.LogWarning(
                    "[Pappy HUD Master] Manual refresh could not find " +
                    element.Name + ".");
            }
        }

        private static string GetLeafName(string suffix)
        {
            int index = suffix.LastIndexOf('/');

            if (index < 0 || index == suffix.Length - 1)
                return suffix;

            return suffix.Substring(index + 1);
        }

        private void AttachTransform(HudElement element, RectTransform found)
        {
            if (element.UseMinimapCompositeOpacity)
                CleanupMinimapCompositor(element);

            element.Transform = found;
            element.TransformInstanceId = found.GetInstanceID();
            element.OriginalAnchoredPosition = found.anchoredPosition;
            element.OriginalLocalScale = found.localScale;
            element.OriginalLocalEuler = found.localEulerAngles;
            element.OriginalCaptured = true;

            element.BackgroundGraphics.Clear();
            element.OpacityCanvasGroup = null;
            ResolveBackgroundGraphics(element);
            ResolveWholeElementOpacity(element);
            ResolveMinimapCompositor(element);

            Logger.LogInfo(
                "[Pappy HUD Master] Found " + element.Name +
                " -> " + GetTransformPath(found) +
                (found.gameObject.activeInHierarchy ? " [ACTIVE]" : " [inactive]"));
        }

        private static RectTransform FindBestMatch(
            RectTransform[] all,
            string suffix)
        {
            RectTransform inactiveMatch = null;

            for (int i = 0; i < all.Length; i++)
            {
                RectTransform rt = all[i];

                if (rt == null)
                    continue;

                if (!string.Equals(
                    NormalizeCloneNames(rt.name),
                    GetLeafName(suffix),
                    StringComparison.Ordinal))
                {
                    continue;
                }

                string path =
                    NormalizeCloneNames(GetTransformPath(rt));

                if (!path.EndsWith(suffix, StringComparison.Ordinal))
                    continue;

                if (rt.gameObject.activeInHierarchy)
                    return rt;

                if (inactiveMatch == null)
                    inactiveMatch = rt;
            }

            return inactiveMatch;
        }

        private static string NormalizeCloneNames(string path)
        {
            return path.Replace("(Clone)", "");
        }

        private void ResolveBackgroundGraphics(HudElement element)
        {
            if (element.Transform == null ||
                element.BackgroundGraphicRelativePaths == null)
                return;

            Graphic[] graphics =
                element.Transform.GetComponentsInChildren<Graphic>(true);

            for (int p = 0;
                 p < element.BackgroundGraphicRelativePaths.Length;
                 p++)
            {
                string relative =
                    element.BackgroundGraphicRelativePaths[p];

                string wanted =
                    NormalizeCloneNames(
                        GetTransformPath(element.Transform) + "/" + relative);

                for (int i = 0; i < graphics.Length; i++)
                {
                    Graphic graphic = graphics[i];

                    if (graphic == null)
                        continue;

                    string actual =
                        NormalizeCloneNames(GetTransformPath(graphic.transform));

                    if (actual == wanted)
                    {
                        GraphicTarget target = new GraphicTarget();
                        target.Graphic = graphic;
                        target.OriginalColor = graphic.color;
                        element.BackgroundGraphics.Add(target);

                        Logger.LogInfo(
                            "[Pappy HUD Master] Opacity target for " +
                            element.Name + " -> " + actual);
                        break;
                    }
                }
            }
        }

        private void ResolveWholeElementOpacity(HudElement element)
        {
            if (element == null ||
                element.Transform == null ||
                !element.UseWholeElementOpacity ||
                element.BackgroundOpacity == null)
            {
                return;
            }

            CanvasGroup group =
                element.Transform.GetComponent<CanvasGroup>();

            if (group == null)
                group = element.Transform.gameObject.AddComponent<CanvasGroup>();

            element.OpacityCanvasGroup = group;
            element.OriginalCanvasGroupAlpha = group.alpha;

            // Preserve normal minimap interaction.
            group.interactable = true;
            group.blocksRaycasts = true;

            Logger.LogInfo(
                "[Pappy HUD Master] Whole-element opacity target for " +
                element.Name + " -> " +
                GetTransformPath(element.Transform));
        }

        private void ResolveMinimapCompositor(HudElement element)
        {
            if (element == null ||
                element.Transform == null ||
                !element.UseMinimapCompositeOpacity)
            {
                return;
            }

            Transform mapTransform = element.Transform.Find("map");

            if (mapTransform == null)
            {
                Logger.LogWarning(
                    "[Pappy HUD Master] Minimap compositor: small/map was not found.");
                return;
            }

            RawImage native =
                mapTransform.GetComponent<RawImage>();

            if (native == null)
            {
                Logger.LogWarning(
                    "[Pappy HUD Master] Minimap compositor: native RawImage was not found.");
                return;
            }

            element.NativeMinimapRawImage = native;

            // Visible overlay that sits directly above Valheim's terrain
            // RawImage, but below map pins / player / wind markers.
            GameObject overlay =
                new GameObject(
                    "PappyHUDMaster_MinimapTerrain",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(RawImage));

            RectTransform overlayRect =
                overlay.GetComponent<RectTransform>();

            overlayRect.SetParent(mapTransform, false);
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.pivot = new Vector2(0.5f, 0.5f);
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;
            overlayRect.localScale = Vector3.one;
            overlayRect.localRotation = Quaternion.identity;
            overlayRect.SetAsFirstSibling();

            RawImage composite =
                overlay.GetComponent<RawImage>();

            composite.raycastTarget = false;
            composite.color = Color.white;

            // Render larger than the actual small-map display so the capture
            // itself cannot be the source of softness.
            RenderTexture renderTexture =
                new RenderTexture(
                    512,
                    512,
                    0,
                    RenderTextureFormat.ARGB32);

            renderTexture.name =
                "PappyHUDMaster_MinimapComposite";

            renderTexture.filterMode = FilterMode.Bilinear;
            renderTexture.wrapMode = TextureWrapMode.Clamp;
            renderTexture.Create();

            composite.texture = renderTexture;

            // 0.3.1 used Graphics.Blit(material). That executes Valheim's
            // map shader as a generic fullscreen blit, which is NOT the same
            // vertex/UV path used by a Unity UI RawImage. The result was the
            // smeared/blurred map seen in the test screenshots.
            //
            // 0.3.2 instead renders a real RawImage on its own tiny
            // ScreenSpaceCamera Canvas. That gives Custom/mapshader the same
            // kind of UI geometry/UV data it receives in Valheim's native
            // minimap, while still producing a texture whose final alpha we
            // can control normally.
            const int captureLayer = 31;

            GameObject cameraObject =
                new GameObject(
                    "PappyHUDMaster_MinimapCaptureCamera",
                    typeof(Camera));

            cameraObject.layer = captureLayer;

            Camera captureCamera =
                cameraObject.GetComponent<Camera>();

            captureCamera.enabled = false;
            captureCamera.clearFlags = CameraClearFlags.SolidColor;
            captureCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            captureCamera.cullingMask = 1 << captureLayer;
            captureCamera.targetTexture = renderTexture;
            captureCamera.nearClipPlane = 0.01f;
            captureCamera.farClipPlane = 10f;

            GameObject canvasObject =
                new GameObject(
                    "PappyHUDMaster_MinimapCaptureCanvas",
                    typeof(RectTransform),
                    typeof(Canvas));

            canvasObject.layer = captureLayer;

            Canvas captureCanvas =
                canvasObject.GetComponent<Canvas>();

            captureCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            captureCanvas.worldCamera = captureCamera;
            captureCanvas.planeDistance = 1f;
            captureCanvas.pixelPerfect = false;
            captureCanvas.sortingOrder = 0;

            GameObject captureMapObject =
                new GameObject(
                    "Map",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(RawImage));

            captureMapObject.layer = captureLayer;

            RectTransform captureRect =
                captureMapObject.GetComponent<RectTransform>();

            captureRect.SetParent(canvasObject.transform, false);
            captureRect.anchorMin = Vector2.zero;
            captureRect.anchorMax = Vector2.one;
            captureRect.pivot = new Vector2(0.5f, 0.5f);
            captureRect.offsetMin = Vector2.zero;
            captureRect.offsetMax = Vector2.zero;
            captureRect.localScale = Vector3.one;
            captureRect.localRotation = Quaternion.identity;

            RawImage captureRawImage =
                captureMapObject.GetComponent<RawImage>();

            captureRawImage.raycastTarget = false;
            captureRawImage.color = native.color;
            captureRawImage.texture = GetMinimapCaptureSource(native);
            captureRawImage.material = native.material;
            captureRawImage.uvRect = native.uvRect;

            element.CompositeMinimapObject = overlay;
            element.CompositeMinimapRawImage = composite;
            element.CompositeMinimapTexture = renderTexture;
            element.CaptureCameraObject = cameraObject;
            element.CaptureCamera = captureCamera;
            element.CaptureCanvasObject = canvasObject;
            element.CaptureCanvas = captureCanvas;
            element.CaptureMapRawImage = captureRawImage;
            element.MinimapCompositeReady = true;
            element.MinimapCompositeFailureLogged = false;

            composite.enabled = false;
            native.enabled = true;

            Material material = native.material;
            string shaderName = "<none>";

            if (material != null && material.shader != null)
                shaderName = material.shader.name;

            Texture initialSource = GetMinimapCaptureSource(native);
            string sourceInfo = "<not assigned yet>";

            if (initialSource != null)
            {
                sourceInfo =
                    initialSource.name +
                    " (" +
                    initialSource.width +
                    "x" +
                    initialSource.height +
                    ")";
            }

            Logger.LogInfo(
                "[Pappy HUD Master] Minimap UI-capture compositor ready. Native shader: " +
                shaderName +
                " | capture source: " +
                sourceInfo +
                ". Native material is NOT replaced or modified.");
        }

        private static Texture GetMinimapCaptureSource(RawImage native)
        {
            if (native == null)
                return null;

            // Valheim 1.0's small-map RawImage can have RawImage.texture == null
            // while Custom/mapshader receives its live terrain texture through
            // the material's _MainTex slot. That is why 0.3.2 always fell back
            // to the untouched native map and appeared to ignore opacity.
            if (native.texture != null)
                return native.texture;

            Material material = native.material;

            if (material != null)
            {
                if (material.HasProperty("_MainTex"))
                {
                    Texture mainTex = material.GetTexture("_MainTex");

                    if (mainTex != null)
                        return mainTex;
                }

                if (material.mainTexture != null)
                    return material.mainTexture;
            }

            return null;
        }

        private void UpdateMinimapComposite(
            HudElement element,
            float opacity)
        {
            if (element == null ||
                !element.UseMinimapCompositeOpacity ||
                !element.MinimapCompositeReady)
            {
                return;
            }

            RawImage native =
                element.NativeMinimapRawImage;

            RawImage composite =
                element.CompositeMinimapRawImage;

            RenderTexture target =
                element.CompositeMinimapTexture;

            Camera captureCamera =
                element.CaptureCamera;

            RawImage captureRawImage =
                element.CaptureMapRawImage;

            if (native == null ||
                composite == null ||
                target == null ||
                captureCamera == null ||
                captureRawImage == null)
            {
                element.MinimapCompositeReady = false;
                return;
            }

            // 100% always returns to Valheim's untouched native renderer.
            if (opacity >= 0.999f)
            {
                native.enabled = true;
                composite.enabled = false;
                return;
            }

            Material nativeMaterial = native.material;
            Texture captureSource = GetMinimapCaptureSource(native);

            if (nativeMaterial == null ||
                captureSource == null)
            {
                native.enabled = true;
                composite.enabled = false;

                if (!element.MinimapCompositeFailureLogged)
                {
                    element.MinimapCompositeFailureLogged = true;
                    Logger.LogWarning(
                        "[Pappy HUD Master] Minimap UI-capture compositor: map material or _MainTex source unavailable.");
                }

                return;
            }

            try
            {
                // Keep the offscreen UI copy synchronized with Valheim's
                // CURRENT live map material and its actual _MainTex source.
                // We only reference these objects; we never replace or mutate
                // Valheim's native minimap material.
                captureRawImage.texture = captureSource;
                captureRawImage.material = nativeMaterial;
                captureRawImage.uvRect = native.uvRect;
                captureRawImage.color = native.color;

                captureCamera.Render();

                composite.texture = target;
                composite.uvRect = new Rect(0f, 0f, 1f, 1f);

                Color color = Color.white;
                color.a = opacity;
                composite.color = color;

                composite.enabled = true;
                native.enabled = false;
                element.MinimapCompositeFailureLogged = false;
            }
            catch (Exception ex)
            {
                native.enabled = true;
                composite.enabled = false;

                if (!element.MinimapCompositeFailureLogged)
                {
                    element.MinimapCompositeFailureLogged = true;
                    Logger.LogWarning(
                        "[Pappy HUD Master] Minimap UI-capture compositor failed; restored native map. " +
                        ex.Message);
                }
            }
        }

        private void CleanupAllMinimapCompositors()
        {
            for (int i = 0; i < elements.Count; i++)
            {
                HudElement element = elements[i];

                if (element != null &&
                    element.UseMinimapCompositeOpacity)
                {
                    CleanupMinimapCompositor(element);
                }
            }
        }

        private void CleanupMinimapCompositor(HudElement element)
        {
            if (element == null)
                return;

            if (element.NativeMinimapRawImage != null)
                element.NativeMinimapRawImage.enabled = true;

            if (element.CompositeMinimapRawImage != null)
                element.CompositeMinimapRawImage.enabled = false;

            if (element.CaptureCamera != null)
                element.CaptureCamera.targetTexture = null;

            if (element.CompositeMinimapTexture != null)
            {
                try
                {
                    if (element.CompositeMinimapTexture.IsCreated())
                        element.CompositeMinimapTexture.Release();
                }
                catch
                {
                }

                Destroy(element.CompositeMinimapTexture);
            }

            if (element.CaptureCanvasObject != null)
                Destroy(element.CaptureCanvasObject);

            if (element.CaptureCameraObject != null)
                Destroy(element.CaptureCameraObject);

            if (element.CompositeMinimapObject != null)
                Destroy(element.CompositeMinimapObject);

            element.NativeMinimapRawImage = null;
            element.CompositeMinimapRawImage = null;
            element.CompositeMinimapTexture = null;
            element.CompositeMinimapObject = null;
            element.CaptureCameraObject = null;
            element.CaptureCamera = null;
            element.CaptureCanvasObject = null;
            element.CaptureCanvas = null;
            element.CaptureMapRawImage = null;
            element.MinimapCompositeReady = false;
            element.MinimapCompositeFailureLogged = false;
        }

        private void ApplyElement(HudElement element)
        {
            if (element == null ||
                element.Transform == null ||
                !element.OriginalCaptured)
                return;

            try
            {
                if (element.Enabled.Value)
                {
                    RectTransform rt = element.Transform;

                    rt.anchoredPosition =
                        element.OriginalAnchoredPosition +
                        new Vector2(
                            element.OffsetX.Value,
                            element.OffsetY.Value);

                    float scale =
                        Mathf.Clamp(element.Scale.Value, 0.25f, 4f);

                    rt.localScale = new Vector3(
                        element.OriginalLocalScale.x * scale,
                        element.OriginalLocalScale.y * scale,
                        element.OriginalLocalScale.z);

                    Vector3 euler = element.OriginalLocalEuler;
                    euler.z =
                        element.OriginalLocalEuler.z +
                        element.Rotation.Value;
                    rt.localEulerAngles = euler;
                }

                ApplyBackgroundOpacity(element);
            }
            catch
            {
                element.Transform = null;
                element.OriginalCaptured = false;
                element.BackgroundGraphics.Clear();
            }
        }

        private static void ApplyBackgroundOpacity(HudElement element)
        {
            if (element.BackgroundOpacity == null)
                return;

            float opacity =
                Mathf.Clamp(element.BackgroundOpacity.Value, 0.05f, 1f);

            if (element.UseWholeElementOpacity &&
                element.OpacityCanvasGroup != null)
            {
                element.OpacityCanvasGroup.alpha =
                    element.OriginalCanvasGroupAlpha * opacity;
            }

            if (element.UseMinimapCompositeOpacity &&
                Instance != null)
            {
                Instance.UpdateMinimapComposite(
                    element,
                    opacity);
            }

            for (int i = 0;
                 i < element.BackgroundGraphics.Count;
                 i++)
            {
                GraphicTarget target =
                    element.BackgroundGraphics[i];

                if (target == null ||
                    target.Graphic == null)
                {
                    continue;
                }

                Color color = target.OriginalColor;
                color.a =
                    target.OriginalColor.a * opacity;
                target.Graphic.color = color;
            }
        }

        private void ProcessKeyboardNudging()
        {
            HudElement element = elements[selectedIndex];

            float amount =
                Input.GetKey(KeyCode.LeftShift) ||
                Input.GetKey(KeyCode.RightShift)
                ? largeNudgeAmount.Value
                : nudgeAmount.Value;

            bool changed = false;

            if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                element.OffsetX.Value -= amount;
                changed = true;
            }

            if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                element.OffsetX.Value += amount;
                changed = true;
            }

            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                element.OffsetY.Value += amount;
                changed = true;
            }

            if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                element.OffsetY.Value -= amount;
                changed = true;
            }

            if (Input.GetKeyDown(KeyCode.Equals) ||
                Input.GetKeyDown(KeyCode.KeypadPlus))
            {
                element.Scale.Value =
                    Mathf.Clamp(
                        element.Scale.Value + 0.05f,
                        0.25f,
                        4f);
                changed = true;
            }

            if (Input.GetKeyDown(KeyCode.Minus) ||
                Input.GetKeyDown(KeyCode.KeypadMinus))
            {
                element.Scale.Value =
                    Mathf.Clamp(
                        element.Scale.Value - 0.05f,
                        0.25f,
                        4f);
                changed = true;
            }

            if (Input.GetKeyDown(KeyCode.Q))
            {
                element.Rotation.Value -= 5f;
                changed = true;
            }

            if (Input.GetKeyDown(KeyCode.E))
            {
                element.Rotation.Value += 5f;
                changed = true;
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                ResetElement(element);
                changed = true;
            }

            if (changed)
            {
                SyncEditStrings(element);
                Config.Save();
            }
        }

        private void OnGUI()
        {
            EnsureStyles();

            if (!editorOpen)
                return;

            ForceEditorCursor();
            textFieldFocused = false;

            Rect selectedRect;
            bool hasSelectedRect = TryGetSelectedScreenRect(out selectedRect);

            if (hasSelectedRect)
            {
                DrawOutline(selectedRect, 2f);
                HandleSelectedDrag(selectedRect);
            }

            editorRect = GUI.Window(
                847211,
                editorRect,
                DrawEditorWindow,
                "");
        }

        private void HandleSelectedDrag(Rect selectedRect)
        {
            if (elements.Count == 0)
                return;

            HudElement element = elements[selectedIndex];

            if (element.Transform == null ||
                !element.Transform.gameObject.activeInHierarchy ||
                !element.Enabled.Value)
            {
                draggingSelected = false;
                return;
            }

            Rect handle = new Rect(
                selectedRect.x,
                Mathf.Max(0f, selectedRect.y - 24f),
                Mathf.Max(100f, Mathf.Min(selectedRect.width, 180f)),
                22f);

            GUI.Box(handle, "DRAG " + element.Name, smallStyle);

            Event ev = Event.current;

            if (ev == null)
                return;

            // Never start an element drag when clicking inside the editor window.
            if (!draggingSelected &&
                editorRect.Contains(ev.mousePosition))
                return;

            if (ev.type == EventType.MouseDown &&
                ev.button == 0 &&
                handle.Contains(ev.mousePosition))
            {
                Vector2 local;

                if (ScreenPointToParentLocal(
                    element.Transform,
                    ev.mousePosition,
                    out local))
                {
                    dragStartParentLocal = local;
                    dragStartOffsetX = element.OffsetX.Value;
                    dragStartOffsetY = element.OffsetY.Value;
                    draggingSelected = true;
                    ev.Use();
                }
            }
            else if (ev.type == EventType.MouseDrag &&
                     ev.button == 0 &&
                     draggingSelected)
            {
                Vector2 local;

                if (ScreenPointToParentLocal(
                    element.Transform,
                    ev.mousePosition,
                    out local))
                {
                    Vector2 delta = local - dragStartParentLocal;
                    element.OffsetX.Value = dragStartOffsetX + delta.x;
                    element.OffsetY.Value = dragStartOffsetY + delta.y;
                    SyncEditStrings(element);
                }

                ev.Use();
            }
            else if (ev.type == EventType.MouseUp &&
                     ev.button == 0 &&
                     draggingSelected)
            {
                draggingSelected = false;
                SyncEditStrings(element);
                Config.Save();
                ev.Use();
            }
        }

        private static bool ScreenPointToParentLocal(
            RectTransform rt,
            Vector2 guiMousePosition,
            out Vector2 localPoint)
        {
            localPoint = Vector2.zero;

            if (rt == null)
                return false;

            RectTransform parent = rt.parent as RectTransform;

            if (parent == null)
                return false;

            Vector2 screenPoint =
                new Vector2(
                    guiMousePosition.x,
                    Screen.height - guiMousePosition.y);

            Canvas canvas = rt.GetComponentInParent<Canvas>();
            Camera camera = null;

            if (canvas != null &&
                canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                camera = canvas.worldCamera;
            }

            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parent,
                screenPoint,
                camera,
                out localPoint);
        }

        private RectTransform GetVisualBoundsTransform(HudElement element)
        {
            if (element == null || element.Transform == null)
                return null;

            if (!string.IsNullOrEmpty(element.VisualBoundsRelativePath))
            {
                Transform child =
                    element.Transform.Find(
                        element.VisualBoundsRelativePath);

                RectTransform childRect =
                    child as RectTransform;

                if (childRect != null)
                    return childRect;
            }

            return element.Transform;
        }

        private bool TryGetSelectedScreenRect(out Rect rect)
        {
            rect = new Rect();

            if (elements.Count == 0)
                return false;

            HudElement e = elements[selectedIndex];

            if (e.Transform == null ||
                !e.Transform.gameObject.activeInHierarchy)
                return false;

            RectTransform visualTransform =
                GetVisualBoundsTransform(e);

            if (visualTransform == null ||
                !visualTransform.gameObject.activeInHierarchy)
                return false;

            try
            {
                Vector3[] corners = new Vector3[4];
                visualTransform.GetWorldCorners(corners);

                Canvas canvas =
                    visualTransform.GetComponentInParent<Canvas>();

                Camera cam = null;

                if (canvas != null &&
                    canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                {
                    cam = canvas.worldCamera;
                }

                Vector2 p0 =
                    RectTransformUtility.WorldToScreenPoint(
                        cam,
                        corners[0]);

                Vector2 p2 =
                    RectTransformUtility.WorldToScreenPoint(
                        cam,
                        corners[2]);

                float x = Mathf.Min(p0.x, p2.x);
                float y =
                    Screen.height - Mathf.Max(p0.y, p2.y);
                float w = Mathf.Abs(p2.x - p0.x);
                float h = Mathf.Abs(p2.y - p0.y);

                if (w < 2f || h < 2f)
                    return false;

                rect = new Rect(x, y, w, h);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void DrawEditorWindow(int windowId)
        {
            const float titleBarHeight = 24f;

            if (editorBackgroundTexture != null)
            {
                GUI.DrawTexture(
                    new Rect(
                        0f,
                        0f,
                        editorRect.width,
                        editorRect.height),
                    editorBackgroundTexture);
            }

            GUI.Box(
                new Rect(
                    0f,
                    0f,
                    editorRect.width,
                    titleBarHeight),
                "Pappy HUD Master " + PluginVersion);

            if (GUI.Button(
                new Rect(
                    editorRect.width - 23f,
                    2f,
                    20f,
                    20f),
                "X"))
            {
                GUI.FocusControl(null);
                SetEditorOpen(false);
                return;
            }

            GUILayout.Space(titleBarHeight + 2f);

            GUILayout.Label(
                "Valheim 1.0 HUD editor",
                titleStyle);

            GUILayout.Label(
                "F7 closes | X closes | drag the DRAG handle above the selected UI | arrows move | Shift+arrows = 25 | +/- scale | Q/E rotate | R reset");

            GUILayout.Space(6);

            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(255));
            GUILayout.Label("UI Elements");

            scroll = GUILayout.BeginScrollView(
                scroll,
                GUILayout.Width(255),
                GUILayout.Height(690));

            for (int i = 0; i < elements.Count; i++)
            {
                HudElement element = elements[i];

                string state;

                if (element.Transform == null)
                    state = " [waiting]";
                else if (element.Transform.gameObject.activeInHierarchy)
                    state = " [ACTIVE]";
                else
                    state = " [hidden]";

                GUIStyle style =
                    i == selectedIndex ? selectedStyle : normalStyle;

                if (GUILayout.Button(
                    element.Name + state,
                    style,
                    GUILayout.Height(28)))
                {
                    selectedIndex = i;
                    draggingSelected = false;
                    SyncEditStrings(element);
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUILayout.Width(390));

            if (elements.Count > 0)
            {
                HudElement e = elements[selectedIndex];

                GUILayout.Label(e.Name, titleStyle);

                if (e.Transform == null)
                    GUILayout.Label("Status: Waiting for this UI object.");
                else if (e.Transform.gameObject.activeInHierarchy)
                    GUILayout.Label("Status: ACTIVE - drag handle is shown on screen.");
                else
                    GUILayout.Label("Status: Loaded, but Valheim currently has it hidden.");

                if (GUILayout.Button(
                    "REFRESH / FIND ACTIVE COPY",
                    GUILayout.Height(30)))
                {
                    RefreshSelectedElement();
                    SyncEditStrings(e);
                }

                bool newEnabled = GUILayout.Toggle(
                    e.Enabled.Value,
                    "Enable position / scale control");

                if (newEnabled != e.Enabled.Value)
                {
                    e.Enabled.Value = newEnabled;
                    Config.Save();
                }

                GUILayout.Space(8);
                GUILayout.Label("Position - type exact values or use buttons");

                DrawConfigFloatField(
                    e,
                    "X offset",
                    ref e.EditX,
                    e.OffsetX,
                    -10000f,
                    10000f,
                    "x_" + selectedIndex);

                DrawConfigFloatField(
                    e,
                    "Y offset",
                    ref e.EditY,
                    e.OffsetY,
                    -10000f,
                    10000f,
                    "y_" + selectedIndex);

                GUILayout.BeginHorizontal();

                if (GUILayout.Button("Left 25"))
                {
                    e.OffsetX.Value -= 25f;
                    SyncEditStrings(e);
                }

                if (GUILayout.Button("Right 25"))
                {
                    e.OffsetX.Value += 25f;
                    SyncEditStrings(e);
                }

                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();

                if (GUILayout.Button("Down 25"))
                {
                    e.OffsetY.Value -= 25f;
                    SyncEditStrings(e);
                }

                if (GUILayout.Button("Up 25"))
                {
                    e.OffsetY.Value += 25f;
                    SyncEditStrings(e);
                }

                GUILayout.EndHorizontal();

                GUILayout.Space(8);

                DrawConfigFloatField(
                    e,
                    "Scale",
                    ref e.EditScale,
                    e.Scale,
                    0.25f,
                    4f,
                    "scale_" + selectedIndex);

                GUILayout.BeginHorizontal();

                if (GUILayout.Button("- 0.05"))
                {
                    e.Scale.Value =
                        Mathf.Clamp(e.Scale.Value - 0.05f, 0.25f, 4f);
                    SyncEditStrings(e);
                }

                if (GUILayout.Button("+ 0.05"))
                {
                    e.Scale.Value =
                        Mathf.Clamp(e.Scale.Value + 0.05f, 0.25f, 4f);
                    SyncEditStrings(e);
                }

                GUILayout.EndHorizontal();

                GUILayout.Space(8);

                DrawConfigFloatField(
                    e,
                    "Rotation",
                    ref e.EditRotation,
                    e.Rotation,
                    -3600f,
                    3600f,
                    "rotation_" + selectedIndex);

                GUILayout.BeginHorizontal();

                if (GUILayout.Button("- 5 deg"))
                {
                    e.Rotation.Value -= 5f;
                    SyncEditStrings(e);
                }

                if (GUILayout.Button("+ 5 deg"))
                {
                    e.Rotation.Value += 5f;
                    SyncEditStrings(e);
                }

                GUILayout.EndHorizontal();

                if (e.BackgroundOpacity != null)
                {
                    GUILayout.Space(10);
                    GUILayout.Label("Background transparency");

                    DrawOpacityField(e);

                    float oldOpacity =
                        Mathf.Clamp(e.BackgroundOpacity.Value, 0.05f, 1f);

                    float newOpacity = GUILayout.HorizontalSlider(
                        oldOpacity,
                        0.05f,
                        1f);

                    if (Mathf.Abs(newOpacity - oldOpacity) > 0.0001f)
                    {
                        e.BackgroundOpacity.Value = newOpacity;
                        e.EditOpacityPercent =
                            (newOpacity * 100f).ToString(
                                "0",
                                CultureInfo.InvariantCulture);
                    }

                    if (e.UseMinimapCompositeOpacity)
                    {
                        GUILayout.Label(
                            "Experimental map-only transparency: below 100% the native map is rendered through a real offscreen UI canvas, then faded as a normal image. Pins/text remain separate.");
                    }
                    else if (e.UseWholeElementOpacity)
                    {
                        GUILayout.Label(
                            "This fades the complete UI element.");
                    }
                    else
                    {
                        GUILayout.Label(
                            "Only the panel background is faded; text, icons, recipes and buttons stay readable.");
                    }
                }

                GUILayout.Space(12);

                if (GUILayout.Button(
                    "RESET THIS ELEMENT",
                    GUILayout.Height(35)))
                {
                    ResetElement(e);
                }

                if (GUILayout.Button(
                    "SAVE CONFIG",
                    GUILayout.Height(32)))
                {
                    Config.Save();
                }

                GUILayout.Space(8);
                GUILayout.Label("Resolved path:", smallStyle);

                if (e.Transform != null)
                {
                    GUILayout.TextArea(
                        GetTransformPath(e.Transform),
                        GUILayout.Height(55));
                }
                else
                {
                    GUILayout.TextArea(
                        e.PathSuffix,
                        GUILayout.Height(55));
                }
            }

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUI.DragWindow(
                new Rect(0f, 0f, editorRect.width - 26f, 24f));
        }

        private void DrawConfigFloatField(
            HudElement element,
            string label,
            ref string editText,
            ConfigEntry<float> config,
            float min,
            float max,
            string controlName)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(110));

            GUI.SetNextControlName(controlName);

            string newText =
                GUILayout.TextField(
                    editText,
                    GUILayout.Width(115));

            if (GUI.GetNameOfFocusedControl() == controlName)
                textFieldFocused = true;

            if (newText != editText)
            {
                editText = newText;

                float parsed;
                if (TryParseFloat(editText, out parsed))
                {
                    config.Value = Mathf.Clamp(parsed, min, max);
                }
            }

            GUILayout.Label(
                "current: " + config.Value.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture));

            GUILayout.EndHorizontal();
        }

        private void DrawOpacityField(HudElement element)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Opacity %", GUILayout.Width(110));

            string controlName = "opacity_" + selectedIndex;
            GUI.SetNextControlName(controlName);

            string newText =
                GUILayout.TextField(
                    element.EditOpacityPercent,
                    GUILayout.Width(115));

            if (GUI.GetNameOfFocusedControl() == controlName)
                textFieldFocused = true;

            if (newText != element.EditOpacityPercent)
            {
                element.EditOpacityPercent = newText;

                float parsed;
                if (TryParseFloat(newText, out parsed))
                {
                    parsed = Mathf.Clamp(parsed, 5f, 100f);
                    element.BackgroundOpacity.Value = parsed / 100f;
                }
            }

            GUILayout.Label(
                "current: " +
                (element.BackgroundOpacity.Value * 100f).ToString(
                    "0",
                    CultureInfo.InvariantCulture) +
                "%");

            GUILayout.EndHorizontal();
        }

        private static bool TryParseFloat(string text, out float value)
        {
            if (float.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value))
            {
                return true;
            }

            return float.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out value);
        }

        private void ResetElement(HudElement e)
        {
            e.OffsetX.Value = 0f;
            e.OffsetY.Value = 0f;
            e.Scale.Value = 1f;
            e.Rotation.Value = 0f;
            e.Enabled.Value = e.DefaultEnabled;

            if (e.BackgroundOpacity != null)
                e.BackgroundOpacity.Value = 1f;

            SyncEditStrings(e);
            Config.Save();
        }

        private static void SyncEditStrings(HudElement e)
        {
            e.EditX =
                e.OffsetX.Value.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture);

            e.EditY =
                e.OffsetY.Value.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture);

            e.EditScale =
                e.Scale.Value.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture);

            e.EditRotation =
                e.Rotation.Value.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture);

            if (e.BackgroundOpacity != null)
            {
                e.EditOpacityPercent =
                    (e.BackgroundOpacity.Value * 100f).ToString(
                        "0",
                        CultureInfo.InvariantCulture);
            }
        }

        private void DrawOutline(Rect rect, float thickness)
        {
            if (outlineTexture == null)
            {
                outlineTexture = new Texture2D(1, 1);
                outlineTexture.SetPixel(0, 0, Color.white);
                outlineTexture.Apply();
            }

            GUI.DrawTexture(
                new Rect(rect.x, rect.y, rect.width, thickness),
                outlineTexture);

            GUI.DrawTexture(
                new Rect(
                    rect.x,
                    rect.yMax - thickness,
                    rect.width,
                    thickness),
                outlineTexture);

            GUI.DrawTexture(
                new Rect(rect.x, rect.y, thickness, rect.height),
                outlineTexture);

            GUI.DrawTexture(
                new Rect(
                    rect.xMax - thickness,
                    rect.y,
                    thickness,
                    rect.height),
                outlineTexture);
        }

        private void EnsureStyles()
        {
            if (titleStyle != null)
                return;

            editorBackgroundTexture = new Texture2D(1, 1);
            editorBackgroundTexture.SetPixel(
                0,
                0,
                new Color(0.035f, 0.035f, 0.035f, 0.90f));
            editorBackgroundTexture.Apply();

            titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.fontSize = 15;
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.wordWrap = true;

            normalStyle = new GUIStyle(GUI.skin.button);
            normalStyle.alignment = TextAnchor.MiddleLeft;

            selectedStyle = new GUIStyle(GUI.skin.button);
            selectedStyle.alignment = TextAnchor.MiddleLeft;
            selectedStyle.fontStyle = FontStyle.Bold;

            smallStyle = new GUIStyle(GUI.skin.box);
            smallStyle.fontSize = 11;
            smallStyle.alignment = TextAnchor.MiddleCenter;
        }

        private static bool ShouldBlockGameInput()
        {
            return Instance != null && Instance.editorOpen;
        }

        // C# 5-compatible Harmony patches. Valheim 1.0 split several input
        // classes across assembly_valheim / assembly_utils, and some target
        // methods are not public. AccessTools lets us resolve them by name at
        // runtime without requiring direct member access at compile time.

        [HarmonyPatch]
        [HarmonyPriority(Priority.Last)]
        private static class PlayerControllerTakeInputPatch
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                MethodInfo method =
                    AccessTools.Method(typeof(PlayerController), "TakeInput");

                if (method != null)
                    yield return method;
            }

            private static void Postfix(ref bool __result)
            {
                if (ShouldBlockGameInput())
                    __result = false;
            }
        }

        [HarmonyPatch]
        [HarmonyPriority(Priority.Last)]
        private static class TextInputVisiblePatch
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                MethodInfo method =
                    AccessTools.Method(typeof(TextInput), "IsVisible");

                if (method != null)
                    yield return method;
            }

            private static void Postfix(ref bool __result)
            {
                if (ShouldBlockGameInput())
                    __result = true;
            }
        }

        [HarmonyPatch]
        [HarmonyPriority(Priority.First)]
        private static class ZInputMouseButtonPatch
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                string[] names = new string[]
                {
                    "GetMouseButton",
                    "GetMouseButtonDown",
                    "GetMouseButtonUp",
                    "GetRadialTap",
                    "GetRadialMultiTap"
                };

                for (int i = 0; i < names.Length; i++)
                {
                    MethodInfo method =
                        AccessTools.Method(typeof(ZInput), names[i]);

                    if (method != null)
                        yield return method;
                }
            }

            private static bool Prefix(ref bool __result)
            {
                if (!ShouldBlockGameInput())
                    return true;

                __result = false;
                return false;
            }
        }

        [HarmonyPatch]
        [HarmonyPriority(Priority.Last)]
        private static class ZInputFloatPatch
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                string[] names = new string[]
                {
                    "GetMouseScrollWheel",
                    "GetJoyLeftStickX",
                    "GetJoyLeftStickY",
                    "GetJoyRightStickX",
                    "GetJoyRightStickY",
                    "GetJoyRTrigger",
                    "GetJoyLTrigger"
                };

                for (int i = 0; i < names.Length; i++)
                {
                    MethodInfo method =
                        AccessTools.Method(typeof(ZInput), names[i]);

                    if (method != null)
                        yield return method;
                }
            }

            private static void Postfix(ref float __result)
            {
                if (ShouldBlockGameInput())
                    __result = 0f;
            }
        }

        [HarmonyPatch]
        [HarmonyPriority(Priority.Last)]
        private static class ZInputMouseDeltaPatch
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                MethodInfo method =
                    AccessTools.Method(typeof(ZInput), "GetMouseDelta");

                if (method != null)
                    yield return method;
            }

            private static void Postfix(ref Vector2 __result)
            {
                if (ShouldBlockGameInput())
                    __result = Vector2.zero;
            }
        }

        private static string GetTransformPath(Transform t)
        {
            if (t == null)
                return "<null>";

            List<string> parts = new List<string>();
            Transform current = t;

            while (current != null)
            {
                parts.Add(current.name);
                current = current.parent;
            }

            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }
    }
}
