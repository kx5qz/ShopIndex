using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.Networking;

namespace ShopIndex
{
    [BepInPlugin("kx.shopindex", "Shop Index", Version)]
    public sealed class Index : BaseUnityPlugin
    {
        private enum EntrySortMode
        {
            Name,
            OldestToNewest,
            NewestToOldest,
            PriceLowToHigh,
            PriceHighToLow
        }

        private enum Page
        {
            Index,
            Rig,
            Cart,
            Dlcs,
            About
        }

        private const string Version = "4.0.0";
        private const string RepositoryUrl = "https://github.com/kx5qz/ShopIndex";
        private const float WindowPadding = 10f;
        private const float RowHeight = 20f;
        private const float HeaderHeight = 28f;
        private const float TabHeight = 22f;
        private const float ContentTop = 64f;
        private const int OutfitColumns = 7;
        private static readonly Color Accent = new Color(0.55f, 0.8f, 1f);
        private static readonly Color Good = new Color(0.5f, 1f, 0.5f);
        private static readonly Color Bad = new Color(1f, 0.5f, 0.5f);
        private static readonly Color Muted = new Color(0.7f, 0.7f, 0.7f);
        private static readonly Color StageColor = new Color(0f, 0f, 0f, 0.22f);
        private static readonly Color LineColor = new Color(1f, 1f, 1f, 0.1f);
        private static readonly string[] OutfitSquareLabels =
        {
            "Hat", "Face", "Badge", "Chest", "Shirt", "Pants", "Fur",
            "Tag FX", "Paw", "Arms L/R", "Back L/R"
        };
        private static readonly int[][] OutfitSquareSlots =
        {
            new[] { 0 }, new[] { 2 }, new[] { 1 }, new[] { 9 }, new[] { 11 }, new[] { 12 }, new[] { 10 },
            new[] { 15 }, new[] { 7, 8 }, new[] { 3, 4 }, new[] { 5, 6 }
        };
        private static readonly string[] OutfitSquareCategories =
        {
            "Hat", "Face", "Badge", "Chest", "Shirt", "Pants", "Fur",
            "TagEffect", "Paw", "Arms", "Back"
        };
        private const int DockLeftArm = 1;
        private const int DockRightArm = 2;
        private const int DockLeftBack = 8;
        private const int DockRightBack = 16;
        private const string GorillaTagShopUrl = "https://store.steampowered.com/app/1533390/";
        private const string EarlyAccessDlcHeaderUrl = "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/1564750/header.jpg?t=1671127857";
        private const string EarlyAccessDlcApiUrl = "https://store.steampowered.com/api/appdetails?appids=1564750&cc=us&l=en";
        private readonly List<Item> entries = new List<Item>();
        private readonly List<Item> filteredEntries = new List<Item>();
        private readonly List<string> categories = new List<string>();
        private readonly Dictionary<string, Item> entriesById = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> cartIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Item> cartEntries = new List<Item>();
        private int cartTotal;
        private readonly HashSet<string> wornIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Item[] wornSlotEntries = new Item[16];
        private readonly Dictionary<string, string> wornDisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private Rect windowRect = new Rect(70f, 35f, 650f, 680f);
        private Vector2 scrollPosition;
        private Page page;
        private EntrySortMode entrySortMode;
        private string searchText = string.Empty;
        private string wardrobeSearchText = string.Empty;
        private string selectedCategory = "All";
        private bool ownedOnly;
        private bool freeOnly;
        private bool affordableOnly;
        private string wardrobeCategory = "All";
        private bool rigSettingsLoaded;
        private string nameInput = string.Empty;
        private string colorRText = "0";
        private string colorGText = "0";
        private string colorBText = "0";
        private int colorR;
        private int colorG;
        private int colorB;
        private readonly Dictionary<string, int> dockPositionsById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private int shinyRocks = -1;
        private Item purchasingEntry;
        private float purchaseStartedAt;
        private string statusText = string.Empty;
        private float statusUntil;
        private string confirmBuyId;
        private float confirmBuyUntil;
        private bool menuOpen;
        private bool windowDragging;
        private Vector2 windowDragOffset;
        private bool filtersDirty = true;
        private string bundleCatalogStatus = "Waiting on the bundle list...";
        private Sprite earlyAccessDlcSprite;
        private bool earlyAccessDlcImageRequested;
        private bool steamDlcAvailable;
        private bool steamDlcIsFree;
        private string steamDlcPriceText = "Checking...";
        private string steamDlcLastError = string.Empty;
        private float nextRefreshTime;
        private bool stylesReady;
        private GUIStyle titleLabelStyle;
        private GUIStyle balanceStyle;
        private GUIStyle aboutTitleStyle;
        private GUIStyle mutedLabelStyle;
        private GUIStyle smallMutedStyle;
        private GUIStyle centerMutedStyle;
        private GUIStyle sectionLabelStyle;
        private GUIStyle sectionTitleStyle;
        private GUIStyle footerLabelStyle;
        private GUIStyle statusLabelStyle;
        private GUIStyle slotLabelStyle;
        private GUIStyle slotActiveLabelStyle;
        private GUIStyle slotCaptionStyle;
        private GUIStyle overlayLabelStyle;
        private GUIStyle cardNameStyle;
        private GUIStyle rowNameStyle;
        private GUIStyle priceStyle;
        private readonly GUIContent measureContent = new GUIContent();
        private GUIStyle tileNameStyle;
        private GUIStyle tileButtonStyle;
        private GUIStyle tooltipStyle;
        private ConfigEntry<float> windowXConfig;
        private ConfigEntry<float> windowYConfig;
        private ConfigEntry<Key> openKeyConfig;
        private Keyboard openKeyKeyboard;
        private Key openKeyResolved;
        private KeyControl openKeyControl;
        private bool rebindingOpenKey;
        private bool windowPlaced;
        private bool textFieldFocused;
        private string lastDrawError;

        private static readonly BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private void Awake()
        {
            windowXConfig = Config.Bind("Window", "X", -1f, "Horizontal position of the ShopIndex window. -1 puts it in the middle of the screen.");
            windowYConfig = Config.Bind("Window", "Y", -1f, "Vertical position of the ShopIndex window. -1 puts it in the middle of the screen.");
            openKeyConfig = Config.Bind("Controls", "OpenKey", Key.F8, "Key that opens and closes the menu. You can also change it in the About tab.");
        }

        private void Start()
        {
            StartCoroutine(MonitorSteamDlcPrice());
        }

        private void Update()
        {
            if (rebindingOpenKey && (!menuOpen || page != Page.About))
            {
                rebindingOpenKey = false;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (rebindingOpenKey)
                {
                    CaptureOpenKey(keyboard);
                }
                else if (OpenKeyPressed(keyboard))
                {
                    menuOpen = !menuOpen;
                    GUIUtility.keyboardControl = 0;
                    textFieldFocused = false;
                    if (menuOpen)
                    {
                        RefreshEntries();
                    }
                }
            }

            if (menuOpen && Time.unscaledTime >= nextRefreshTime)
            {
                RefreshEntries();
            }

            if (purchasingEntry != null)
            {
                PollPurchase();
            }
        }

        private bool OpenKeyPressed(Keyboard keyboard)
        {
            Key key = openKeyConfig.Value;
            if (openKeyControl == null || keyboard != openKeyKeyboard || key != openKeyResolved)
            {
                openKeyControl = keyboard.allKeys.FirstOrDefault(control => control != null && control.keyCode == key) ?? keyboard.f8Key;
                openKeyKeyboard = keyboard;
                openKeyResolved = key;
            }

            if (!openKeyControl.wasPressedThisFrame)
            {
                return false;
            }

            Key pressed = openKeyControl.keyCode;
            bool functionKey = pressed >= Key.F1 && pressed <= Key.F12;
            return functionKey || !(menuOpen && textFieldFocused);
        }

        private void CaptureOpenKey(Keyboard keyboard)
        {
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                rebindingOpenKey = false;
                return;
            }

            foreach (KeyControl control in keyboard.allKeys)
            {
                if (control != null && control.wasPressedThisFrame)
                {
                    openKeyConfig.Value = control.keyCode;
                    rebindingOpenKey = false;
                    return;
                }
            }
        }

        private string GetOpenKeyName()
        {
            if (openKeyControl != null && openKeyControl.keyCode == openKeyConfig.Value && !string.IsNullOrEmpty(openKeyControl.displayName))
            {
                return openKeyControl.displayName;
            }
            return openKeyConfig.Value.ToString();
        }

