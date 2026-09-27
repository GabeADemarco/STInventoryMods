using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace STStockPlanner
{
    [BepInPlugin("gabe.supermarkettogether.stockplanner", "Stock Planner", "0.2.2")]
    public sealed class StockPlanner : BaseUnityPlugin
    {
        private ConfigEntry<bool> enabledOnStart, includeFullShelves, diagnosticLogging;
        private ConfigEntry<int> reserveBoxes, checkIntervalMinutes;
        private ConfigEntry<KeyboardShortcut> toggleKey;
        private bool enabled;
        private float nextCheck;
        private Type boardType, npcType, containerType, listingType, listEntryType, canvasType;
        private FieldInfo npcInstance, listingInstance, shelvesField, productsField;
        private FieldInfo shoppingParentField, productInfoField, entryIdField, maxBoxField;
        private FieldInfo canvasInstance;
        private MethodInfo maxPerRowMethod, existencesMethod, priceMethod, addMethod, notifyMethod;

        private void Awake()
        {
            enabledOnStart = Config.Bind("Automation", "EnabledOnStart", false,
                "If true, stock planning starts automatically when the game loads. F10 toggles it during play.");
            includeFullShelves = Config.Bind("StockTarget", "IncludeFullShelves", true,
                "Include all assigned shelf-row capacities in the target stock.");
            reserveBoxes = Config.Bind("StockTarget", "ReserveBoxes", 2,
                "Additional full boxes to keep beyond the optional full shelf target. Minimum 0.");
            checkIntervalMinutes = Config.Bind("Automation", "CheckIntervalMinutes", 1,
                new ConfigDescription("Minutes between stock checks. Minimum 1. Change in the config file and restart the game.",
                    new AcceptableValueRange<int>(1, 1440)));
            diagnosticLogging = Config.Bind("Diagnostics", "DiagnosticLogging", false,
                "Log per-product shelf capacity, owned stock, queued boxes, target, and shortfall at every check. Change in the config file and restart the game.");
            toggleKey = Config.Bind("Controls", "ToggleAutomation", new KeyboardShortcut(KeyCode.F10),
                "Turn automatic shopping-list additions on or off.");
            if (reserveBoxes.Value < 0) reserveBoxes.Value = 0;

            boardType = AccessTools.TypeByName("ManagerBlackboard");
            npcType = AccessTools.TypeByName("NPC_Manager");
            containerType = AccessTools.TypeByName("Data_Container");
            listingType = AccessTools.TypeByName("ProductListing");
            listEntryType = AccessTools.TypeByName("InteractableData");
            canvasType = AccessTools.TypeByName("GameCanvas");
            if (boardType == null || npcType == null || containerType == null ||
                listingType == null || listEntryType == null)
            {
                Logger.LogError("Required game classes were not found; Stock Planner is disabled.");
                return;
            }
            npcInstance = AccessTools.Field(npcType, "Instance");
            listingInstance = AccessTools.Field(listingType, "Instance");
            shelvesField = AccessTools.Field(npcType, "shelvesOBJ");
            productsField = AccessTools.Field(listingType, "productsData");
            shoppingParentField = AccessTools.Field(boardType, "shoppingListParent");
            productInfoField = AccessTools.Field(containerType, "productInfoArray");
            entryIdField = AccessTools.Field(listEntryType, "thisSkillIndex");
            maxPerRowMethod = AccessTools.Method(npcType, "GetMaxProductsPerRow", new[] { typeof(int), typeof(int) });
            existencesMethod = AccessTools.Method(boardType, "GetProductsExistences", new[] { typeof(int) });
            priceMethod = AccessTools.Method(boardType, "PricePerBoxRetrieve", new[] { typeof(int) });
            addMethod = AccessTools.Method(boardType, "AddShoppingListProduct", new[] { typeof(int), typeof(float) });
            if (canvasType != null)
            {
                canvasInstance = AccessTools.Field(canvasType, "Instance");
                notifyMethod = AccessTools.Method(canvasType, "CreateCanvasNotification", new[] { typeof(string) });
                var localizer = AccessTools.TypeByName("LocalizationManager");
                var lookup = localizer == null ? null : AccessTools.Method(localizer, "GetLocalizationString", new[] { typeof(string) });
                if (lookup != null)
                    new Harmony("gabe.supermarkettogether.stockplanner").Patch(lookup,
                        prefix: new HarmonyMethod(typeof(StockPlanner), nameof(RawNotificationPrefix)));
            }
            if (npcInstance == null || listingInstance == null || shelvesField == null || productsField == null ||
                shoppingParentField == null || productInfoField == null || entryIdField == null ||
                maxPerRowMethod == null || existencesMethod == null || priceMethod == null || addMethod == null)
            {
                Logger.LogError("Required game fields or methods were not found; Stock Planner is disabled.");
                return;
            }
            enabled = enabledOnStart.Value;
            nextCheck = Time.unscaledTime + 5f;
            Logger.LogInfo("Stock Planner ready; automatic list additions " + (enabled ? "ON" : "OFF") + ".");
        }

        private void Update()
        {
            if (addMethod == null || npcInstance == null || shoppingParentField == null) return;
            if (toggleKey.Value.IsDown())
            {
                enabled = !enabled;
                nextCheck = Time.unscaledTime;
                Notify("Stock Planner: " + (enabled ? "ON" : "OFF"));
            }
            if (!enabled || Time.unscaledTime < nextCheck || addMethod == null) return;
            nextCheck = Time.unscaledTime + checkIntervalMinutes.Value * 60f;
            try { CheckAndAdd(); }
            catch (Exception e) { Logger.LogError("Stock check failed: " + e); }
        }

        private void CheckAndAdd()
        {
            object npc = npcInstance.GetValue(null);
            object listing = listingInstance.GetValue(null);
            object board = FindSceneBlackboard();
            if (npc == null || listing == null || board == null) return;
            GameObject shelves = shelvesField.GetValue(npc) as GameObject;
            GameObject shoppingParent = shoppingParentField.GetValue(board) as GameObject;
            Array products = productsField.GetValue(listing) as Array;
            if (shelves == null || shoppingParent == null || products == null) return;

            var capacities = new Dictionary<int, int>();
            Transform shelfRows = shelves.transform;
            for (int row = 0; row < shelfRows.childCount; row++)
            {
                Component container = shelfRows.GetChild(row).GetComponent(containerType);
                if (container == null) continue;
                int[] info = productInfoField.GetValue(container) as int[];
                if (info == null) continue;
                var assignedSlots = new Dictionary<int, int>();
                for (int i = 0; i + 1 < info.Length; i += 2)
                {
                    int id = info[i];
                    if (id >= 0 && id < products.Length)
                        assignedSlots[id] = (assignedSlots.ContainsKey(id) ? assignedSlots[id] : 0) + 1;
                }
                foreach (var assignment in assignedSlots)
                {
                    int id = assignment.Key;
                    int capacity = (int)maxPerRowMethod.Invoke(npc, new object[] { row, id });
                    if (diagnosticLogging.Value)
                    {
                        int currentUnits = 0;
                        for (int i = 0; i + 1 < info.Length; i += 2)
                            if (info[i] == id) currentUnits += info[i + 1];
                        Logger.LogInfo("Shelf row " + row + ": product=" + id +
                            ", rowObject=" + shelfRows.GetChild(row).name +
                            ", assignedSlots=" + assignment.Value +
                            ", currentUnits=" + currentUnits + ", capacityPerSlot=" + capacity +
                            ", totalCapacity=" + (long)capacity * assignment.Value);
                    }
                    if (capacity > 0)
                        capacities[id] = (capacities.ContainsKey(id) ? capacities[id] : 0) + capacity * assignment.Value;
                }
            }
            if (capacities.Count == 0) return;

            var queued = new Dictionary<int, int>();
            Transform list = shoppingParent.transform;
            for (int i = 0; i < list.childCount; i++)
            {
                Component entry = list.GetChild(i).GetComponent(listEntryType);
                if (entry == null) continue;
                int id = (int)entryIdField.GetValue(entry);
                queued[id] = (queued.ContainsKey(id) ? queued[id] : 0) + 1;
            }

            int added = 0;
            foreach (var item in capacities.OrderBy(x => x.Key))
            {
                int id = item.Key;
                object product = products.GetValue(id);
                if (product == null) continue;
                if (maxBoxField == null) maxBoxField = AccessTools.Field(product.GetType(), "maxItemsPerBox");
                if (maxBoxField == null) continue;
                int unitsPerBox = (int)maxBoxField.GetValue(product);
                if (unitsPerBox <= 0) continue;
                int[] amounts = existencesMethod.Invoke(board, new object[] { id }) as int[];
                if (amounts == null || amounts.Length < 3) continue;
                long target = (includeFullShelves.Value ? item.Value : 0) + (long)Math.Max(0, reserveBoxes.Value) * unitsPerBox;
                long owned = (long)amounts[0] + amounts[1] + amounts[2];
                long pending = (long)(queued.ContainsKey(id) ? queued[id] : 0) * unitsPerBox;
                long shortfall = target - owned - pending;
                if (diagnosticLogging.Value)
                    Logger.LogInfo("Product " + id + ": capacity=" + item.Value +
                        ", unitsPerBox=" + unitsPerBox + ", shelves=" + amounts[0] +
                        ", storage=" + amounts[1] + ", boxes=" + amounts[2] +
                        ", queuedBoxes=" + (queued.ContainsKey(id) ? queued[id] : 0) +
                        ", target=" + target + ", shortfall=" + shortfall +
                        ", boxesToAdd=" + (shortfall > 0 ? Math.Min(100, (shortfall + unitsPerBox - 1) / unitsPerBox) : 0));
                if (shortfall <= 0) continue;
                int boxes = (int)Math.Min(100, (shortfall + unitsPerBox - 1) / unitsPerBox);
                float price = (float)priceMethod.Invoke(board, new object[] { id });
                for (int j = 0; j < boxes; j++)
                {
                    addMethod.Invoke(board, new object[] { id, price });
                    added++;
                }
            }
            if (added > 0) Notify("Stock Planner: added " + added + " box" + (added == 1 ? "" : "es"));
            Logger.LogInfo("Stock check: " + capacities.Count + " assigned products, " + added + " boxes added to shopping list.");
        }

        private object FindSceneBlackboard()
        {
            foreach (UnityEngine.Object candidate in Resources.FindObjectsOfTypeAll(boardType))
            {
                Component component = candidate as Component;
                if (component != null && component.gameObject.scene.IsValid()) return candidate;
            }
            return null;
        }

        private void Notify(string text)
        {
            try
            {
                object canvas = canvasInstance == null ? null : canvasInstance.GetValue(null);
                if (canvas != null && notifyMethod != null)
                    notifyMethod.Invoke(canvas, new object[] { "`" + text });
                else Logger.LogInfo(text);
            }
            catch (Exception e) { Logger.LogWarning("Notification failed: " + e); }
        }

        private static bool RawNotificationPrefix(string __0, ref string __result)
        {
            if (!string.IsNullOrEmpty(__0) && __0[0] == '`')
            {
                __result = __0.Substring(1);
                return false;
            }
            return true;
        }
    }
}
