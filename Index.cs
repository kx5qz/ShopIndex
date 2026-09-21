using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ShopIndex
{
    [BepInPlugin("kx.shopindex", "Shop Index", "3.0.1")]
    public sealed class Index : BaseUnityPlugin
    {
        private const int WindowId = 72841;
        private readonly List<Item> entries = new List<Item>();
        private readonly List<Item> filteredEntries = new List<Item>();
        private readonly List<string> categories = new List<string>();
        private Rect windowRect = new Rect(70f, 35f, 760f, 700f);
        private Vector2 scrollPosition;
        private string searchText = string.Empty;
        private string selectedCategory = "All";
        private string minimumPriceText = string.Empty;
        private string maximumPriceText = string.Empty;
        private bool menuOpen;
        private bool showFilters;
        private bool categoryDropdownOpen;
        private bool showCart;
        private bool filtersDirty = true;
        private float nextRefreshTime;

        private static readonly BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

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

            windowRect = GUI.Window(WindowId, windowRect, DrawWindow, "SHOP INDEX");
            windowRect.x = Mathf.Clamp(windowRect.x, 0f, Mathf.Max(0f, Screen.width - windowRect.width));
            windowRect.y = Mathf.Clamp(windowRect.y, 0f, Mathf.Max(0f, Screen.height - windowRect.height));
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginArea(new Rect(10f, 24f, windowRect.width - 20f, windowRect.height - 34f));
            GUILayout.BeginHorizontal();
            string oldSearch = searchText;
            searchText = GUILayout.TextField(searchText, GUILayout.Height(24f));
            if (!string.Equals(oldSearch, searchText, StringComparison.Ordinal))
            {
                filtersDirty = true;
            }
            if (GUILayout.Button(showFilters ? "Hide Filters" : "Filters", GUILayout.Width(82f), GUILayout.Height(24f)))
            {
                showFilters = !showFilters;
            }
            string indexButtonText = showCart ? "INDEX" : "CART (" + GetCartCount() + ")";
            if (GUILayout.Button(indexButtonText, GUILayout.Width(100f), GUILayout.Height(24f)))
            {
                showCart = !showCart;
                categoryDropdownOpen = false;
                scrollPosition = Vector2.zero;
            }
            GUILayout.EndHorizontal();

            if (showFilters)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Category", GUILayout.Width(55f));
                if (GUILayout.Button(selectedCategory, GUILayout.Width(110f)))
                {
                    categoryDropdownOpen = !categoryDropdownOpen;
                }
                GUILayout.Label("Min", GUILayout.Width(22f));
                string oldMinimum = minimumPriceText;
                minimumPriceText = GUILayout.TextField(minimumPriceText, GUILayout.Width(52f));
                GUILayout.Label("Max", GUILayout.Width(25f));
                string oldMaximum = maximumPriceText;
                maximumPriceText = GUILayout.TextField(maximumPriceText, GUILayout.Width(52f));
                if (oldMinimum != minimumPriceText || oldMaximum != maximumPriceText)
                {
                    filtersDirty = true;
                }
                GUILayout.EndHorizontal();

                if (categoryDropdownOpen)
                {
                    GUILayout.BeginVertical(GUI.skin.box);
                    foreach (string category in categories)
                    {
                        if (GUILayout.Button(category))
                        {
                            selectedCategory = category;
                            categoryDropdownOpen = false;
                            filtersDirty = true;
                        }
                    }
                    GUILayout.EndVertical();
                }
            }

            GUILayout.Space(5f);
            if (showCart)
            {
                DrawCart();
            }
            else
            {
                DrawCatalog();
            }
            GUILayout.EndArea();
            GUI.DragWindow(new Rect(0f, 0f, windowRect.width, 22f));
        }

        private void DrawCatalog()
        {
            if (filteredEntries.Count == 0)
            {
                GUILayout.Label(entries.Count == 0 ? "Loading cosmetics..." : "No matching cosmetics.");
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
            GUI.Label(new Rect(cardRect.x + 8f, cardRect.y + 139f, cardRect.width - 16f, 18f), entry.Category + "   " + entry.Cost);
            Rect buttonRect = new Rect(cardRect.x + 8f, cardRect.yMax - 34f, cardRect.width - 16f, 26f);
            if (IsInCart(entry))
            {
                if (GUI.Button(buttonRect, "Remove"))
                {
                    RemoveFromCart(entry);
                }
            }
            else if (GUI.Button(buttonRect, "Add to cart"))
            {
                AddToCart(entry);
            }
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
                if (entry.Cost <= 0)
                {
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(searchText) && entry.SearchText.IndexOf(searchText.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                if (!string.Equals(selectedCategory, "All", StringComparison.OrdinalIgnoreCase) && !string.Equals(entry.Category, selectedCategory, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (hasMinimum && entry.Cost < minimum || hasMaximum && entry.Cost > maximum)
                {
                    continue;
                }
                filteredEntries.Add(entry);
            }
            filtersDirty = false;
        }

        private void RefreshEntries()
        {
            nextRefreshTime = Time.unscaledTime + 5f;
            Type controllerType = Type.GetType("GorillaNetworking.CosmeticsController, Assembly-CSharp");
            if (controllerType == null)
            {
                return;
            }
            object controller = GetController(controllerType) ?? FindFirstObjectByType(controllerType);
            if (controller == null)
            {
                return;
            }
            IEnumerable items = ReadMember<IEnumerable>(controllerType, controller, "allCosmetics");
            if (items == null)
            {
                return;
            }

            List<Item> refreshed = new List<Item>();
            foreach (object item in items)
            {
                Item entry = Item.From(item);
                if (entry != null && !refreshed.Any(existing => existing.Id == entry.Id))
                {
                    refreshed.Add(entry);
                }
            }
            entries.Clear();
            entries.AddRange(refreshed.OrderBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase));
            categories.Clear();
            categories.Add("All");
            categories.AddRange(entries
                .Where(entry => entry.Cost > 0)
                .Select(entry => entry.Category)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(category => category, StringComparer.OrdinalIgnoreCase));
            if (!categories.Contains(selectedCategory, StringComparer.OrdinalIgnoreCase))
            {
                selectedCategory = "All";
            }
            filtersDirty = true;
        }

        private sealed class Item
        {
            public object Source;
            public string Id;
            public string DisplayName;
            public string Category;
            public int Cost;
            public Sprite Sprite;
            public string SearchText;

            public static Item From(object item)
            {
                if (item == null)
                {
                    return null;
                }
                Type type = item.GetType();
                string id = Read<string>(type, item, "itemName");
                if (string.IsNullOrWhiteSpace(id))
                {
                    return null;
                }
                string displayName = Read<string>(type, item, "overrideDisplayName");
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = Read<string>(type, item, "displayName");
                }
                object category = Read<object>(type, item, "itemCategory");
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = id;
                }
                string categoryName = category == null ? "Unknown" : category.ToString();
                return new Item
                {
                    Source = item,
                    Id = id,
                    DisplayName = displayName,
                    Category = categoryName,
                    Cost = Read<int>(type, item, "cost"),
                    Sprite = Read<Sprite>(type, item, "itemPicture"),
                    SearchText = id + " " + displayName + " " + categoryName
                };
            }

            private static T Read<T>(Type type, object instance, string fieldName)
            {
                FieldInfo field = type.GetField(fieldName, InstanceMembers);
                object value = field == null ? null : field.GetValue(instance);
                return value is T typed ? typed : default(T);
            }
        }
    }
}
