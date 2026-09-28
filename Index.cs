using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using BepInEx;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;

namespace ShopIndex
{
    [BepInPlugin("kx.shopindex", "Shop Index", "3.1.1")]
    public sealed class Index : BaseUnityPlugin
    {
        private enum EntrySortMode
        {
            Name,
            OldestToNewest,
            NewestToOldest
        }

        private const int WindowId = 72841;
        private const string GorillaTagShopUrl = "https://store.steampowered.com/app/1533390/";
        private const string EarlyAccessDlcHeaderUrl = "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/1564750/header.jpg?t=1671127857";
        private const string EarlyAccessDlcApiUrl = "https://store.steampowered.com/api/appdetails?appids=1564750&cc=us&l=en";
        private readonly List<Item> entries = new List<Item>();
        private readonly List<Item> filteredEntries = new List<Item>();
        private readonly List<string> categories = new List<string>();
        private Rect windowRect = new Rect(70f, 35f, 650f, 680f);
        private Vector2 scrollPosition;
        private Rect categoryDropdownAnchor;
        private bool showBundlesOnly;
        private EntrySortMode entrySortMode;
        private string searchText = string.Empty;
        private string selectedCategory = "All";
        private string minimumPriceText = string.Empty;
        private string maximumPriceText = string.Empty;
        private bool menuOpen;
        private bool categoryDropdownOpen;
        private bool showCart;
        private bool showAbout;
        private bool windowDragging;
        private Vector2 windowDragOffset;
        private bool filtersDirty = true;
        private string bundleCatalogStatus = "Waiting for Gorilla Tag's bundle catalog.";
        private Sprite earlyAccessDlcSprite;
        private bool earlyAccessDlcImageRequested;
        private bool steamDlcAvailable;
        private bool steamDlcIsFree;
        private string steamDlcPriceText = "Checking Steam...";
        private string steamDlcLastError = string.Empty;
        private float nextRefreshTime;
        private GUIStyle windowStyle;
        private GUIStyle cardStyle;
        private GUIStyle inputStyle;
        private GUIStyle accentButtonStyle;
        private GUIStyle activeButtonStyle;
        private GUIStyle tabButtonStyle;
        private GUIStyle mutedLabelStyle;
        private GUIStyle titleLabelStyle;
        private Texture2D windowTexture;
        private Texture2D cardTexture;
        private Texture2D accentTexture;
        private Texture2D activeTexture;

        private static readonly BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private void Start()
        {
            StartCoroutine(MonitorSteamDlcPrice());
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
            {
                menuOpen = !menuOpen;
                if (menuOpen)
                {
                    RefreshEntries();
                }
            }

            if (menuOpen && Time.unscaledTime >= nextRefreshTime)
            {
                RefreshEntries();
            }
        }

        private void OnGUI()
        {
            if (!menuOpen)
            {
                return;
            }

            if (filtersDirty)
            {
                RebuildFilteredEntries();
            }

            DrawWindow(0);
            windowRect.x = Mathf.Clamp(windowRect.x, 0f, Mathf.Max(0f, Screen.width - windowRect.width));
            windowRect.y = Mathf.Clamp(windowRect.y, 0f, Mathf.Max(0f, Screen.height - windowRect.height));
        }

        private void DrawWindow(int id)
        {
            Rect panelRect = new Rect(windowRect.x, windowRect.y, 650f, 680f);
            HandleWindowDrag(panelRect);
            GUI.Box(panelRect, GUIContent.none);
            GUIStyle centeredTitleStyle = new GUIStyle(GUI.skin.label);
            centeredTitleStyle.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(panelRect.x + 15f, panelRect.y + 5f, 270f, 20f), "SHOPINDEX", centeredTitleStyle);

