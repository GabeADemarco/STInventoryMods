using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace STByBoxUnits
{
    [BepInPlugin("gabe.supermarkettogether.byboxunits", "By Box (Units)", "0.4.2")]
    public sealed class ByBoxUnits : BaseUnityPlugin
    {
        private static ByBoxUnits plugin;
        private static Type blackboardType, listingType, idReferenceType;
        private static FieldInfo modeField, sortedObjectsField, listingInstanceField, productDataField, boxSizeField, idField;
        private static MethodInfo existencesMethod;
        private static bool selected;
        private static UnityEngine.Object installedControl;
        private static Transform optionsParent;
        private float nextLabelCheck;
        private ConfigEntry<int> minimumStockBoxes;
        private ConfigEntry<bool> includeUnitsInBoxes;
        private ConfigEntry<KeyboardShortcut> increaseKey, decreaseKey, toggleBoxesKey;
        private static Type gameCanvasType;
        private static FieldInfo gameCanvasInstanceField;
        private static MethodInfo notificationMethod;

        private void Awake()
        {
            plugin = this;
            minimumStockBoxes = Config.Bind("Sorting", "MinimumStockBoxes", 1,
                "Priority threshold in full boxes (1 through 5). Products at or below this amount come first.");
            includeUnitsInBoxes = Config.Bind("Sorting", "IncludeUnitsInBoxes", true,
                "Include units currently in boxes with units in storage for sorting and the priority threshold.");
            increaseKey = Config.Bind("Controls", "IncreaseMinimumStock", new KeyboardShortcut(KeyCode.F6),
                "Increase the minimum stock by one box, up to five.");
            decreaseKey = Config.Bind("Controls", "DecreaseMinimumStock", new KeyboardShortcut(KeyCode.F7),
                "Decrease the minimum stock by one box, down to one.");
            toggleBoxesKey = Config.Bind("Controls", "ToggleUnitsInBoxes", new KeyboardShortcut(KeyCode.F8),
                "Toggle whether units in boxes count toward stock.");
            minimumStockBoxes.Value = Math.Max(1, Math.Min(5, minimumStockBoxes.Value));
            gameCanvasType = AccessTools.TypeByName("GameCanvas");
            if (gameCanvasType != null)
            {
                gameCanvasInstanceField = AccessTools.Field(gameCanvasType, "Instance");
                notificationMethod = AccessTools.Method(gameCanvasType, "CreateCanvasNotification", new[] { typeof(string) });
            }
            blackboardType = AccessTools.TypeByName("ManagerBlackboard");
            listingType = AccessTools.TypeByName("ProductListing");
            idReferenceType = AccessTools.TypeByName("ProductIDReference");
            if (blackboardType == null || listingType == null || idReferenceType == null)
            {
                Logger.LogError("Game types not found; this game build is unsupported.");
                return;
            }
            modeField = AccessTools.Field(blackboardType, "currentFilterMode");
            sortedObjectsField = AccessTools.Field(blackboardType, "sortList1");
            existencesMethod = AccessTools.Method(blackboardType, "GetProductsExistences");
            listingInstanceField = AccessTools.Field(listingType, "Instance");
            productDataField = AccessTools.Field(listingType, "productsData");
            idField = AccessTools.Field(idReferenceType, "productID");
            if (modeField == null || sortedObjectsField == null || existencesMethod == null ||
                listingInstanceField == null || productDataField == null || idField == null)
            {
                Logger.LogError("Required game fields not found; this game build is unsupported.");
                return;
            }
            var harmony = new Harmony("gabe.supermarkettogether.byboxunits");
            harmony.Patch(AccessTools.Method(blackboardType, "SortModes"),
                postfix: new HarmonyMethod(typeof(ByBoxUnits), nameof(AfterSort)));
            Type localizationType = AccessTools.TypeByName("LocalizationManager");
            MethodInfo localization = localizationType == null ? null :
                AccessTools.Method(localizationType, "GetLocalizationString", new[] { typeof(string) });
            if (localization != null)
                harmony.Patch(localization, prefix: new HarmonyMethod(typeof(ByBoxUnits), nameof(RawNotificationPrefix)));
            else Logger.LogWarning("Localization hook not found; notifications may show LocError.");
            Logger.LogInfo("By Box (Units) loaded; searching for the ordering sort control.");
            StartCoroutine(FindControl());
        }

        private void Update()
        {
            if (increaseKey.Value.IsDown())
            {
                minimumStockBoxes.Value = Math.Min(5, minimumStockBoxes.Value + 1);
                Notify("Minimum stock: " + minimumStockBoxes.Value + " box" + (minimumStockBoxes.Value == 1 ? "" : "es"));
            }
            else if (decreaseKey.Value.IsDown())
            {
                minimumStockBoxes.Value = Math.Max(1, minimumStockBoxes.Value - 1);
                Notify("Minimum stock: " + minimumStockBoxes.Value + " box" + (minimumStockBoxes.Value == 1 ? "" : "es"));
            }
            else if (toggleBoxesKey.Value.IsDown())
            {
                includeUnitsInBoxes.Value = !includeUnitsInBoxes.Value;
                Notify("Include units in boxes: " + (includeUnitsInBoxes.Value ? "ON" : "OFF"));
            }
        }

        private void Notify(string message)
        {
            try
            {
                object canvas = gameCanvasInstanceField == null ? null : gameCanvasInstanceField.GetValue(null);
                if (canvas != null && notificationMethod != null)
                    notificationMethod.Invoke(canvas, new object[] { "`" + message });
                else Logger.LogInfo(message);
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

        private IEnumerator FindControl()
        {
            while (true)
            {
                if (!installedControl)
                {
                    selected = false;
                    try { InstallControl(); }
                    catch (Exception e) { Logger.LogWarning("UI inspection failed: " + e); }
                }
                yield return new WaitForSeconds(2f);
            }
        }

        private static bool IsStorageSortLabel(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            string s = value.Trim().ToLowerInvariant();
            return s == "by lowest shelf stock" || s == "by lowest storage" ||
                   s == "lowest shelf stock" || s == "lowest storage";
        }

        private void InstallControl()
        {
            foreach (Text label in Resources.FindObjectsOfTypeAll<Text>())
                if (TryCloneButton(label, label.text)) return;
            foreach (TMP_Text label in Resources.FindObjectsOfTypeAll<TMP_Text>())
                if (TryCloneButton(label, label.text)) return;
        }

        private bool TryCloneButton(Component label, string caption)
        {
            if (!label.gameObject.activeInHierarchy || !IsStorageSortLabel(caption)) return false;
            Button source = label.GetComponentInParent<Button>();
            if (source == null || source.transform.parent == null) return false;

            Button copy = Instantiate(source, source.transform.parent);
            copy.name = "ByBoxUnitsSortButton";
            copy.transform.SetSiblingIndex(source.transform.GetSiblingIndex() + 1);
            Text text = copy.GetComponentInChildren<Text>(true);
            if (text != null) text.text = "By box (units)";
            TMP_Text tmp = copy.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null) tmp.text = "By box (units)";
            copy.onClick = new Button.ButtonClickedEvent();
            copy.onClick.AddListener(() => { selected = true; SetNativeStorageMode(); });
            optionsParent = source.transform.parent;
            foreach (Button other in optionsParent.GetComponentsInChildren<Button>(true))
            {
                if (other == copy) continue;
                other.onClick.AddListener(() => selected = false);
            }
            installedControl = copy;
            Logger.LogInfo("Added By box (units) beside the storage sort button; selector caption repair enabled.");
            return true;
        }

        private void LateUpdate()
        {
            if (!selected || Time.unscaledTime < nextLabelCheck) return;
            nextLabelCheck = Time.unscaledTime + 0.2f;
            foreach (Text label in Resources.FindObjectsOfTypeAll<Text>())
                if (IsDisplayCaption(label, label.text)) label.text = "By box (units)";
            foreach (TMP_Text label in Resources.FindObjectsOfTypeAll<TMP_Text>())
                if (IsDisplayCaption(label, label.text)) label.text = "By box (units)";
        }

        private static bool IsDisplayCaption(Component label, string value)
        {
            if (!label.gameObject.activeInHierarchy || optionsParent == null ||
                label.transform.IsChildOf(optionsParent)) return false;
            string caption = (value ?? "").Trim();
            return caption.Equals("By lowest shelf stock", StringComparison.OrdinalIgnoreCase) ||
                   caption.Equals("By lowest storage", StringComparison.OrdinalIgnoreCase);
        }

        private static object FindBlackboard()
        {
            if (blackboardType == null) return null;
            foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(blackboardType))
            {
                Component component = obj as Component;
                if (component != null && component.gameObject.activeInHierarchy) return obj;
            }
            return null;
        }

        private static void SetNativeStorageMode()
        {
            object board = FindBlackboard();
            if (board != null) modeField.SetValue(board, 3); // mode 3 sorts by storage units
        }

        private static void AfterSort(object __instance)
        {
            if (!selected) return;
            try
            {
                IList rows = sortedObjectsField.GetValue(__instance) as IList;
                object listing = listingInstanceField.GetValue(null);
                Array products = productDataField.GetValue(listing) as Array;
                if (rows == null || products == null) return;
                var ordered = new List<Row>();
                for (int i = 0; i < rows.Count; i++)
                {
                    GameObject go = rows[i] as GameObject;
                    if (go == null) continue;
                    Component reference = go.GetComponent(idReferenceType);
                    if (reference == null) continue;
                    int id = (int)idField.GetValue(reference);
                    if (id < 0 || id >= products.Length) continue;
                    object data = products.GetValue(id);
                    if (data == null) continue;
                    if (boxSizeField == null) boxSizeField = AccessTools.Field(data.GetType(), "maxItemsPerBox");
                    if (boxSizeField == null) return;
                    int unitsPerBox = (int)boxSizeField.GetValue(data);
                    int[] amounts = existencesMethod.Invoke(__instance, new object[] { id }) as int[];
                    if (amounts == null || amounts.Length < 2) continue;
                    int countedUnits = amounts[1] + (plugin.includeUnitsInBoxes.Value && amounts.Length > 2 ? amounts[2] : 0);
                    ordered.Add(new Row { Object = go, Storage = countedUnits, BoxSize = unitsPerBox, Original = i });
                }
                int targetBoxes = Math.Max(1, Math.Min(5, plugin.minimumStockBoxes.Value));
                foreach (Row row in ordered.OrderBy(r => (long)r.Storage > (long)targetBoxes * r.BoxSize)
                                           .ThenBy(r => r.Storage).ThenBy(r => r.Original))
                    row.Object.transform.SetAsLastSibling();
            }
            catch (Exception e)
            {
                selected = false;
                plugin.Logger.LogError("Sort failed; custom mode disabled: " + e);
            }
        }

        private sealed class Row
        {
            public GameObject Object;
            public int Storage, BoxSize, Original;
        }
    }
}