        private void OnGUI()
        {
            if (!menuOpen)
            {
                return;
            }

            textFieldFocused = GUIUtility.keyboardControl != 0;

            if (Event.current.type == EventType.Layout)
            {
                RefreshCartSnapshot();
                RefreshWornSnapshot();
                if (filtersDirty)
                {
                    RebuildFilteredEntries();
                }
            }

            EnsureStyles();

            if (!windowPlaced)
            {
                windowPlaced = true;
                bool hasSavedPosition = windowXConfig.Value >= 0f && windowYConfig.Value >= 0f;
                windowRect.x = hasSavedPosition ? windowXConfig.Value : (Screen.width - windowRect.width) / 2f;
                windowRect.y = hasSavedPosition ? windowYConfig.Value : (Screen.height - windowRect.height) / 2f;
            }

            windowRect.x = Mathf.Clamp(windowRect.x, 0f, Mathf.Max(0f, Screen.width - windowRect.width));
            windowRect.y = Mathf.Clamp(windowRect.y, 0f, Mathf.Max(0f, Screen.height - windowRect.height));
            HandleWindowDrag();

            Color previousSelectionColor = GUI.skin.settings.selectionColor;
            GUI.skin.settings.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.6f);
            try
            {
                DrawWindow();
            }
            finally
            {
                GUI.skin.settings.selectionColor = previousSelectionColor;
            }
        }

        private void DrawWindow()
        {
            Fill(new Rect(windowRect.x + 1f, windowRect.y + 1f, windowRect.width - 2f, windowRect.height - 2f), new Color(0f, 0f, 0f, 0.4f));
            GUI.Box(windowRect, GUIContent.none);
            Fill(new Rect(windowRect.x + 1f, windowRect.y + 1f, windowRect.width - 2f, HeaderHeight - 1f), new Color(0f, 0f, 0f, 0.3f));
            Fill(new Rect(windowRect.x + 1f, windowRect.y + HeaderHeight, windowRect.width - 2f, 1f), LineColor);

            GUI.Label(new Rect(windowRect.x, windowRect.y + 4f, windowRect.width, RowHeight), "ShopIndex", titleLabelStyle);
            GUI.Label(
                new Rect(windowRect.x + 10f, windowRect.y + 4f, 220f, RowHeight),
                new GUIContent(
                    "<color=#B3B3B3>Shiny Rocks</color>  <color=#8CCCFF>" + (shinyRocks < 0 ? "..." : shinyRocks.ToString()) + "</color>",
                    "How many Shiny Rocks you have"),
                balanceStyle);

            SafeDraw(DrawPage);

            Color previousBackground = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.9f, 0.08f, 0.08f, 1f);
            if (GUI.Button(new Rect(windowRect.xMax - 25f, windowRect.y + 4f, 20f, 20f), new GUIContent("X", "Close (" + GetOpenKeyName() + ")")))
            {
                menuOpen = false;
                GUIUtility.keyboardControl = 0;
            }
            GUI.backgroundColor = previousBackground;

            DrawTooltip();

            if (Event.current.type == EventType.MouseDown && windowRect.Contains(Event.current.mousePosition))
            {
                GUIUtility.keyboardControl = 0;
            }
        }

        private void DrawPage()
        {
            GUIContent[] tabs =
            {
                new GUIContent("Index", "Everything in the shop"),
                new GUIContent("Rig", "Your name, color and fit"),
                new GUIContent("Cart (" + GetCartCount() + ")", "Stuff in your cart"),
                new GUIContent("DLCs", "Bundles and DLC"),
                new GUIContent("About", "Credits and the menu key")
            };

            const float tabGap = 4f;
            float tabWidth = (windowRect.width - 10f - tabGap * (tabs.Length - 1)) / tabs.Length;
            for (int index = 0; index < tabs.Length; index++)
            {
                Rect tabRect = new Rect(windowRect.x + 5f + index * (tabWidth + tabGap), windowRect.y + HeaderHeight + 5f, tabWidth, TabHeight);
                bool current = page == (Page)index;
                Color previousBackground = GUI.backgroundColor;
                if ((Page)index == Page.Dlcs)
                {
                    GUI.backgroundColor = new Color(0.15f, 1f, 0.05f, 1f);
                }
                bool selected = GUI.Toggle(tabRect, current, tabs[index], GUI.skin.button);
                GUI.backgroundColor = previousBackground;
                if (selected && !current)
                {
                    page = (Page)index;
                    scrollPosition = Vector2.zero;
                    filtersDirty = true;
                }
            }

            float left = windowRect.x + WindowPadding;
            float top = windowRect.y + ContentTop;
            float width = windowRect.width - WindowPadding * 2f;
            float bottom = windowRect.yMax - 37f;
            float footerY = windowRect.yMax - 25f;

            Fill(new Rect(windowRect.x + 1f, windowRect.yMax - 31f, windowRect.width - 2f, 1f), LineColor);

            string footerText = null;
            switch (page)
            {
                case Page.Index:
                case Page.Dlcs:
                    float listTop = DrawFilters(left, top, width);
                    DrawCatalog(new Rect(left, listTop, width, bottom - listTop));
                    footerText = CountLabel(filteredEntries.Count, page == Page.Dlcs ? "bundle" : "item");
                    break;
                case Page.Rig:
                    float rigTop = DrawProfileBar(left, top, width);
                    rigTop = DrawOutfit(left, rigTop, width);
                    rigTop = DrawWardrobeHeader(left, rigTop, width);
                    DrawCatalog(new Rect(left, rigTop, width, bottom - rigTop));
                    footerText = CountLabel(filteredEntries.Count, "item") + ", " + wornIds.Count + " equipped";
                    break;
                case Page.Cart:
                    DrawCart(new Rect(left, top, width, bottom - top));
                    footerText = CountLabel(GetCartCount(), "item") + " in cart";
                    bool previousCartEnabled = GUI.enabled;
                    GUI.enabled = previousCartEnabled && GetCartCount() > 0 && purchasingEntry == null;
                    if (GUI.Button(new Rect(windowRect.xMax - 190f, footerY, 100f, RowHeight), new GUIContent("Clear cart", "Empty your cart")))
                    {
                        ClearCart();
                    }
                    GUI.enabled = previousCartEnabled;
                    break;
                case Page.About:
                    DrawAbout(new Rect(left, top, width, bottom - top), footerY);
                    break;
            }

            if (footerText != null)
            {
                bool showingStatus = Time.unscaledTime < statusUntil;
                float footerWidth = width - (page == Page.Cart ? 200f : 90f);
                GUI.Label(new Rect(left, footerY, footerWidth, RowHeight), showingStatus ? statusText : footerText, showingStatus ? statusLabelStyle : footerLabelStyle);
            }

            if (GUI.Button(new Rect(windowRect.xMax - 80f, footerY, 75f, RowHeight), new GUIContent("Refresh", "Reload the shop")))
            {
                RefreshEntries();
            }
        }

        private static string CountLabel(int count, string noun)
        {
            return count + " " + noun + (count == 1 ? string.Empty : "s");
        }

        private float DrawFilters(float left, float top, float width)
        {
            const float resetWidth = 74f;
            bool bundles = page == Page.Dlcs;

            Rect bar = new Rect(left, top, width, bundles ? 30f : 55f);
            GUI.Box(bar, GUIContent.none);
            float x = bar.x + 8f;
            float y = bar.y + 5f;
            float right = bar.xMax - 8f;

            string newSearch = DrawSearchField(
                new Rect(x, y, right - x - resetWidth - 6f, RowHeight), "IndexSearch", searchText, bundles ? "search bundles..." : "search items...");
            if (!string.Equals(newSearch, searchText, StringComparison.Ordinal))
            {
                searchText = newSearch;
                scrollPosition = Vector2.zero;
                filtersDirty = true;
            }
            if (GUI.Button(new Rect(right - resetWidth, y, resetWidth, RowHeight), new GUIContent("Reset", "Clear the search and filters")))
            {
                ResetFilters();
            }

            if (bundles)
            {
                return bar.yMax + 8f;
            }

            y += 25f;
            GUI.Label(new Rect(x, y, 58f, RowHeight), "Category", mutedLabelStyle);
            if (GUI.Button(new Rect(x + 58f, y, 110f, RowHeight), new GUIContent(selectedCategory, "Click for the next one, right click to go back")))
            {
                int categoryIndex = categories.FindIndex(category => string.Equals(category, selectedCategory, StringComparison.OrdinalIgnoreCase));
                int step = Event.current.button == 1 ? -1 : 1;
                selectedCategory = categories.Count == 0
                    ? "All"
                    : categories[((categoryIndex + step) % categories.Count + categories.Count) % categories.Count];
                scrollPosition = Vector2.zero;
                filtersDirty = true;
            }

            DrawSortButton(x + 178f, y, 130f);

            bool newOwnedOnly = GUI.Toggle(new Rect(x + 350f, y, 70f, RowHeight), ownedOnly, new GUIContent("Owned", "Show stuff you already own (hidden otherwise)"));
            bool newFreeOnly = GUI.Toggle(new Rect(x + 422f, y, 84f, RowHeight), freeOnly, new GUIContent("Free only", "Only free stuff"));
            bool newAffordableOnly = GUI.Toggle(new Rect(x + 508f, y, right - (x + 508f), RowHeight), affordableOnly, new GUIContent("Can afford", "Only stuff you can afford"));
            if (newOwnedOnly != ownedOnly || newFreeOnly != freeOnly || newAffordableOnly != affordableOnly)
            {
                ownedOnly = newOwnedOnly;
                freeOnly = newFreeOnly;
                affordableOnly = newAffordableOnly;
                scrollPosition = Vector2.zero;
                filtersDirty = true;
            }
            return bar.yMax + 8f;
        }

        private void DrawSortButton(float x, float y, float buttonWidth)
        {
            GUI.Label(new Rect(x, y, 32f, RowHeight), "Sort", mutedLabelStyle);
            if (GUI.Button(new Rect(x + 32f, y, buttonWidth, RowHeight), new GUIContent(GetSortLabel(), "Click for the next one, right click to go back")))
            {
                int modeCount = Enum.GetValues(typeof(EntrySortMode)).Length;
                int step = Event.current.button == 1 ? -1 : 1;
                entrySortMode = (EntrySortMode)(((int)entrySortMode + step + modeCount) % modeCount);
                scrollPosition = Vector2.zero;
                filtersDirty = true;
            }
        }

        private string DrawSearchField(Rect rect, string controlName, string text, string placeholder)
        {
            GUI.SetNextControlName(controlName);
            string result = GUI.TextField(rect, text);
            if (string.IsNullOrEmpty(result) && GUI.GetNameOfFocusedControl() != controlName)
            {
                GUI.Label(new Rect(rect.x + 4f, rect.y, rect.width - 8f, rect.height), placeholder, mutedLabelStyle);
            }
            return result;
        }

        private void EnsureStyles()
        {
            if (stylesReady)
            {
                return;
            }

            GUIStyle label = GUI.skin.label;

            titleLabelStyle = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
            balanceStyle = new GUIStyle(label) { alignment = TextAnchor.MiddleLeft, richText = true, wordWrap = false };
            aboutTitleStyle = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = 20 };

            mutedLabelStyle = new GUIStyle(label);
            mutedLabelStyle.normal.textColor = Muted;
            smallMutedStyle = new GUIStyle(mutedLabelStyle) { fontSize = 12, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip };
            centerMutedStyle = new GUIStyle(mutedLabelStyle) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            sectionLabelStyle = new GUIStyle(mutedLabelStyle) { fontSize = 11, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            footerLabelStyle = new GUIStyle(mutedLabelStyle) { alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip };
            statusLabelStyle = new GUIStyle(footerLabelStyle);
            statusLabelStyle.normal.textColor = Accent;

            slotLabelStyle = new GUIStyle(mutedLabelStyle) { fontSize = 12, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            slotActiveLabelStyle = new GUIStyle(slotLabelStyle);
            slotActiveLabelStyle.normal.textColor = Accent;
            slotCaptionStyle = new GUIStyle(label) { fontSize = 10, alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Clip, padding = new RectOffset(0, 0, 0, 0) };
            slotCaptionStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
            overlayLabelStyle = new GUIStyle(label) { fontSize = 11, alignment = TextAnchor.MiddleCenter, wordWrap = true, padding = new RectOffset(0, 0, 0, 0) };
            overlayLabelStyle.normal.textColor = Color.white;

            sectionTitleStyle = new GUIStyle(label) { alignment = TextAnchor.MiddleLeft, wordWrap = false };
            cardNameStyle = new GUIStyle(label) { alignment = TextAnchor.UpperLeft, wordWrap = true, clipping = TextClipping.Clip };
            rowNameStyle = new GUIStyle(cardNameStyle) { alignment = TextAnchor.MiddleLeft, wordWrap = false };
            priceStyle = new GUIStyle(label) { alignment = TextAnchor.MiddleRight, wordWrap = false };

            tileNameStyle = new GUIStyle(label) { fontSize = 12, alignment = TextAnchor.UpperCenter, wordWrap = true, clipping = TextClipping.Clip };
            tileButtonStyle = new GUIStyle(GUI.skin.button) { fontSize = 12, padding = new RectOffset(2, 2, 2, 2) };

            tooltipStyle = new GUIStyle(GUI.skin.box) { fontSize = 12, alignment = TextAnchor.MiddleLeft, wordWrap = true, padding = new RectOffset(8, 8, 5, 5) };
            tooltipStyle.normal.textColor = new Color(0.92f, 0.92f, 0.92f);

            stylesReady = true;
        }

        private static void Fill(Rect rect, Color color)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            Color previousColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        private static void ColoredLabel(Rect rect, string text, GUIStyle style, Color color)
        {
            Color previousColor = GUI.contentColor;
            GUI.contentColor = color;
            GUI.Label(rect, text, style);
            GUI.contentColor = previousColor;
        }

        private float DrawProfileBar(float left, float top, float width)
        {
            const float buttonWidth = 74f;
            if (!rigSettingsLoaded)
            {
                LoadRigSettings();
            }

            Rect bar = new Rect(left, top, width, 30f);
            GUI.Box(bar, GUIContent.none);
            float y = bar.y + 5f;

            float x = bar.x + 8f;
            nameInput = GUI.TextField(new Rect(x, y, 140f, RowHeight), nameInput, 12);
            if (GUI.Button(new Rect(x + 146f, y, buttonWidth, RowHeight), new GUIContent("Set name", "Your name right now: " + GetSavedName() + ". Letters and numbers, 12 max")))
            {
                ApplyName();
            }

            x = bar.xMax - 8f - buttonWidth;
            if (GUI.Button(new Rect(x, y, buttonWidth, RowHeight), new GUIContent("Set color", "Saves your color and puts it on your gorilla")))
            {
                ApplyColor();
            }

            x -= 28f;
            Rect swatch = new Rect(x, y, 22f, RowHeight);
            Color previousColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(swatch.x - 1f, swatch.y - 1f, swatch.width + 2f, swatch.height + 2f), Texture2D.whiteTexture);
            GUI.color = new Color(colorR / 255f, colorG / 255f, colorB / 255f);
            GUI.DrawTexture(swatch, Texture2D.whiteTexture);
            GUI.color = previousColor;

            x -= 6f;
            colorB = DrawColorChannelField(ref x, y, "B", ref colorBText);
            colorG = DrawColorChannelField(ref x, y, "G", ref colorGText);
            colorR = DrawColorChannelField(ref x, y, "R", ref colorRText);
            return bar.yMax + 8f;
        }

        private static int DrawColorChannelField(ref float x, float y, string channel, ref string text)
        {
            x -= 36f;
            string newText = GUI.TextField(new Rect(x, y, 36f, RowHeight), text, 3);
            if (newText != text)
            {
                text = new string(newText.Where(char.IsDigit).ToArray());
            }
            x -= 14f;
            GUI.Label(new Rect(x, y, 12f, RowHeight), channel);
            x -= 6f;

            int value;
            int.TryParse(text, out value);
            return Mathf.Clamp(value, 0, 255);
        }

        private void LoadRigSettings()
        {
            rigSettingsLoaded = true;
            nameInput = GetSavedName();
            colorR = Mathf.RoundToInt(PlayerPrefs.GetFloat("redValue", 0f) * 255f);
            colorG = Mathf.RoundToInt(PlayerPrefs.GetFloat("greenValue", 0f) * 255f);
            colorB = Mathf.RoundToInt(PlayerPrefs.GetFloat("blueValue", 0f) * 255f);
            colorRText = colorR.ToString();
            colorGText = colorG.ToString();
            colorBText = colorB.ToString();
        }

        private string GetSavedName()
        {
            Type computerType;
            object computer = GetGorillaComputer(out computerType);
            string savedName = computer == null ? null : ReadMember<string>(computerType, computer, "savedName");
            return string.IsNullOrEmpty(savedName) ? PlayerPrefs.GetString("playerName", "gorilla") : savedName;
        }

        private void ApplyColor()
        {
            float red = colorR / 255f;
            float green = colorG / 255f;
            float blue = colorB / 255f;

            Type computerType;
            object computer = GetGorillaComputer(out computerType);
            Type taggerType = Type.GetType("GorillaTagger, Assembly-CSharp");
            object tagger = taggerType?.GetProperty("Instance", StaticMembers)?.GetValue(null, null);
            Type[] colorArguments = { typeof(float), typeof(float), typeof(float) };
            MethodInfo updateComputerColor = computerType?.GetMethod("UpdateColor", InstanceMembers, null, colorArguments, null);
            MethodInfo updateTaggerColor = taggerType?.GetMethod("UpdateColor", InstanceMembers, null, colorArguments, null);
            if (tagger == null || updateTaggerColor == null)
            {
                Logger.LogWarning("[SI] Cannot set color: GorillaTagger.UpdateColor is unavailable.");
                ShowStatus("Your gorilla hasn't loaded yet");
                return;
            }

            try
            {
                PlayerPrefs.SetFloat("redValue", red);
                PlayerPrefs.SetFloat("greenValue", green);
                PlayerPrefs.SetFloat("blueValue", blue);
                PlayerPrefs.Save();
                if (computer != null && updateComputerColor != null)
                {
                    updateComputerColor.Invoke(computer, new object[] { red, green, blue });
                }
                updateTaggerColor.Invoke(tagger, new object[] { red, green, blue });
                SendColorToRoom(taggerType, tagger, red, green, blue);
            }
            catch (Exception ex)
            {
                Exception cause = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                Logger.LogWarning("[SI] Setting color failed: " + cause);
                ShowStatus("Couldn't set color: " + cause.Message);
                return;
            }
            ShowStatus("Color set to " + colorR + " " + colorG + " " + colorB);
        }

        private static void SendColorToRoom(Type taggerType, object tagger, float red, float green, float blue)
        {
            Type photonType = Type.GetType("Photon.Pun.PhotonNetwork, PhotonUnityNetworking");
            bool inRoom = photonType?.GetProperty("InRoom", StaticMembers)?.GetValue(null, null) is bool value && value;
            if (!inRoom)
            {
                return;
            }

            object netView = taggerType.GetProperty("myVRRig", InstanceMembers)?.GetValue(tagger, null);
            Type targetType = Type.GetType("Photon.Pun.RpcTarget, PhotonUnityNetworking");
            MethodInfo sendRpc = netView == null || targetType == null ? null : netView.GetType().GetMethod(
                "SendRPC", InstanceMembers, null, new[] { typeof(string), targetType, typeof(object[]) }, null);
            sendRpc?.Invoke(netView, new object[] { "RPC_InitializeNoobMaterial", Enum.Parse(targetType, "All"), new object[] { red, green, blue } });
        }

        private void ApplyName()
        {
            string name = new string(nameInput.ToUpperInvariant().Where(character => character >= 'A' && character <= 'Z' || character >= '0' && character <= '9').ToArray());
            if (name.Length > 12)
            {
                name = name.Substring(0, 12);
            }
            nameInput = name;
            if (name.Length == 0)
            {
                ShowStatus("Name needs at least one letter or number");
                return;
            }
            if (name == GetSavedName())
            {
                ShowStatus("That's already your name");
                return;
            }

            Type computerType;
            object computer = GetGorillaComputer(out computerType);
            FieldInfo currentNameField = computerType?.GetField("currentName", InstanceMembers);
            MethodInfo processName = computerType?.GetMethod("ProcessNameState", InstanceMembers);
            Type keyType = computerType?.Assembly.GetType("GorillaNetworking.GorillaKeyboardBindings");
            if (computer == null || currentNameField == null || processName == null || keyType == null)
            {
                Logger.LogWarning("[SI] Cannot set name: the Gorilla Tag computer is unavailable.");
                ShowStatus("Can't reach the computer right now");
                return;
            }
            if (!ReadMember<bool>(computerType, computer, "NametagsEnabled"))
            {
                ShowStatus("Nametags are off, turn them on at the computer first");
                return;
            }

            try
            {
                currentNameField.SetValue(computer, name);
                processName.Invoke(computer, new[] { Enum.Parse(keyType, "enter") });
            }
            catch (Exception ex)
            {
                Exception cause = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                Logger.LogWarning("[SI] Setting name failed: " + cause);
                ShowStatus("Couldn't set name: " + cause.Message);
                return;
            }
            ShowStatus("Checking if " + name + " is allowed...");
        }

        private static object GetGorillaComputer(out Type computerType)
        {
            computerType = Type.GetType("GorillaNetworking.GorillaComputer, Assembly-CSharp");
            return computerType == null ? null : GetController(computerType);
        }

        private void ResetFilters()
        {
            GUIUtility.keyboardControl = 0;
            searchText = string.Empty;
            selectedCategory = "All";
            ownedOnly = false;
            freeOnly = false;
            affordableOnly = false;
            scrollPosition = Vector2.zero;
            filtersDirty = true;
        }

        private void ShowStatus(string text, float seconds = 6f)
        {
            statusText = text;
            statusUntil = Time.unscaledTime + seconds;
        }

        private void ShowCartStatus(string text)
        {
            if (purchasingEntry == null)
            {
                ShowStatus(text, 3f);
            }
        }

        private void DrawAbout(Rect area, float footerY)
        {
            GUI.Label(new Rect(area.x, area.y + 6f, area.width, 32f), "ShopIndex", aboutTitleStyle);
            GUI.Label(new Rect(area.x, area.y + 38f, area.width, RowHeight), "v" + Version, centerMutedStyle);
            GUI.Label(new Rect(area.x, area.y + 56f, area.width, RowHeight), "(C) Copyright 2026 kx5qz / MIT License", centerMutedStyle);

            Rect panel = new Rect(area.x, area.y + 88f, area.width, 280f);
            GUI.Box(panel, GUIContent.none);
            float x = panel.x + 14f;
            float y = panel.y + 10f;
            float width = panel.width - 28f;
            y = DrawAboutSection(x, y, width, "made by", Color.white, "kx5qz");
            y = DrawAboutSection(x, y, width, "inspired by", Color.white, "GorillaShop by the graze");
            y = DrawAboutSection(x, y, width, "controls", Color.white,
                "press " + GetOpenKeyName() + " to open or close this",
                "drag the top bar to move it",
                "right click category or sort to go back");

            GUI.Label(new Rect(x, y, width, 16f), "menu key", sectionLabelStyle);
            y += 18f;
            measureContent.text = rebindingOpenKey ? "..." : GetOpenKeyName();
            float keyWidth = Mathf.Max(36f, GUI.skin.button.CalcSize(measureContent).x + 6f);
            if (GUI.Button(new Rect(x, y, keyWidth, RowHeight), new GUIContent(measureContent.text, "Click it, then press the key you want")))
            {
                rebindingOpenKey = !rebindingOpenKey;
            }
            GUI.Label(new Rect(x + keyWidth + 8f, y, width - keyWidth - 8f, RowHeight), rebindingOpenKey ? "press a key, esc to cancel" : "click to change it", mutedLabelStyle);
            y += 31f;

            DrawAboutSection(x, y, width, "source", Accent, RepositoryUrl);

            if (GUI.Button(new Rect(area.x, footerY, 150f, RowHeight), new GUIContent("Open GitHub", "Opens the GitHub page in your browser")))
            {
                Application.OpenURL(RepositoryUrl);
            }
        }

        private float DrawAboutSection(float x, float y, float width, string heading, Color lineColor, params string[] lines)
        {
            GUI.Label(new Rect(x, y, width, 16f), heading, sectionLabelStyle);
            y += 17f;
            foreach (string line in lines)
            {
                ColoredLabel(new Rect(x, y, width, RowHeight), line, GUI.skin.label, lineColor);
                y += 19f;
            }
            return y + 9f;
        }

        private void DrawTooltip()
        {
            if (string.IsNullOrEmpty(GUI.tooltip))
            {
                return;
            }

            const float maxWidth = 300f;
            GUIContent tooltip = new GUIContent(GUI.tooltip);
            float width = Mathf.Min(tooltipStyle.CalcSize(tooltip).x + 2f, maxWidth);
            float height = tooltipStyle.CalcHeight(tooltip, width);
            Vector2 mouse = Event.current.mousePosition;
            Rect tooltipRect = new Rect(mouse.x + 14f, mouse.y + 18f, width, height);
            if (tooltipRect.xMax > Screen.width)
            {
                tooltipRect.x = mouse.x - 8f - width;
            }
            if (tooltipRect.yMax > Screen.height)
            {
                tooltipRect.y = mouse.y - 8f - height;
            }
            tooltipRect.x = Mathf.Max(0f, tooltipRect.x);
            tooltipRect.y = Mathf.Max(0f, tooltipRect.y);

            Fill(tooltipRect, new Color(0f, 0f, 0f, 0.85f));
            GUI.Box(tooltipRect, tooltip, tooltipStyle);

            GUI.tooltip = string.Empty;
        }

        private void SafeDraw(Action draw)
        {
            try
            {
                draw();
            }
            catch (Exception ex) when (!(ex is ExitGUIException))
            {
                if (ex.Message != lastDrawError)
                {
                    lastDrawError = ex.Message;
                    Logger.LogError("[SI] Error while drawing UI: " + ex);
                }
                GUI.Label(
                    new Rect(windowRect.x + WindowPadding, windowRect.y + ContentTop, windowRect.width - WindowPadding * 2f, windowRect.height - ContentTop - 37f),
                    "Something broke while drawing the menu.\n\n" + ex.Message + "\n\n" + ex.StackTrace);
            }
        }

        private void HandleWindowDrag()
        {
            Rect dragRect = new Rect(windowRect.x, windowRect.y, windowRect.width - 30f, HeaderHeight);
            Event currentEvent = Event.current;

            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && dragRect.Contains(currentEvent.mousePosition))
            {
                windowDragging = true;
                windowDragOffset = currentEvent.mousePosition - new Vector2(windowRect.x, windowRect.y);
                GUIUtility.keyboardControl = 0;
                currentEvent.Use();
            }
            else if (currentEvent.type == EventType.MouseDrag && windowDragging && currentEvent.button == 0)
            {
                windowRect.x = currentEvent.mousePosition.x - windowDragOffset.x;
                windowRect.y = currentEvent.mousePosition.y - windowDragOffset.y;
                windowRect.x = Mathf.Clamp(windowRect.x, 0f, Mathf.Max(0f, Screen.width - windowRect.width));
                windowRect.y = Mathf.Clamp(windowRect.y, 0f, Mathf.Max(0f, Screen.height - windowRect.height));
                currentEvent.Use();
            }
            else if (currentEvent.rawType == EventType.MouseUp && currentEvent.button == 0 && windowDragging)
            {
                windowDragging = false;
                windowXConfig.Value = windowRect.x;
                windowYConfig.Value = windowRect.y;
            }
        }

        private string GetSortLabel()
        {
            return entrySortMode == EntrySortMode.OldestToNewest ? "Oldest to Newest" :
                entrySortMode == EntrySortMode.NewestToOldest ? "Newest to Oldest" :
                entrySortMode == EntrySortMode.PriceLowToHigh ? "Price Low to High" :
                entrySortMode == EntrySortMode.PriceHighToLow ? "Price High to Low" : "Name A-Z";
        }

        private void DrawCatalog(Rect viewport)
        {
            if (filteredEntries.Count == 0)
            {
                bool showingUnfilteredBundles = page == Page.Dlcs && string.IsNullOrWhiteSpace(searchText);
                string message = entries.Count == 0 ? "Loading the shop..." :
                    showingUnfilteredBundles && !string.IsNullOrWhiteSpace(bundleCatalogStatus) ? bundleCatalogStatus : "Nothing found";
                GUI.Label(new Rect(viewport.x, viewport.y, viewport.width, Mathf.Min(viewport.height, 90f)), message, centerMutedStyle);
                return;
            }

            bool wardrobe = page == Page.Rig;
            float cardHeight = wardrobe ? 170f : 208f;
            float minimumCardWidth = wardrobe ? 130f : 154f;
            const float gap = 8f;
            float usableWidth = viewport.width;
            int columns = Mathf.Max(1, Mathf.FloorToInt((usableWidth + gap) / (minimumCardWidth + gap)));
            int rows = Mathf.CeilToInt(filteredEntries.Count / (float)columns);
            if (rows * (cardHeight + gap) - gap > viewport.height)
            {
                usableWidth = viewport.width - 20f;
                columns = Mathf.Max(1, Mathf.FloorToInt((usableWidth + gap) / (minimumCardWidth + gap)));
                rows = Mathf.CeilToInt(filteredEntries.Count / (float)columns);
            }
            float cardWidth = (usableWidth - gap * (columns - 1)) / columns;
            float contentWidth = usableWidth;
            float contentHeight = rows * (cardHeight + gap) - gap;
            scrollPosition = GUI.BeginScrollView(viewport, scrollPosition, new Rect(0f, 0f, contentWidth, contentHeight));

            int firstRow = Mathf.Max(0, Mathf.FloorToInt(scrollPosition.y / (cardHeight + gap)) - 1);
            int lastRow = Mathf.Min(rows - 1, Mathf.CeilToInt((scrollPosition.y + viewport.height) / (cardHeight + gap)) + 1);
            for (int row = firstRow; row <= lastRow; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    int index = row * columns + column;
                    if (index >= filteredEntries.Count)
                    {
                        break;
                    }
                    Rect cardRect = new Rect(column * (cardWidth + gap), row * (cardHeight + gap), cardWidth, cardHeight);
                    if (wardrobe)
                    {
                        DrawWardrobeTile(filteredEntries[index], cardRect);
                    }
                    else
                    {
                        DrawCard(filteredEntries[index], cardRect);
                    }
                }
            }
            GUI.EndScrollView();
        }

        private void DrawCard(Item entry, Rect cardRect)
        {
            bool inCart = !entry.IsBundle && IsInCart(entry);

            GUI.Box(cardRect, GUIContent.none);

            Rect stage = new Rect(cardRect.x + 8f, cardRect.y + 8f, cardRect.width - 16f, 92f);
            Fill(stage, StageColor);
            if (entry.Sprite == null)
            {
                GUI.Label(stage, "no picture", slotLabelStyle);
            }
            DrawSprite(entry.Sprite, new Rect(stage.x + 2f, stage.y + 2f, stage.width - 4f, stage.height - 4f));

            GUI.Label(new Rect(cardRect.x + 8f, cardRect.y + 104f, cardRect.width - 16f, 34f), entry.DisplayName, cardNameStyle);

            string price = string.IsNullOrWhiteSpace(entry.DisplayPrice) ? FormatCost(entry) : entry.DisplayPrice;
            if (entry.IsBundle && !entry.CanPurchase)
            {
                price = string.IsNullOrWhiteSpace(entry.DisplayPrice) ? "No price" : entry.DisplayPrice;
            }
            Color priceColor = entry.IsOwned ? Muted :
                entry.IsFree ? Good :
                entry.IsBundle ? Color.white :
                shinyRocks >= 0 && entry.Cost > shinyRocks ? Bad : Color.white;
            Rect detailRect = new Rect(cardRect.x + 8f, cardRect.y + 142f, cardRect.width - 16f, 18f);
            measureContent.text = price;
            float categoryWidth = detailRect.width - priceStyle.CalcSize(measureContent).x - 6f;
            measureContent.text = entry.IsSteamDlc ? "Steam DLC" : entry.Category;
            if (smallMutedStyle.CalcSize(measureContent).x <= categoryWidth)
            {
                GUI.Label(new Rect(detailRect.x, detailRect.y, categoryWidth, detailRect.height), measureContent.text, smallMutedStyle);
            }
            ColoredLabel(detailRect, price, priceStyle, priceColor);

            Rect buttonRect = new Rect(cardRect.x + 8f, cardRect.yMax - 34f, cardRect.width - 16f, 26f);
            if (entry.IsBundle)
            {
                bool previousEnabled = GUI.enabled;
                GUI.enabled = previousEnabled && (entry.IsSteamDlc || entry.CanPurchase && !entry.IsOwned);
                string buttonText = entry.IsOwned ? "Owned" :
                    entry.IsSteamDlc ? (entry.IsFree ? "Get on Steam" : entry.CanPurchase ? "Buy on Steam" : "View on Steam") :
                    entry.CanPurchase ? "Buy bundle" : "Unavailable";
                if (GUI.Button(buttonRect, buttonText))
                {
                    if (entry.IsSteamDlc)
                    {
                        OpenSteamDlcPage(entry);
                    }
                    else
                    {
                        TryPurchaseEntry(entry);
                    }
                }
                GUI.enabled = previousEnabled;
            }
            else if (entry.IsOwned)
            {
                bool previousEnabled = GUI.enabled;
                GUI.enabled = false;
                GUI.Button(buttonRect, new GUIContent("OWNED", "You already own this"));
                GUI.enabled = previousEnabled;
            }
            else
            {
                float halfWidth = (buttonRect.width - 4f) / 2f;
                GUIContent cartContent = inCart
                    ? new GUIContent("REMOVE", "Take it out of your cart")
                    : new GUIContent("CART", "Add it to your cart to try it on");
                if (GUI.Button(new Rect(buttonRect.x, buttonRect.y, halfWidth, buttonRect.height), cartContent))
                {
                    if (inCart)
                    {
                        RemoveFromCart(entry);
                    }
                    else
                    {
                        AddToCart(entry);
                    }
                }
                DrawBuyButton(entry, new Rect(buttonRect.x + halfWidth + 4f, buttonRect.y, halfWidth, buttonRect.height));
            }
        }

        private void DrawBuyButton(Item entry, Rect buttonRect)
        {
            bool canAfford = shinyRocks >= 0 && entry.Cost <= shinyRocks;
            bool confirming = confirmBuyId == entry.Id && Time.unscaledTime < confirmBuyUntil;
            string tooltip = purchasingEntry != null ? "Hold on, something's still buying" :
                !canAfford ? "You need " + (entry.Cost - Mathf.Max(0, shinyRocks)).ToString() + " more Shiny Rocks" :
                confirming ? "Click again to buy for " + FormatCost(entry) + ", you'll have " + (shinyRocks - entry.Cost).ToString() + " left" :
                "Buy for " + FormatCost(entry);
            string label = purchasingEntry != null && purchasingEntry.Id == entry.Id ? "BUYING..." :
                confirming ? "U SURE?" :
                entry.IsFree ? "GET" : "BUY";

            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && purchasingEntry == null && canAfford;
            if (GUI.Button(buttonRect, new GUIContent(label, tooltip)))
            {
                if (confirming)
                {
                    confirmBuyId = null;
                    TryBuyCosmetic(entry);
                }
                else
                {
                    confirmBuyId = entry.Id;
                    confirmBuyUntil = Time.unscaledTime + 3f;
                }
            }
            GUI.enabled = previousEnabled;
        }

        private static string FormatCost(Item entry)
        {
            return entry.IsFree ? "FREE" : entry.Cost.ToString() + " SR";
        }

        private void TryBuyCosmetic(Item entry)
        {
            Type controllerType;
            object controller = GetCosmeticsController(out controllerType);
            FieldInfo stageField = controllerType?.GetField("currentPurchaseItemStage", InstanceMembers);
            FieldInfo itemToBuyField = controllerType?.GetField("itemToBuy", InstanceMembers);
            MethodInfo processStage = controllerType?.GetMethod("ProcessPurchaseItemState", InstanceMembers);
            if (controller == null || stageField == null || itemToBuyField == null || processStage == null || entry.Source == null)
            {
                Logger.LogWarning("[SI] Cannot buy " + entry.Id + ": Gorilla Tag's checkout is unavailable.");
                ShowStatus("Checkout isn't working right now");
                return;
            }

            if (purchasingEntry != null || string.Equals(stageField.GetValue(controller)?.ToString(), "Buying", StringComparison.Ordinal))
            {
                ShowStatus("Hold on, something's still buying");
                return;
            }

            try
            {
                itemToBuyField.SetValue(controller, entry.Source);
                stageField.SetValue(controller, Enum.Parse(stageField.FieldType, "CheckoutButtonPressed"));
                processStage.Invoke(controller, new object[] { null, false });

                string stage = stageField.GetValue(controller).ToString();
                if (stage == "ItemOwned")
                {
                    ShowStatus("You already own " + entry.DisplayName);
                    return;
                }
                if (stage != "ItemSelected")
                {
                    ShowStatus("Not enough Shiny Rocks for " + entry.DisplayName);
                    return;
                }

                processStage.Invoke(controller, new object[] { "right", false });
                processStage.Invoke(controller, new object[] { "left", false });
            }
            catch (Exception ex)
            {
                Exception cause = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                Logger.LogWarning("[SI] Checkout failed for " + entry.Id + ": " + cause);
                ShowStatus("Couldn't buy it: " + cause.Message);
                return;
            }

            purchasingEntry = entry;
            purchaseStartedAt = Time.unscaledTime;
            ShowStatus("Buying " + entry.DisplayName + "...", 30f);
            Logger.LogInfo("[SI] Started Gorilla Tag checkout for cosmetic: " + entry.Id);
        }

        private void PollPurchase()
        {
            Type controllerType;
            object controller = GetCosmeticsController(out controllerType);
            FieldInfo stageField = controllerType?.GetField("currentPurchaseItemStage", InstanceMembers);
            string stage = controller == null || stageField == null ? null : stageField.GetValue(controller)?.ToString();

            if (stage == "Success")
            {
                ShowStatus("Bought " + purchasingEntry.DisplayName + "!");
                Logger.LogInfo("[SI] Bought cosmetic: " + purchasingEntry.Id);
            }
            else if (stage == "Failure")
            {
                ShowStatus("Didn't go through, no Shiny Rocks spent");
                Logger.LogWarning("[SI] Purchase failed for cosmetic: " + purchasingEntry.Id);
            }
            else if (Time.unscaledTime - purchaseStartedAt > 30f)
            {
                ShowStatus("Taking a while, check the checkout screen in the shop");
            }
            else
            {
                return;
            }

            purchasingEntry = null;
            RefreshEntries();
        }

        private float DrawOutfit(float left, float top, float width)
        {
            const float gap = 4f;

            float squareSize = Mathf.Min(74f, Mathf.Floor((width - gap * (OutfitColumns - 1)) / OutfitColumns));
            int rowCount = (OutfitSquareLabels.Length + OutfitColumns - 1) / OutfitColumns;
            for (int square = 0; square < OutfitSquareLabels.Length; square++)
            {
                int row = square / OutfitColumns;
                int squaresInRow = Mathf.Min(OutfitColumns, OutfitSquareLabels.Length - row * OutfitColumns);
                float rowLeft = left + (width - (squareSize * squaresInRow + gap * (squaresInRow - 1))) / 2f;
                Rect squareRect = new Rect(rowLeft + (square % OutfitColumns) * (squareSize + gap), top + row * (squareSize + gap), squareSize, squareSize);
                DrawOutfitSquare(square, squareRect);
            }
            return top + rowCount * (squareSize + gap) + 6f;
        }

        private void DrawOutfitSquare(int square, Rect squareRect)
        {
            const float captionHeight = 14f;

            int[] slots = OutfitSquareSlots[square];
            string label = OutfitSquareLabels[square];
            Item first = wornSlotEntries[slots[0]];
            Item second = slots.Length > 1 ? wornSlotEntries[slots[1]] : null;
            if (first != null && second != null && first.Id == second.Id)
            {
                second = null;
            }

            Vector2 pointer = Event.current.mousePosition;
            bool hovered = Event.current.type == EventType.Repaint && squareRect.Contains(pointer);

            Color previousBackground = GUI.backgroundColor;
            if (first != null || second != null)
            {
                GUI.backgroundColor = Accent;
            }
            GUI.Box(squareRect, GUIContent.none);
            GUI.backgroundColor = previousBackground;

            Rect caption = new Rect(squareRect.x + 1f, squareRect.yMax - captionHeight - 1f, squareRect.width - 2f, captionHeight);
            float pictureHeight = squareRect.height - captionHeight - 6f;

            if (first != null && second != null)
            {
                float half = squareRect.width / 2f;
                Rect leftHalf = new Rect(squareRect.x, squareRect.y, half, squareRect.height);
                Rect rightHalf = new Rect(squareRect.x + half, squareRect.y, half, squareRect.height);
                DrawSprite(first.Sprite, new Rect(leftHalf.x + 3f, leftHalf.y + 4f, leftHalf.width - 4f, pictureHeight));
                DrawSprite(second.Sprite, new Rect(rightHalf.x + 1f, rightHalf.y + 4f, rightHalf.width - 4f, pictureHeight));
                Fill(new Rect(squareRect.x + half, squareRect.y + 4f, 1f, pictureHeight), new Color(0f, 0f, 0f, 0.5f));
                DrawSlotCaption(caption, label);
                if (hovered)
                {
                    DrawTakeOffHint(leftHalf.Contains(pointer) ? leftHalf : rightHalf);
                }

                DrawTakeOffButton(leftHalf, first, label + ", left");
                DrawTakeOffButton(rightHalf, second, label + ", right");
                return;
            }

            Item worn = first ?? second;
            if (worn != null)
            {
                DrawSprite(worn.Sprite, new Rect(squareRect.x + 4f, squareRect.y + 4f, squareRect.width - 8f, pictureHeight));
                DrawSlotCaption(caption, label);
                if (hovered)
                {
                    DrawTakeOffHint(squareRect);
                }
                DrawTakeOffButton(squareRect, worn, label);
                return;
            }

            string squareCategory = OutfitSquareCategories[square];
            bool browsing = wardrobeCategory == squareCategory;
            GUI.Label(squareRect, label, browsing ? slotActiveLabelStyle : slotLabelStyle);
            string tooltip = browsing ? "Showing your " + label + " stuff, click to show everything" : label + " is empty, click to browse";
            if (GUI.Button(squareRect, new GUIContent(string.Empty, tooltip), GUIStyle.none))
            {
                wardrobeCategory = browsing ? "All" : squareCategory;
                scrollPosition = Vector2.zero;
                filtersDirty = true;
            }
        }

        private void DrawSlotCaption(Rect caption, string label)
        {
            Fill(caption, new Color(0f, 0f, 0f, 0.45f));
            GUI.Label(caption, label, slotCaptionStyle);
        }

        private void DrawTakeOffHint(Rect area)
        {
            Rect overlay = new Rect(area.x + 1f, area.y + 1f, area.width - 2f, area.height - 2f);
            Fill(overlay, new Color(0f, 0f, 0f, 0.6f));
            GUI.Label(overlay, "TAKE OFF", overlayLabelStyle);
        }

        private void DrawTakeOffButton(Rect area, Item worn, string where)
        {
            if (GUI.Button(area, new GUIContent(string.Empty, worn.DisplayName + " (" + where + "): click to take off"), GUIStyle.none))
            {
                ToggleEquipped(worn, false);
            }
        }

        private float DrawWardrobeHeader(float left, float top, float width)
        {
            const float searchWidth = 150f;

            measureContent.text = "Wardrobe";
            float titleWidth = sectionTitleStyle.CalcSize(measureContent).x;
            GUI.Label(new Rect(left, top, titleWidth, RowHeight), "Wardrobe", sectionTitleStyle);
            if (wardrobeCategory != "All")
            {
                measureContent.text = wardrobeCategory;
                float filterWidth = Mathf.Max(50f, tileButtonStyle.CalcSize(measureContent).x + 14f);
                if (GUI.Button(new Rect(left + titleWidth + 6f, top + 1f, filterWidth, RowHeight - 2f), new GUIContent(wardrobeCategory, "Click to show everything"), tileButtonStyle))
                {
                    wardrobeCategory = "All";
                    scrollPosition = Vector2.zero;
                    filtersDirty = true;
                }
            }

            string newSearch = DrawSearchField(new Rect(left + width - searchWidth, top, searchWidth, RowHeight), "WardrobeSearch", wardrobeSearchText, "search...");
            if (!string.Equals(newSearch, wardrobeSearchText, StringComparison.Ordinal))
            {
                wardrobeSearchText = newSearch;
                scrollPosition = Vector2.zero;
                filtersDirty = true;
            }
            return top + 26f;
        }

        private void DrawWardrobeTile(Item entry, Rect tileRect)
        {
            const float iconSize = 84f;

            bool worn = wornIds.Contains(entry.Id);
            Color previousBackground = GUI.backgroundColor;
            if (worn)
            {
                GUI.backgroundColor = Accent;
            }
            GUI.Box(tileRect, GUIContent.none);
            GUI.backgroundColor = previousBackground;

            Rect stage = new Rect(tileRect.x + 6f, tileRect.y + 6f, tileRect.width - 12f, iconSize + 4f);
            Fill(stage, StageColor);
            DrawSprite(entry.Sprite, new Rect(tileRect.x + (tileRect.width - iconSize) / 2f, tileRect.y + 8f, iconSize, iconSize));

            GUI.Label(new Rect(tileRect.x + 4f, tileRect.y + iconSize + 14f, tileRect.width - 8f, 34f), new GUIContent(entry.DisplayName, entry.DisplayName + " (" + entry.Category + ")"), tileNameStyle);
            DrawEquipButtons(entry, new Rect(tileRect.x + 6f, tileRect.yMax - 30f, tileRect.width - 12f, 24f));
        }

        private void DrawEquipButtons(Item entry, Rect buttonRect)
        {
            if (wornIds.Contains(entry.Id))
            {
                if (GUI.Button(buttonRect, new GUIContent("UNEQUIP", "Take it off"), tileButtonStyle))
                {
                    ToggleEquipped(entry, false);
                }
                return;
            }

            string leftPressLabel;
            string rightPressLabel;
            if (!GetEquipSideLabels(entry, out leftPressLabel, out rightPressLabel))
            {
                if (GUI.Button(buttonRect, new GUIContent("EQUIP", "Put it on"), tileButtonStyle))
                {
                    ToggleEquipped(entry, false);
                }
                return;
            }

            bool leftPressFirst = leftPressLabel.StartsWith("L", StringComparison.Ordinal);
            float halfWidth = (buttonRect.width - 3f) / 2f;
            Rect firstRect = new Rect(buttonRect.x, buttonRect.y, halfWidth, buttonRect.height);
            Rect secondRect = new Rect(buttonRect.x + halfWidth + 3f, buttonRect.y, halfWidth, buttonRect.height);
            if (GUI.Button(leftPressFirst ? firstRect : secondRect, new GUIContent(leftPressLabel, "Put it on your " + DescribeSide(leftPressLabel)), tileButtonStyle))
            {
                ToggleEquipped(entry, true);
            }
            if (GUI.Button(leftPressFirst ? secondRect : firstRect, new GUIContent(rightPressLabel, "Put it on your " + DescribeSide(rightPressLabel)), tileButtonStyle))
            {
                ToggleEquipped(entry, false);
            }
        }

        private static string DescribeSide(string label)
        {
            return label.Replace("L ", "left ").Replace("R ", "right ").ToLowerInvariant();
        }

        private bool GetEquipSideLabels(Item entry, out string leftPressLabel, out string rightPressLabel)
        {
            leftPressLabel = null;
            rightPressLabel = null;
            if (!entry.IsHoldable)
            {
                return false;
            }

            int dockPositions = GetDockPositions(entry);
            if (dockPositions <= 0)
            {
                return false;
            }

            int leftPressSpot = (dockPositions & DockRightArm) != 0 ? DockRightArm : DockLeftBack;
            int rightPressSpot = (dockPositions & DockLeftArm) != 0 ? DockLeftArm : DockRightBack;
            if ((dockPositions & leftPressSpot) == 0 || (dockPositions & rightPressSpot) == 0)
            {
                return false;
            }

            leftPressLabel = DockSpotLabel(leftPressSpot);
            rightPressLabel = DockSpotLabel(rightPressSpot);
            return true;
        }

        private static string DockSpotLabel(int spot)
        {
            return spot == DockLeftArm ? "L ARM" : spot == DockRightArm ? "R ARM" : spot == DockLeftBack ? "L BACK" : "R BACK";
        }

        private int GetDockPositions(Item entry)
        {
            int cached;
            if (dockPositionsById.TryGetValue(entry.Id, out cached))
            {
                return cached;
            }

            try
            {
                Type taggerType = Type.GetType("GorillaTagger, Assembly-CSharp");
                Type dockType = Type.GetType("BodyDockPositions, Assembly-CSharp");
                object tagger = taggerType?.GetProperty("Instance", StaticMembers)?.GetValue(null, null);
                Component rig = tagger == null ? null : ReadMember<Component>(taggerType, tagger, "offlineVRRig");
                Component dock = rig == null || dockType == null ? null : rig.GetComponent(dockType);
                MethodInfo indexFromName = dockType?.GetMethod("TransferrableObjectIndexFromName", InstanceMembers);
                string displayName = ReadMember<string>(entry.Source.GetType(), entry.Source, "displayName");
                IList indices = dock == null || indexFromName == null ? null : indexFromName.Invoke(dock, new object[] { displayName }) as IList;
                IList allObjects = dock == null ? null : ReadMember<IList>(dockType, dock, "allObjects");
                if (indices == null || indices.Count == 0 || allObjects == null)
                {
                    return -1;
                }

                object transferrable = allObjects[(int)indices[0]];
                object positions = transferrable == null ? null : ReadMember<object>(transferrable.GetType(), transferrable, "dockPositions");
                int dockPositions = positions == null ? -1 : Convert.ToInt32(positions);
                dockPositionsById[entry.Id] = dockPositions;
                return dockPositions;
            }
            catch (Exception ex)
            {
                Logger.LogWarning("[SI] Could not read dock spots for " + entry.Id + ": " + ex.Message);
                dockPositionsById[entry.Id] = -1;
                return -1;
            }
        }

        private void ToggleEquipped(Item entry, bool isLeftHand)
        {
            Type controllerType;
            object controller = GetCosmeticsController(out controllerType);
            if (controller == null || entry.Source == null)
            {
                Logger.LogWarning("[SI] Cannot equip " + entry.Id + ": CosmeticsController is unavailable.");
                ShowStatus("Wardrobe isn't working right now");
                return;
            }

            bool wasEquipped = wornIds.Contains(entry.Id);
            try
            {
                if (wasEquipped)
                {
                    Unequip(controllerType, controller, entry);
                }
                else
                {
                    MethodInfo pressWardrobeItem = controllerType.GetMethod(
                        "PressWardrobeItemButton", InstanceMembers, null, new[] { entry.Source.GetType(), typeof(bool), typeof(bool) }, null);
                    if (pressWardrobeItem == null)
                    {
                        throw new MissingMethodException("PressWardrobeItemButton was not found.");
                    }
                    pressWardrobeItem.Invoke(controller, new object[] { entry.Source, isLeftHand, false });
                }
            }
            catch (Exception ex)
            {
                Exception cause = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                Logger.LogWarning("[SI] Equip failed for " + entry.Id + ": " + cause);
                ShowStatus("Couldn't change " + entry.DisplayName + ": " + cause.Message);
                return;
            }

            RefreshWornSnapshot();
            ShowStatus((wasEquipped ? "Took off " : "Put on ") + entry.DisplayName);
        }

        private void Unequip(Type controllerType, object controller, Item entry)
        {
            object wornSet = ReadMember<object>(controllerType, controller, "currentWornSet");
            MethodInfo removeFromSet = controllerType.GetMethod("RemoveCosmeticItemFromSet", InstanceMembers);
            MethodInfo updateWornCosmetics = controllerType.GetMethod("UpdateWornCosmetics", InstanceMembers, null, new[] { typeof(bool) }, null);
            string wornDisplayName;
            if (wornSet == null || removeFromSet == null || updateWornCosmetics == null || !wornDisplayNames.TryGetValue(entry.Id, out wornDisplayName))
            {
                throw new MissingMethodException("Gorilla Tag's unequip methods were not found.");
            }

            removeFromSet.Invoke(controller, new object[] { wornSet, wornDisplayName, true });
            updateWornCosmetics.Invoke(controller, new object[] { true });
            ReadMember<Action>(controllerType, controller, "OnCosmeticsUpdated")?.Invoke();
        }

        private void RefreshWornSnapshot()
        {
            wornIds.Clear();
            wornDisplayNames.Clear();

            Type controllerType;
            object controller = GetCosmeticsController(out controllerType);
            object wornSet = controller == null ? null : ReadMember<object>(controllerType, controller, "currentWornSet");
            IList wornItems = wornSet == null ? null : ReadMember<IList>(wornSet.GetType(), wornSet, "items");
            Array.Clear(wornSlotEntries, 0, wornSlotEntries.Length);
            if (wornItems != null)
            {
                for (int slot = 0; slot < wornItems.Count; slot++)
                {
                    object wornItem = wornItems[slot];
                    string id = GetItemId(wornItem);
                    if (string.IsNullOrEmpty(id) || ReadMember<bool>(wornItem.GetType(), wornItem, "isNullItem"))
                    {
                        continue;
                    }
                    wornIds.Add(id);
                    wornDisplayNames[id] = ReadMember<string>(wornItem.GetType(), wornItem, "displayName");
                    Item entry;
                    if (slot < wornSlotEntries.Length && entriesById.TryGetValue(id, out entry))
                    {
                        wornSlotEntries[slot] = entry;
                    }
                }
            }
        }

        private static object GetCosmeticsController(out Type controllerType)
        {
            controllerType = Type.GetType("GorillaNetworking.CosmeticsController, Assembly-CSharp");
            return controllerType == null ? null : GetController(controllerType);
        }

        private void TryPurchaseEntry(Item entry)
        {
            if (entry == null || !entry.IsBundle || !entry.CanPurchase)
            {
                return;
            }

            Type managerType = Type.GetType("GorillaNetworking.Store.BundleManager, Assembly-CSharp");
            object manager = managerType == null ? null : GetController(managerType) ?? FindFirstObjectByType(managerType);
            Type providerType = Type.GetType("Cosmetics.ICreatorCodeProvider, Assembly-CSharp");
            object provider = FindBundleCreatorCodeProvider(entry, providerType);
            if (manager == null || provider == null)
            {
                Logger.LogWarning("[SI] Cannot start bundle checkout for " + entry.Id + ": manager=" + (manager != null) + ", creatorCodeProvider=" + (provider != null));
                ShowStatus("Can't buy bundles from this area");
                return;
            }

            foreach (MethodInfo method in managerType.GetMethods(InstanceMembers))
            {
                if (method.Name != "BundlePurchaseButtonPressed")
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 2 || parameters[0].ParameterType != typeof(string) || !parameters[1].ParameterType.IsInstanceOfType(provider))
                {
                    continue;
                }

                try
                {
                    method.Invoke(manager, new object[] { entry.Id, provider });
                    Logger.LogInfo("[SI] Started Gorilla Tag checkout for bundle: " + entry.Id);
                    ShowStatus("Opening checkout for " + entry.DisplayName + "...");
                }
                catch (Exception ex)
                {
                    Exception cause = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                    Logger.LogWarning("[SI] Bundle checkout failed for " + entry.Id + ": " + cause.Message);
                    ShowStatus("Couldn't open the bundle checkout: " + cause.Message);
                }
                return;
            }

            Logger.LogWarning("[SI] Cannot start bundle checkout: BundlePurchaseButtonPressed was not found.");
            ShowStatus("Bundle checkout isn't working right now");
        }

        private static object FindBundleCreatorCodeProvider(Item entry, Type providerType)
        {
            if (providerType == null || entry == null || entry.Source == null)
            {
                return null;
            }

            IEnumerable stands = ReadMember<IEnumerable>(entry.Source.GetType(), entry.Source, "bundleStands");
            if (stands != null)
            {
                foreach (object stand in stands)
                {
                    if (stand == null)
                    {
                        continue;
                    }

                    GameObject providerObject = ReadMember<GameObject>(stand.GetType(), stand, "creatorCodeProvider");
                    object provider = GetCreatorCodeProvider(providerObject, providerType);
                    if (provider != null)
                    {
                        return provider;
                    }

                    object purchaseButton = ReadMember<object>(stand.GetType(), stand, "_bundlePurchaseButton");
                    provider = purchaseButton == null ? null : ReadMember<object>(purchaseButton.GetType(), purchaseButton, "codeProvider");
                    if (providerType.IsInstanceOfType(provider))
                    {
                        return provider;
                    }
                }
            }

            Type standType = Type.GetType("TryOnBundlesStand, Assembly-CSharp");
            object tryOnStand = standType == null ? null : FindFirstObjectByType(standType);
            GameObject tryOnProvider = tryOnStand == null ? null : ReadMember<GameObject>(standType, tryOnStand, "creatorCodeProvider");
            return GetCreatorCodeProvider(tryOnProvider, providerType);
        }

        private static object GetCreatorCodeProvider(GameObject providerObject, Type providerType)
        {
            if (providerObject == null)
            {
                return null;
            }

            Component provider = providerObject.GetComponent(providerType);
            if (provider != null)
            {
                return provider;
            }

            foreach (Component component in providerObject.GetComponentsInChildren<Component>(true))
            {
                if (component != null && providerType.IsInstanceOfType(component))
                {
                    return component;
                }
            }
            return null;
        }

        private void OpenSteamDlcPage(Item entry)
        {
            string url = entry != null && !string.IsNullOrWhiteSpace(entry.SteamStoreUrl) ? entry.SteamStoreUrl : GorillaTagShopUrl;
            Logger.LogInfo("[SI] Opening Gorilla Tag store page in the default browser: " + (entry?.DisplayName ?? "unknown"));
            Application.OpenURL(url);
        }

        private void DrawCart(Rect area)
        {
            if (GetCartCount() == 0)
            {
                GUI.Label(
                    new Rect(area.x, area.y, area.width, Mathf.Min(area.height, 90f)),
                    "Your cart's empty.\nHit CART on something in the Index to try it on.",
                    centerMutedStyle);
                return;
            }

            const float rowHeight = 64f;
            const float rowGap = 6f;
            const float summaryHeight = 30f;

            Rect listRect = new Rect(area.x, area.y, area.width, area.height - summaryHeight - 8f);
            float contentHeight = cartEntries.Count * (rowHeight + rowGap) - rowGap;
            float contentWidth = contentHeight > listRect.height ? listRect.width - 20f : listRect.width;
            Item entryToRemove = null;
            scrollPosition = GUI.BeginScrollView(listRect, scrollPosition, new Rect(0f, 0f, contentWidth, contentHeight));
            for (int index = 0; index < cartEntries.Count; index++)
            {
                Item entry = cartEntries[index];
                Rect row = new Rect(0f, index * (rowHeight + rowGap), contentWidth, rowHeight);
                GUI.Box(row, GUIContent.none);

                Rect stage = new Rect(row.x + 6f, row.y + 6f, 52f, 52f);
                Fill(stage, StageColor);
                DrawSprite(entry.Sprite, new Rect(stage.x + 2f, stage.y + 2f, stage.width - 4f, stage.height - 4f));

                float textWidth = row.width - 68f - 258f;
                GUI.Label(new Rect(row.x + 68f, row.y + 11f, textWidth, RowHeight), entry.DisplayName, rowNameStyle);
                GUI.Label(new Rect(row.x + 68f, row.y + 32f, textWidth, 18f), entry.Category, smallMutedStyle);

                Color priceColor = entry.IsOwned ? Muted :
                    entry.IsFree ? Good :
                    shinyRocks >= 0 && entry.Cost > shinyRocks ? Bad : Color.white;
                ColoredLabel(new Rect(row.xMax - 250f, row.y + 22f, 90f, RowHeight), FormatCost(entry), priceStyle, priceColor);

                Rect buyRect = new Rect(row.xMax - 152f, row.y + 20f, 70f, 24f);
                if (entry.IsOwned)
                {
                    bool previousEnabled = GUI.enabled;
                    GUI.enabled = false;
                    GUI.Button(buyRect, new GUIContent("OWNED", "You already own this"));
                    GUI.enabled = previousEnabled;
                }
                else
                {
                    DrawBuyButton(entry, buyRect);
                }
                if (GUI.Button(new Rect(row.xMax - 76f, row.y + 20f, 70f, 24f), new GUIContent("REMOVE", "Take it out of your cart")))
                {
                    entryToRemove = entry;
                }
            }
            GUI.EndScrollView();

            Rect summary = new Rect(area.x, area.yMax - summaryHeight, area.width, summaryHeight);
            GUI.Box(summary, GUIContent.none);
            GUI.Label(
                new Rect(summary.x + 8f, summary.y + 5f, 250f, RowHeight),
                "<color=#B3B3B3>Total</color>  " + GetCartTotal().ToString() + " SR",
                balanceStyle);
            if (shinyRocks >= 0)
            {
                int remaining = shinyRocks - GetCartTotal();
                ColoredLabel(
                    new Rect(summary.xMax - 308f, summary.y + 5f, 300f, RowHeight),
                    remaining >= 0 ? "You'll have " + remaining.ToString() + " SR left" : "You need " + (-remaining).ToString() + " more Shiny Rocks",
                    priceStyle,
                    remaining >= 0 ? Good : Bad);
            }

            if (entryToRemove != null)
            {
                RemoveFromCart(entryToRemove);
            }
        }

        private void RefreshCartSnapshot()
        {
            cartIds.Clear();
            cartEntries.Clear();
            cartTotal = 0;
            IList currentCart = GetCurrentCart();
            if (currentCart == null)
            {
                return;
            }

            foreach (object cartItem in currentCart)
            {
                string id = GetItemId(cartItem);
                if (id == null || !cartIds.Add(id))
                {
                    continue;
                }
                cartTotal += ReadMember<int>(cartItem.GetType(), cartItem, "cost");
                Item entry;
                if (entriesById.TryGetValue(id, out entry))
                {
                    cartEntries.Add(entry);
                }
            }
        }

        private int GetCartTotal()
        {
            return cartTotal;
        }

        private void ClearCart()
        {
            Type controllerType;
            object controller = GetCosmeticsController(out controllerType);
            MethodInfo clear = controllerType?.GetMethod("ClearCheckoutAndCart", InstanceMembers);
            if (controller == null || clear == null)
            {
                Logger.LogWarning("[SI] Cannot clear cart: ClearCheckoutAndCart is unavailable.");
                ShowStatus("Cart isn't working right now");
                return;
            }
            clear.Invoke(controller, new object[] { false });
            UpdateShoppingCart();
            RefreshCartSnapshot();
            ShowStatus("Cart cleared", 3f);
            Logger.LogInfo("[SI] Cleared Gorilla Tag cart.");
        }

        private int GetCartCount()
        {
            return cartIds.Count;
        }

        private bool IsInCart(Item entry)
        {
            return cartIds.Contains(entry.Id);
        }

        private void AddToCart(Item entry)
        {
            IList currentCart = GetCurrentCart();
            if (currentCart == null)
            {
                Logger.LogWarning("[SI] Cannot add item to cart: currentCart is unavailable.");
                ShowStatus("Cart isn't working right now");
                return;
            }
            if (!IsInCart(entry))
            {
                currentCart.Add(entry.Source);
                UpdateShoppingCart();
                RefreshCartSnapshot();
                ShowCartStatus("Added " + entry.DisplayName + " to cart");
                Logger.LogInfo("[SI] Added item to Gorilla Tag cart: " + entry.Id + " (" + currentCart.Count + " items)");
            }
        }

        private void RemoveFromCart(Item entry)
        {
            IList currentCart = GetCurrentCart();
            if (currentCart == null)
            {
                return;
            }
            for (int index = currentCart.Count - 1; index >= 0; index--)
            {
                if (!string.Equals(GetItemId(currentCart[index]), entry.Id, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                currentCart.RemoveAt(index);
                UpdateShoppingCart();
                RefreshCartSnapshot();
                ShowCartStatus("Took " + entry.DisplayName + " out of cart");
                Logger.LogInfo("[SI] Removed item from Gorilla Tag cart: " + entry.Id + " (" + currentCart.Count + " items)");
                break;
            }
        }

        private static string GetItemId(object item)
        {
            if (item == null)
            {
                return null;
            }

            return ReadMember<string>(item.GetType(), item, "itemName");
        }

        private IList GetCurrentCart()
        {
            Type controllerType = Type.GetType("GorillaNetworking.CosmeticsController, Assembly-CSharp");
            if (controllerType == null)
            {
                return null;
            }
            object controller = GetController(controllerType);
            if (controller == null)
            {
                return null;
            }
            return ReadMember<IList>(controllerType, controller, "currentCart");
        }

        private void UpdateShoppingCart()
        {
            Type controllerType = Type.GetType("GorillaNetworking.CosmeticsController, Assembly-CSharp");
            object controller = controllerType == null ? null : GetController(controllerType);
            if (controller != null)
            {
                controllerType.GetMethod("UpdateShoppingCart", InstanceMembers)?.Invoke(controller, null);
            }
        }

        private static object GetController(Type controllerType)
        {
            FieldInfo field = controllerType.GetField("instance", StaticMembers);
            if (field != null)
            {
                return field.GetValue(null);
            }

            PropertyInfo property = controllerType.GetProperty("instance", StaticMembers);
            return property?.GetValue(null, null);
        }

        private static T ReadMember<T>(Type type, object instance, string memberName)
        {
            FieldInfo field = type.GetField(memberName, InstanceMembers);
            if (field != null)
            {
                object fieldValue = field.GetValue(instance);
                return fieldValue is T typedFieldValue ? typedFieldValue : default(T);
            }

            PropertyInfo property = type.GetProperty(memberName, InstanceMembers);
            object propertyValue = property?.GetValue(instance, null);
            return propertyValue is T typedPropertyValue ? typedPropertyValue : default(T);
        }

        private void DrawSprite(Sprite sprite, Rect destination)
        {
            if (sprite == null || sprite.texture == null)
            {
                return;
            }

            Rect source = sprite.textureRect;
            float sourceAspect = source.width / source.height;
            Rect fitted = destination;
            if (sourceAspect > destination.width / destination.height)
            {
                fitted.height = destination.width / sourceAspect;
                fitted.y += (destination.height - fitted.height) * 0.5f;
            }
            else
            {
                fitted.width = destination.height * sourceAspect;
                fitted.x += (destination.width - fitted.width) * 0.5f;
            }
            Rect uv = new Rect(source.x / sprite.texture.width, source.y / sprite.texture.height, source.width / sprite.texture.width, source.height / sprite.texture.height);
            GUI.DrawTextureWithTexCoords(fitted, sprite.texture, uv, true);
        }

        private void RebuildFilteredEntries()
        {
            filteredEntries.Clear();
            bool bundlesOnly = page == Page.Dlcs;
            bool itemsOnly = page == Page.Rig;
            string search = (itemsOnly ? wardrobeSearchText : searchText).Trim();
            string category = itemsOnly ? wardrobeCategory : selectedCategory;
            foreach (Item entry in entries)
            {
                if (entry.IsBundle != bundlesOnly)
                {
                    continue;
                }
                if (itemsOnly)
                {
                    if (!entry.IsOwned || entry.Category == "Set" || entry.Category == "Collectable")
                    {
                        continue;
                    }
                }
                else if (!entry.IsBundle && !entry.CanPurchase)
                {
                    continue;
                }
                if (search.Length > 0 && entry.SearchText.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                if (!bundlesOnly && !string.Equals(category, "All", StringComparison.OrdinalIgnoreCase) && !string.Equals(entry.Category, category, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!bundlesOnly && !itemsOnly && (entry.IsOwned != ownedOnly || freeOnly && !entry.IsFree || affordableOnly && (shinyRocks < 0 || entry.Cost > shinyRocks)))
                {
                    continue;
                }
                filteredEntries.Add(entry);
            }
            EntrySortMode sortMode = itemsOnly ? EntrySortMode.Name : entrySortMode;
            if (sortMode == EntrySortMode.OldestToNewest)
            {
                filteredEntries.Sort((left, right) => left.CatalogOrder.CompareTo(right.CatalogOrder));
            }
            else if (sortMode == EntrySortMode.NewestToOldest)
            {
                filteredEntries.Sort((left, right) => right.CatalogOrder.CompareTo(left.CatalogOrder));
            }
            else if (sortMode == EntrySortMode.PriceLowToHigh || sortMode == EntrySortMode.PriceHighToLow)
            {
                int direction = sortMode == EntrySortMode.PriceLowToHigh ? 1 : -1;
                filteredEntries.Sort((left, right) => left.Cost != right.Cost
                    ? direction * left.Cost.CompareTo(right.Cost)
                    : StringComparer.OrdinalIgnoreCase.Compare(left.DisplayName, right.DisplayName));
            }
            else
            {
                filteredEntries.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.DisplayName, right.DisplayName));
            }
            if (bundlesOnly)
            {
                int steamDlcIndex = filteredEntries.FindIndex(bundle => bundle.IsSteamDlc);
                if (steamDlcIndex > 0)
                {
                    Item steamDlc = filteredEntries[steamDlcIndex];
                    filteredEntries.RemoveAt(steamDlcIndex);
                    filteredEntries.Insert(0, steamDlc);
                }
            }
            filtersDirty = false;
        }

        private void RefreshEntries()
        {
            nextRefreshTime = Time.unscaledTime + 5f;
            var refreshed = new List<Item>();

            TryLoadCosmetics(refreshed);
            TryLoadBundleManager(refreshed);
            AddKnownSteamDlc(refreshed);
            Item earlyAccessDlc = refreshed.FirstOrDefault(entry => entry.IsSteamDlc);
            if (earlyAccessDlc != null)
            {
                earlyAccessDlc.Sprite = earlyAccessDlcSprite;
                earlyAccessDlc.CanPurchase = steamDlcAvailable;
                earlyAccessDlc.IsFree = steamDlcIsFree;
                earlyAccessDlc.DisplayPrice = steamDlcPriceText;
                if (!earlyAccessDlcImageRequested)
                {
                    earlyAccessDlcImageRequested = true;
                    StartCoroutine(LoadEarlyAccessDlcImage());
                }
            }

            if (refreshed.Count == 0)
            {
                return;
            }

            entries.Clear();
            entries.AddRange(refreshed.OrderBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase));
            entriesById.Clear();
            foreach (Item entry in entries)
            {
                if (!entry.IsBundle)
                {
                    entriesById[entry.Id] = entry;
                }
            }
            categories.Clear();
            categories.Add("All");
            categories.AddRange(entries
                .Where(entry => entry.CanPurchase || entry.IsDlc || entry.IsOwned)
                .Select(entry => entry.Category)
                .Where(category => !string.Equals(category, "Bundle", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(category => category, StringComparer.OrdinalIgnoreCase));
            if (!categories.Contains(selectedCategory, StringComparer.OrdinalIgnoreCase))
            {
                selectedCategory = "All";
            }
            filtersDirty = true;
        }

        private bool TryLoadCosmetics(List<Item> refreshed)
        {
            Type controllerType = Type.GetType("GorillaNetworking.CosmeticsController, Assembly-CSharp");
            if (controllerType == null)
            {
                return false;
            }
            object controller = GetController(controllerType) ?? FindFirstObjectByType(controllerType);
            if (controller == null)
            {
                return false;
            }
            IEnumerable items = ReadMember<IEnumerable>(controllerType, controller, "allCosmetics");
            if (items == null)
            {
                return false;
            }

            shinyRocks = ReadMember<int>(controllerType, controller, "currencyBalance");
            HashSet<string> ownedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IEnumerable unlocked = ReadMember<IEnumerable>(controllerType, controller, "unlockedCosmetics");
            if (unlocked != null)
            {
                foreach (object unlockedItem in unlocked)
                {
                    string unlockedId = GetItemId(unlockedItem);
                    if (unlockedId != null)
                    {
                        ownedIds.Add(unlockedId);
                    }
                }
            }
            HashSet<string> shinyRockPricedIds = ReadShinyRockPricedIds(controllerType, controller);

            var seenIds = new HashSet<string>();
            int catalogOrder = 0;
            foreach (object item in items)
            {
                int sourceOrder = catalogOrder++;
                Item entry = Item.From(item);
                if (entry != null && seenIds.Add(entry.Id))
                {
                    entry.CatalogOrder = sourceOrder;
                    entry.IsOwned = ownedIds.Contains(entry.Id);
                    entry.IsHoldable = ReadMember<bool>(item.GetType(), item, "isHoldable");
                    entry.CanPurchase = shinyRockPricedIds.Count > 0 ? shinyRockPricedIds.Contains(entry.Id) : entry.Cost > 0;
                    entry.IsFree = entry.CanPurchase && entry.Cost == 0;
                    refreshed.Add(entry);
                }
            }
            return refreshed.Count > 0;
        }

        private static HashSet<string> ReadShinyRockPricedIds(Type controllerType, object controller)
        {
            var pricedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string currency = ReadMember<string>(controllerType, controller, "currencyName");
            IEnumerable catalog = ReadMember<IEnumerable>(controllerType, controller, "catalogItems");
            if (string.IsNullOrEmpty(currency) || catalog == null)
            {
                return pricedIds;
            }

            foreach (object catalogItem in catalog)
            {
                if (catalogItem == null)
                {
                    continue;
                }
                string id = ReadMember<string>(catalogItem.GetType(), catalogItem, "ItemId");
                IDictionary prices = ReadMember<IDictionary>(catalogItem.GetType(), catalogItem, "VirtualCurrencyPrices");
                if (id != null && prices != null && prices.Contains(currency))
                {
                    pricedIds.Add(id);
                }
            }
            return pricedIds;
        }

        private void TryLoadBundleManager(List<Item> refreshed)
        {
            Type managerType = Type.GetType("GorillaNetworking.Store.BundleManager, Assembly-CSharp");
            object manager = managerType == null ? null : GetController(managerType) ?? FindFirstObjectByType(managerType);
            var bundleLists = new List<IEnumerable>();
            if (manager != null)
            {
                FieldInfo storeBundlesField = managerType.GetField("_storeBundles", InstanceMembers);
                if (storeBundlesField != null)
                {
                    try
                    {
                        IEnumerable managerBundles = storeBundlesField.GetValue(manager) as IEnumerable;
                        if (managerBundles != null)
                        {
                            bundleLists.Add(managerBundles);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning("[SI] Could not read live Gorilla Tag store bundles: " + ex.Message);
                    }
                }
            }

            Type standType = Type.GetType("TryOnBundlesStand, Assembly-CSharp");
            object stand = manager != null && managerType.GetField("_tryOnBundlesStand", InstanceMembers) != null
                ? ReadMember<object>(managerType, manager, "_tryOnBundlesStand")
                : null;
            stand = stand ?? (standType == null ? null : FindFirstObjectByType(standType));
            if (stand != null && standType != null)
            {
                IEnumerable standBundles = ReadMember<IEnumerable>(standType, stand, "storeBundles");
                if (standBundles != null && !bundleLists.Any(existing => ReferenceEquals(existing, standBundles)))
                {
                    bundleLists.Add(standBundles);
                }
            }

            if (bundleLists.Count == 0 && manager == null)
            {
                bundleCatalogStatus = manager == null && stand == null
                    ? "Bundles aren't loaded in this area"
                    : "Bundles haven't loaded yet";
                return;
            }

            int loadedCount = 0;
            foreach (IEnumerable bundles in bundleLists)
            {
                foreach (object bundle in bundles)
                {
                    Item entry = Item.From(bundle);
                    if (entry == null ||
                        entry.Id.IndexOf("null", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        string.Equals(entry.DisplayName, "Null Bundle", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (refreshed.Any(existing => existing.IsBundle && string.Equals(existing.Id, entry.Id, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    entry.CatalogOrder = refreshed.Count;
                    loadedCount++;
                    entry.IsBundle = true;
                    entry.CanPurchase = manager != null && Item.HasPrice(bundle);
                    entry.IsOwned = Item.ReadOwned(bundle);
                    entry.IsDlc = true;
                    entry.Category = "Bundle";
                    entry.SearchText += " bundle dlc pack";
                    refreshed.Add(entry);
                }
            }

            if (manager != null)
            {
                MethodInfo getStoreBundles = managerType.GetMethod("GetStoreBundles", InstanceMembers);
                if (getStoreBundles != null)
                {
                    try
                    {
                        loadedCount += AddConfiguredBundles(refreshed, getStoreBundles.Invoke(manager, null) as IEnumerable);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning("[SI] Could not read configured Gorilla Tag bundles: " + ex.Message);
                    }
                }

                foreach (string fieldName in new[] { "tryOnBundleButton1", "tryOnBundleButton2", "tryOnBundleButton3", "tryOnBundleButton4", "tryOnBundleButton5" })
                {
                    object configuredBundle = ReadMember<object>(managerType, manager, fieldName);
                    loadedCount += AddConfiguredBundles(refreshed, configuredBundle == null ? null : new object[] { configuredBundle });
                }
            }

            bundleCatalogStatus = loadedCount == 0
                ? "No bundles loaded right now"
                : string.Empty;
        }

        private static int AddConfiguredBundles(List<Item> refreshed, IEnumerable configuredBundles)
        {
            if (configuredBundles == null)
            {
                return 0;
            }

            int loadedCount = 0;
            foreach (object bundleData in configuredBundles)
            {
                Item entry = Item.From(bundleData);
                if (entry == null ||
                    entry.Id.IndexOf("null", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    string.Equals(entry.DisplayName, "Null Bundle", StringComparison.OrdinalIgnoreCase) ||
                    refreshed.Any(existing => existing.IsBundle && string.Equals(existing.Id, entry.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                entry.IsBundle = true;
                entry.IsDlc = true;
                entry.CatalogOrder = refreshed.Count;
                entry.Category = "Bundle";
                entry.SearchText += " bundle dlc pack";
                refreshed.Add(entry);
                loadedCount++;
            }
            return loadedCount;
        }

        private static void AddKnownSteamDlc(List<Item> refreshed)
        {
            const string appId = "1564750";
            if (refreshed.Any(entry => string.Equals(entry.Id, appId, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            refreshed.Add(new Item
            {
                Id = appId,
                DisplayName = "Early Access Supporter Pack",
                CatalogOrder = refreshed.Count,
                Category = "Bundle",
                IsDlc = true,
                IsBundle = true,
                IsSteamDlc = true,
                CanPurchase = false,
                DisplayPrice = "Unavailable",
                SteamStoreUrl = "https://store.steampowered.com/app/1564750/",
                SearchText = "Early Access"
            });
        }

        private IEnumerator LoadEarlyAccessDlcImage()
        {
            using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(EarlyAccessDlcHeaderUrl))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Logger.LogWarning("[SI] Could not load Steam DLC image: " + request.error);
                    yield break;
                }

                Texture2D texture = DownloadHandlerTexture.GetContent(request);
                if (texture == null)
                {
                    Logger.LogWarning("[SI] Steam DLC image response was empty.");
                    yield break;
                }

                earlyAccessDlcSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                Item entry = entries.FirstOrDefault(item => item.IsSteamDlc);
                if (entry != null)
                {
                    entry.Sprite = earlyAccessDlcSprite;
                    filtersDirty = true;
                }
            }
        }

        private IEnumerator MonitorSteamDlcPrice()
        {
            while (true)
            {
                float requestStartedAt = Time.realtimeSinceStartup;
                yield return RefreshSteamDlcPrice();
                float elapsed = Time.realtimeSinceStartup - requestStartedAt;
                yield return new WaitForSecondsRealtime(Mathf.Max(0f, 5f - elapsed));
            }
        }

        private IEnumerator RefreshSteamDlcPrice()
        {
            float startedAt = Time.realtimeSinceStartup;
            ShowSteamDlcPrice("Checking...");

            string priceText;
            using (UnityWebRequest request = UnityWebRequest.Get(EarlyAccessDlcApiUrl))
            {
                request.timeout = 4;
                yield return request.SendWebRequest();
                priceText = ReadSteamDlcPrice(request);
            }

            float remaining = 1f - (Time.realtimeSinceStartup - startedAt);
            if (remaining > 0f)
            {
                yield return new WaitForSecondsRealtime(remaining);
            }
            ShowSteamDlcPrice(priceText);
        }

        private string ReadSteamDlcPrice(UnityWebRequest request)
        {
            if (request.result != UnityWebRequest.Result.Success)
            {
                LogSteamDlcApiError(request.error);
                return "Can't reach Steam";
            }

            SteamDlcApiResponse response;
            try
            {
                byte[] json = request.downloadHandler.data;
                using (var stream = new MemoryStream(json))
                {
                    var serializer = new DataContractJsonSerializer(typeof(SteamDlcApiResponse));
                    response = (SteamDlcApiResponse)serializer.ReadObject(stream);
                }
            }
            catch (Exception ex)
            {
                LogSteamDlcApiError(ex.Message);
                return "Can't reach Steam";
            }

            SteamDlcApiResult app = response == null ? null : response.App;
            if (app == null || !app.Success || app.Data == null)
            {
                LogSteamDlcApiError("Steam returned no app details for DLC 1564750.");
                steamDlcIsFree = false;
                steamDlcAvailable = false;
                return "Not for sale";
            }

            steamDlcLastError = string.Empty;
            steamDlcIsFree = app.Data.IsFree;
            steamDlcAvailable = steamDlcIsFree ||
                app.Data.PriceOverview != null && !string.IsNullOrWhiteSpace(app.Data.PriceOverview.FinalFormatted);
            return steamDlcIsFree ? "Free" :
                steamDlcAvailable ? app.Data.PriceOverview.FinalFormatted : "Not for sale";
        }

        private void ShowSteamDlcPrice(string priceText)
        {
            steamDlcPriceText = priceText;
            foreach (Item entry in entries.Where(item => item.IsSteamDlc))
            {
                entry.CanPurchase = steamDlcAvailable;
                entry.IsFree = steamDlcIsFree;
                entry.DisplayPrice = priceText;
            }
        }

        private void LogSteamDlcApiError(string message)
        {
            if (string.Equals(steamDlcLastError, message, StringComparison.Ordinal))
            {
                return;
            }
            steamDlcLastError = message;
            Logger.LogWarning("[SI] Could not refresh Steam DLC price: " + message);
        }

        [DataContract]
        private sealed class SteamDlcApiResponse
        {
            [DataMember(Name = "1564750")]
            public SteamDlcApiResult App { get; private set; }
        }

        [DataContract]
        private sealed class SteamDlcApiResult
        {
            [DataMember(Name = "success")]
            public bool Success { get; private set; }

            [DataMember(Name = "data")]
            public SteamDlcApiData Data { get; private set; }
        }

        [DataContract]
        private sealed class SteamDlcApiData
        {
            [DataMember(Name = "is_free")]
            public bool IsFree { get; private set; }

            [DataMember(Name = "price_overview")]
            public SteamDlcPriceOverview PriceOverview { get; private set; }
        }

        [DataContract]
        private sealed class SteamDlcPriceOverview
        {
            [DataMember(Name = "final_formatted")]
            public string FinalFormatted { get; private set; }
        }

        private sealed class Item
        {
            public object Source;
            public string Id;
            public string DisplayName;
            public string Category;
            public int Cost;
            public int CatalogOrder;
            public bool IsDlc;
            public bool IsBundle;
            public bool IsSteamDlc;
            public bool CanPurchase;
            public bool IsFree;
            public bool IsOwned;
            public bool IsHoldable;
            public string DisplayPrice;
            public string SteamStoreUrl;
            public Sprite Sprite;
            public string SearchText;

            public static bool HasPrice(object item)
            {
                return item != null && Read<bool>(item.GetType(), item, "HasPrice");
            }

            public static bool ReadOwned(object item)
            {
                return item != null && Read<bool>(item.GetType(), item, "isOwned");
            }

            public static Item From(object item)
            {
                if (item == null)
                {
                    return null;
                }
                Type type = item.GetType();
                string id = ReadAnyString(type, item, "playfabBundleID", "bundleSKU", "itemName", "bundleId", "id", "name", "bundleName");
                if (string.IsNullOrWhiteSpace(id))
                {
                    return null;
                }
                string displayName = ReadAnyString(type, item, "overrideDisplayName", "displayName", "bundleDisplayName", "bundleName", "title", "name");
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = ReadAnyString(type, item, "bundleSKU", "bundleDescriptionText");
                }
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = id;
                }
                object category = ReadAnyObject(type, item, "itemCategory", "bundleCategory", "category", "type");
                string categoryName = category == null ? "Bundle" : category.ToString();
                bool isDlc = LooksLikeDlc(id, displayName, categoryName, type, item);
                return new Item
                {
                    Source = item,
                    Id = id,
                    DisplayName = displayName,
                    Category = categoryName,
                    Cost = ReadAnyInt(type, item, "cost", "price", "bundleCost", "value"),
                    IsDlc = isDlc,
                    DisplayPrice = ReadAnyString(type, item, "price"),
                    SteamStoreUrl = GorillaTagShopUrl,
                    Sprite = ReadAnySprite(type, item, "itemPicture", "bundlePicture", "bundleImage", "sprite"),
                    SearchText = id + " " + displayName + " " + categoryName + (isDlc ? " dlc pack" : string.Empty)
                };
            }

            private static object ReadAnyObject(Type type, object instance, params string[] fieldNames)
            {
                foreach (string fieldName in fieldNames)
                {
                    object value = Read<object>(type, instance, fieldName);
                    if (value != null)
                    {
                        return value;
                    }
                }
                return null;
            }

            private static string ReadAnyString(Type type, object instance, params string[] fieldNames)
            {
                foreach (string fieldName in fieldNames)
                {
                    string value = Read<string>(type, instance, fieldName);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
                return null;
            }

            private static int ReadAnyInt(Type type, object instance, params string[] fieldNames)
            {
                foreach (string fieldName in fieldNames)
                {
                    int value = Read<int>(type, instance, fieldName);
                    if (value != 0)
                    {
                        return value;
                    }
                }
                return 0;
            }

            private static Sprite ReadAnySprite(Type type, object instance, params string[] fieldNames)
            {
                foreach (string fieldName in fieldNames)
                {
                    Sprite value = Read<Sprite>(type, instance, fieldName);
                    if (value != null)
                    {
                        return value;
                    }
                }
                return null;
            }

            private static bool LooksLikeDlc(string id, string displayName, string categoryName, Type type, object item)
            {
                if (string.Equals(categoryName, "DLC", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(categoryName, "Cosmetic Pack", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(categoryName, "CosmeticPack", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                string combined = (id ?? string.Empty) + " " + (displayName ?? string.Empty) + " " + (categoryName ?? string.Empty);
                if (combined.IndexOf("dlc", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    combined.IndexOf("pack", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    combined.IndexOf("bundle", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                object access = Read<object>(type, item, "isDlc");
                if (access is bool isDlcFlag)
                {
                    return isDlcFlag;
                }

                object itemType = Read<object>(type, item, "itemType");
                return itemType != null && itemType.ToString().IndexOf("DLC", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            private static T Read<T>(Type type, object instance, string fieldName)
            {
                FieldInfo field = type.GetField(fieldName, InstanceMembers);
                object value = field == null ? null : field.GetValue(instance);
                if (field == null)
                {
                    PropertyInfo property = type.GetProperty(fieldName, InstanceMembers);
                    value = property == null ? null : property.GetValue(instance, null);
                }
                return value is T typed ? typed : default(T);
            }
        }
    }
}