            string cartNavigationLabel = showCart || showBundlesOnly || showAbout ? "Index" : "Cart (" + GetCartCount() + ")";
            if (GUI.Button(new Rect(panelRect.x + 300f, panelRect.y + 5f, 88f, 20f), cartNavigationLabel))
            {
                bool leavingIndex = showCart || showBundlesOnly || showAbout;
                showCart = !leavingIndex;
                showBundlesOnly = false;
                showAbout = false;
                scrollPosition = Vector2.zero;
                filtersDirty = true;
            }
            Color previousBackgroundColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.15f, 1f, 0.05f, 1f);
            if (GUI.Button(new Rect(panelRect.x + 393f, panelRect.y + 5f, 68f, 20f), "DLCs"))
            {
                showCart = false;
                showBundlesOnly = !showBundlesOnly;
                showAbout = false;
                scrollPosition = Vector2.zero;
                filtersDirty = true;
            }
            GUI.backgroundColor = previousBackgroundColor;
            if (GUI.Button(new Rect(panelRect.x + 466f, panelRect.y + 5f, 75f, 20f), "Refresh"))
            {
                RefreshEntries();
            }
            if (GUI.Button(new Rect(panelRect.x + 546f, panelRect.y + 5f, 68f, 20f), "About"))
            {
                showCart = false;
                showBundlesOnly = false;
                categoryDropdownOpen = false;
                showAbout = !showAbout;
            }
            GUI.backgroundColor = new Color(0.9f, 0.08f, 0.08f, 1f);
            if (GUI.Button(new Rect(panelRect.xMax - 25f, panelRect.y + 5f, 20f, 20f), "X"))
            {
                GUI.backgroundColor = previousBackgroundColor;
                menuOpen = false;
                return;
            }
            GUI.backgroundColor = previousBackgroundColor;
            string oldSearch = searchText;
            searchText = GUI.TextField(new Rect(panelRect.x + 5f, panelRect.y + 30f, panelRect.width - 15f, 22f), searchText);
            if (!string.Equals(oldSearch, searchText, StringComparison.Ordinal))
            {
                filtersDirty = true;
            }

            Rect contentRect = new Rect(panelRect.x + 10f, panelRect.y + 60f, panelRect.width - 20f, panelRect.height - 95f);
            if (categoryDropdownOpen && Event.current.type == EventType.MouseDown)
            {
                Rect dropdownRect = GetCategoryDropdownRect(categories.Count * 26f);
                if (!dropdownRect.Contains(Event.current.mousePosition))
                {
                    categoryDropdownOpen = false;
                }
            }
            GUI.BeginGroup(contentRect);
            if (showAbout)
            {
                GUI.Label(new Rect(0f, 0f, contentRect.width, 20f), "SHOPINDEX Made by KX5QZ!");
                GUI.Label(new Rect(0f, 22f, contentRect.width, 20f), "Browse Gorilla Tag cosmetics and DLC's.");
                GUI.Label(new Rect(0f, 44f, contentRect.width, 20f), "Press F8 to close.");
            }
            else
            {
                bool indexView = !showCart && !showBundlesOnly;
                float contentY = 0f;
                if (indexView)
                {
                    GUI.Label(new Rect(0f, contentY, 62f, 20f), "Category");
                    categoryDropdownAnchor = new Rect(contentRect.x + 62f, contentRect.y + contentY, 120f, 22f);
                    if (GUI.Button(new Rect(62f, contentY, 120f, 22f), selectedCategory))
                    {
                        categoryDropdownOpen = !categoryDropdownOpen;
                    }
                    GUI.Label(new Rect(190f, contentY, 28f, 20f), "Min");
                    minimumPriceText = GUI.TextField(new Rect(220f, contentY, 58f, 22f), minimumPriceText);
                    GUI.Label(new Rect(286f, contentY, 30f, 20f), "Max");
                    maximumPriceText = GUI.TextField(new Rect(318f, contentY, 58f, 22f), maximumPriceText);
                    if (GUI.Button(new Rect(384f, contentY, 150f, 22f), "Sort: " + GetSortLabel()))
                    {
                        entrySortMode = (EntrySortMode)(((int)entrySortMode + 1) % 3);
                        scrollPosition = Vector2.zero;
                        filtersDirty = true;
                    }
                    contentY += 30f;
                }
                GUI.BeginGroup(new Rect(0f, contentY, contentRect.width, contentRect.height - contentY));
                GUILayout.BeginArea(new Rect(0f, 0f, contentRect.width, contentRect.height - contentY));
                bool previousGuiEnabled = GUI.enabled;
                if (categoryDropdownOpen)
                {
                    GUI.enabled = false;
                }
                if (showCart)
                {
                    DrawCart();
                }
                else
                {
                    DrawCatalog();
                }
                GUI.enabled = previousGuiEnabled;
                GUILayout.EndArea();
                GUI.EndGroup();
            }
            GUI.EndGroup();

            if (categoryDropdownOpen && !showCart && !showBundlesOnly && !showAbout)
            {
                DrawCategoryDropdownOverlay();
            }

        }

        private void HandleWindowDrag(Rect panelRect)
        {
            const float titleBarHeight = 25f;
            Rect dragRect = new Rect(panelRect.x, panelRect.y, 295f, titleBarHeight);
            Event currentEvent = Event.current;

            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && dragRect.Contains(currentEvent.mousePosition))
            {
                windowDragging = true;
                windowDragOffset = currentEvent.mousePosition - new Vector2(windowRect.x, windowRect.y);
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
            else if (currentEvent.type == EventType.MouseUp && currentEvent.button == 0)
            {
                windowDragging = false;
            }
        }

        private string GetSortLabel()
        {
            return entrySortMode == EntrySortMode.OldestToNewest ? "Oldest to Newest" :
                entrySortMode == EntrySortMode.NewestToOldest ? "Newest to Oldest" : "Name A-Z";
        }

        private void EnsureStyles()
        {
            if (windowStyle != null)
            {
                return;
            }

            windowTexture = MakeTexture(new Color(0.02f, 0.018f, 0.014f, 0.68f));
            cardTexture = MakeTexture(new Color(0.03f, 0.03f, 0.03f, 0.28f));
            accentTexture = MakeTexture(new Color(0.45f, 0.45f, 0.45f, 0.72f));
            activeTexture = MakeTexture(new Color(0.58f, 0.58f, 0.58f, 0.82f));

            windowStyle = new GUIStyle(GUI.skin.window);
            windowStyle.padding = new RectOffset(0, 0, 0, 0);
            windowStyle.fontSize = 13;
            windowStyle.normal.background = windowTexture;
            windowStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);

            cardStyle = new GUIStyle(GUI.skin.box);
            cardStyle.padding = new RectOffset(8, 8, 8, 8);
            cardStyle.normal.background = cardTexture;
            cardStyle.normal.textColor = new Color(0.9f, 0.92f, 0.94f);

            mutedLabelStyle = new GUIStyle(GUI.skin.label);
            mutedLabelStyle.fontSize = 10;
            mutedLabelStyle.fontStyle = FontStyle.Bold;
            mutedLabelStyle.normal.textColor = new Color(0.55f, 0.64f, 0.68f);

            titleLabelStyle = new GUIStyle(GUI.skin.label);
            titleLabelStyle.fontSize = 12;
            titleLabelStyle.fontStyle = FontStyle.Bold;
            titleLabelStyle.alignment = TextAnchor.MiddleCenter;
            titleLabelStyle.normal.textColor = new Color(0.78f, 0.78f, 0.78f);

            inputStyle = new GUIStyle(GUI.skin.textField);
            inputStyle.padding = new RectOffset(9, 9, 5, 5);
            inputStyle.fontSize = 11;
            inputStyle.normal.background = cardTexture;
            inputStyle.normal.textColor = new Color(0.78f, 0.78f, 0.78f);
            inputStyle.focused.background = cardTexture;
            inputStyle.focused.textColor = Color.white;

            accentButtonStyle = MakeButtonStyle(accentTexture);
            activeButtonStyle = MakeButtonStyle(activeTexture);
            tabButtonStyle = MakeButtonStyle(accentTexture);
        }

        private static GUIStyle MakeButtonStyle(Texture2D background)
        {
            GUIStyle style = new GUIStyle(GUI.skin.button);
            style.padding = new RectOffset(8, 8, 5, 5);
            style.fontSize = 10;
            style.fontStyle = FontStyle.Bold;
            style.alignment = TextAnchor.MiddleCenter;
            style.normal.background = background;
            style.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
            style.hover.background = background;
            style.hover.textColor = Color.white;
            style.active.background = background;
            style.active.textColor = Color.white;
            return style;
        }

        private static Texture2D MakeTexture(Color color)
        {
            Texture2D texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private void DrawCategoryDropdownOverlay()
        {
            const float rowHeight = 26f;
            float contentHeight = categories.Count * rowHeight;
            Rect dropdownRect = GetCategoryDropdownRect(contentHeight);
            GUI.Box(dropdownRect, GUIContent.none);
            for (int index = 0; index < categories.Count; index++)
            {
                Rect optionRect = new Rect(dropdownRect.x, dropdownRect.y + index * rowHeight, dropdownRect.width, rowHeight);
                if (GUI.Button(optionRect, categories[index]))
                {
                    selectedCategory = categories[index];
                    categoryDropdownOpen = false;
                    filtersDirty = true;
                }
            }
        }

        private Rect GetCategoryDropdownRect(float contentHeight)
        {
            return new Rect(categoryDropdownAnchor.x, categoryDropdownAnchor.yMax + 2f, categoryDropdownAnchor.width, contentHeight);
        }

        private void DrawCatalog()
        {
            if (filteredEntries.Count == 0)
            {
                bool showingUnfilteredBundles = showBundlesOnly && string.IsNullOrWhiteSpace(searchText);
                string message = entries.Count == 0 ? "Loading store items." :
                    showingUnfilteredBundles && !string.IsNullOrWhiteSpace(bundleCatalogStatus) ? bundleCatalogStatus : "No matching items.";
                GUILayout.Label(message);
                return;
            }

            Rect viewport = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            const float cardHeight = 208f;
            const float gap = 8f;
            float usableWidth = Mathf.Max(220f, viewport.width - 20f);
            int columns = Mathf.Max(1, Mathf.FloorToInt((usableWidth + gap) / (154f + gap)));
            float cardWidth = (usableWidth - gap * (columns - 1)) / columns;
            int rows = Mathf.CeilToInt(filteredEntries.Count / (float)columns);
            float contentWidth = usableWidth;
            float contentHeight = rows * (cardHeight + gap);
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
                    DrawCard(filteredEntries[index], new Rect(column * (cardWidth + gap), row * (cardHeight + gap), cardWidth, cardHeight));
                }
            }
            GUI.EndScrollView();
        }

        private void DrawCard(Item entry, Rect cardRect)
        {
            GUI.Box(cardRect, GUIContent.none);
            Rect imageRect = new Rect(cardRect.x + 8f, cardRect.y + 8f, cardRect.width - 16f, 92f);
            DrawSprite(entry.Sprite, imageRect);
            GUI.Label(new Rect(cardRect.x + 8f, cardRect.y + 105f, cardRect.width - 16f, 30f), entry.DisplayName);
            string price = string.IsNullOrWhiteSpace(entry.DisplayPrice) ? entry.Cost.ToString() : entry.DisplayPrice;
            if (entry.IsBundle && !entry.CanPurchase)
            {
                price = string.IsNullOrWhiteSpace(entry.DisplayPrice) ? "Price unavailable" : entry.DisplayPrice;
            }
            GUI.Label(new Rect(cardRect.x + 8f, cardRect.y + 139f, cardRect.width - 16f, 18f), entry.Category + "   " + price);

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
            else if (IsInCart(entry))
            {
                if (GUI.Button(buttonRect, "REMOVE"))
                {
                    RemoveFromCart(entry);
                }
            }
            else if (GUI.Button(buttonRect, "ADD TO CART"))
            {
                AddToCart(entry);
            }
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
                }
                catch (Exception ex)
                {
                    Logger.LogWarning("[SI] Bundle checkout failed for " + entry.Id + ": " + ex.Message);
                }
                return;
            }

            Logger.LogWarning("[SI] Cannot start bundle checkout: BundlePurchaseButtonPressed was not found.");
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

        private void DrawCart()
        {
            if (GetCartCount() == 0)
            {
                GUILayout.Label("Your cart is empty.");
                return;
            }

            scrollPosition = GUILayout.BeginScrollView(scrollPosition);
            foreach (Item entry in entries)
            {
                if (!IsInCart(entry))
                {
                    continue;
                }
                GUILayout.BeginHorizontal(GUI.skin.box, GUILayout.Height(70f));
                Rect imageRect = GUILayoutUtility.GetRect(58f, 54f, GUILayout.Width(58f));
                DrawSprite(entry.Sprite, imageRect);
                GUILayout.BeginVertical();
                GUILayout.Label(entry.DisplayName);
                GUILayout.Label(entry.Category + "   " + entry.Cost);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Remove", GUILayout.Width(70f)))
                {
                    RemoveFromCart(entry);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        private int GetCartCount()
        {
            IList currentCart = GetCurrentCart();
            return currentCart == null ? 0 : currentCart.Count;
        }

        private bool IsInCart(Item entry)
        {
            IList currentCart = GetCurrentCart();
            if (currentCart == null)
            {
                return false;
            }

            foreach (object cartItem in currentCart)
            {
                if (string.Equals(GetItemId(cartItem), entry.Id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private void AddToCart(Item entry)
        {
            IList currentCart = GetCurrentCart();
            if (currentCart == null)
            {
                Logger.LogWarning("[SI] Cannot add item to cart: currentCart is unavailable.");
                return;
            }
            if (!IsInCart(entry))
            {
                currentCart.Add(entry.Source);
                UpdateShoppingCart();
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
            int minimum;
            int maximum;
            bool hasMinimum = int.TryParse(minimumPriceText, out minimum);
            bool hasMaximum = int.TryParse(maximumPriceText, out maximum);
            foreach (Item entry in entries)
            {
                if (entry.IsBundle != showBundlesOnly)
                {
                    continue;
                }
                if (entry.Cost <= 0 && !entry.IsBundle)
                {
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(searchText) && entry.SearchText.IndexOf(searchText.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                if (!showBundlesOnly && !string.Equals(selectedCategory, "All", StringComparison.OrdinalIgnoreCase) && !string.Equals(entry.Category, selectedCategory, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!showBundlesOnly && ((hasMinimum && entry.Cost < minimum) || (hasMaximum && entry.Cost > maximum)))
                {
                    continue;
                }
                filteredEntries.Add(entry);
            }
            if (entrySortMode == EntrySortMode.OldestToNewest)
            {
                filteredEntries.Sort((left, right) => left.CatalogOrder.CompareTo(right.CatalogOrder));
            }
            else if (entrySortMode == EntrySortMode.NewestToOldest)
            {
                filteredEntries.Sort((left, right) => right.CatalogOrder.CompareTo(left.CatalogOrder));
            }
            else
            {
                filteredEntries.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.DisplayName, right.DisplayName));
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
            categories.Clear();
            categories.Add("All");
            categories.AddRange(entries
                .Where(entry => entry.Cost > 0 || entry.IsDlc)
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

            int catalogOrder = 0;
            foreach (object item in items)
            {
                int sourceOrder = catalogOrder++;
                Item entry = Item.From(item);
                if (entry != null && !refreshed.Any(existing => existing.Id == entry.Id))
                {
                    entry.CatalogOrder = sourceOrder;
                    refreshed.Add(entry);
                }
            }
            return refreshed.Count > 0;
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

            if (bundleLists.Count == 0)
            if (bundleLists.Count == 0 && manager == null)
            {
                bundleCatalogStatus = manager == null && stand == null
                    ? "Gorilla Tag's bundle store is not loaded in this area."
                    : "Gorilla Tag has not loaded its bundle list yet.";
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
                ? "No bundles are currently loaded by Gorilla Tag."
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
                yield return new WaitForSecondsRealtime(Mathf.Max(0f, 10f - elapsed));
            }
        }

        private IEnumerator RefreshSteamDlcPrice()
        {
            using (UnityWebRequest request = UnityWebRequest.Get(EarlyAccessDlcApiUrl))
            {
                request.timeout = 8;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    LogSteamDlcApiError(request.error);
                    yield break;
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
                    yield break;
                }

                SteamDlcApiResult app = response == null ? null : response.App;
                if (app == null || !app.Success || app.Data == null)
                {
                    LogSteamDlcApiError("Steam returned no app details for DLC 1564750.");
                    yield break;
                }

                steamDlcLastError = string.Empty;
                steamDlcIsFree = app.Data.IsFree;
                steamDlcAvailable = steamDlcIsFree ||
                    app.Data.PriceOverview != null && !string.IsNullOrWhiteSpace(app.Data.PriceOverview.FinalFormatted);
                steamDlcPriceText = steamDlcIsFree ? "Free" :
                    steamDlcAvailable ? app.Data.PriceOverview.FinalFormatted : "Delisted on Steam";

                foreach (Item entry in entries.Where(item => item.IsSteamDlc))
                {
                    entry.CanPurchase = steamDlcAvailable;
                    entry.IsFree = steamDlcIsFree;
                    entry.DisplayPrice = steamDlcPriceText;
                }
                filtersDirty = true;
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
