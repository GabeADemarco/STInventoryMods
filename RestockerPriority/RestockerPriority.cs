using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace STRestockerPriority
{
    [BepInPlugin("gabe.supermarkettogether.restockerpriority", "Restocker Priority", "0.1.0")]
    public sealed class RestockerPriority : BaseUnityPlugin
    {
        private static readonly string[] TaskLists = {
            "lowProductCountList", "mediumProductCountList", "highProductCountList", "veryHighProductCountList"
        };

        private static FieldInfo shelvesField, storageField, employeesField, infoField, taskPriorityField;
        private static FieldInfo listingInstanceField, productsField, maxBoxField;
        private static Type containerType, npcInfoType;
        private static MethodInfo rowCapacityMethod;
        private static FieldInfo[] taskListFields;
        private static ConfigEntry<bool> productWide, preferFull, diagnostic;
        private static RestockerPriority instance;
        private Harmony harmony;

        private void Awake()
        {
            instance = this;
            productWide = Config.Bind("Restocking", "ProductWidePriority", true,
                "Prioritize products by total shelf fill percentage rather than individual row thresholds.");
            preferFull = Config.Bind("Restocking", "PreferFullBoxWhenNeeded", true,
                "If the selected partial box cannot fill the chosen row, prefer a full box that can.");
            diagnostic = Config.Bind("Diagnostics", "LogTaskChanges", false,
                "Log changed tasks and box selections. Disable for normal play.");

            Type manager = AccessTools.TypeByName("NPC_Manager");
            containerType = AccessTools.TypeByName("Data_Container");
            npcInfoType = AccessTools.TypeByName("NPC_Info");
            Type listing = AccessTools.TypeByName("ProductListing");
            if (manager == null || containerType == null || npcInfoType == null || listing == null)
            {
                Logger.LogError("Required game classes unavailable; Restocker Priority disabled.");
                return;
            }

            shelvesField = AccessTools.Field(manager, "shelvesOBJ");
            storageField = AccessTools.Field(manager, "storageOBJ");
            employeesField = AccessTools.Field(manager, "employeeParentOBJ");
            infoField = AccessTools.Field(containerType, "productInfoArray");
            taskPriorityField = AccessTools.Field(npcInfoType, "taskPriority");
            listingInstanceField = AccessTools.Field(listing, "Instance");
            productsField = AccessTools.Field(listing, "productsData");
            rowCapacityMethod = AccessTools.Method(manager, "GetMaxProductsPerRow", new[] { typeof(int), typeof(int) });
            MethodInfo choose = AccessTools.Method(manager, "ReturnWeightedRestockerTask", new[] { typeof(GameObject) });
            taskListFields = new FieldInfo[TaskLists.Length];
            for (int i = 0; i < taskListFields.Length; i++)
                taskListFields[i] = AccessTools.Field(manager, TaskLists[i]);

            if (shelvesField == null || storageField == null || employeesField == null ||
                infoField == null || taskPriorityField == null || listingInstanceField == null ||
                productsField == null || rowCapacityMethod == null || choose == null ||
                Array.Exists(taskListFields, field => field == null))
            {
                Logger.LogError("Required game members unavailable; Restocker Priority disabled.");
                return;
            }

            harmony = new Harmony("gabe.supermarkettogether.restockerpriority");
            harmony.Patch(choose, postfix: new HarmonyMethod(typeof(RestockerPriority), nameof(ChooseTask)));
            Logger.LogInfo("Restocker Priority ready.");
        }

        private static void ChooseTask(object __instance, GameObject __0, ref int[] __result)
        {
            try
            {
                if (__result == null || __result.Length != 6 || __result[0] < 0) return;
                int[] original = (int[])__result.Clone();
                Transform shelves = Root(shelvesField.GetValue(__instance));
                Transform storage = Root(storageField.GetValue(__instance));
                if (shelves == null || storage == null) return;

                int[] chosen = __result;
                if (productWide.Value)
                {
                    int[] ranked = RankTask(__instance, __0, shelves);
                    if (ranked != null) chosen = ranked;
                }
                if (preferFull.Value)
                    ChooseFullBox(__instance, shelves, storage, chosen);

                if (diagnostic.Value && !SameTask(original, chosen))
                    instance.Logger.LogInfo("Restocker task changed: product " + chosen[4] +
                        ", shelf " + chosen[0] + ", storage " + chosen[2] + "/" + chosen[3]);
                __result = chosen;
            }
            catch (Exception ex)
            {
                instance.Logger.LogWarning("Restocker task left unchanged: " + ex);
            }
        }

        private static bool SameTask(int[] a, int[] b)
        {
            for (int i = 0; i < 6; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private sealed class Task
        {
            internal int[] Values;
            internal int Order;
            internal double ProductFill, RowFill;
        }

        private sealed class Stock
        {
            internal long Units, Capacity;
        }

        private static int[] RankTask(object manager, GameObject worker, Transform shelves)
        {
            Transform employees = Root(employeesField.GetValue(manager));
            if (employees == null) return null;
            int workerIndex = -1, workerCount = 0;
            foreach (Transform employee in employees)
            {
                Component info = employee.GetComponent(npcInfoType);
                if (info == null || (int)taskPriorityField.GetValue(info) != 2) continue;
                if (employee.gameObject == worker) workerIndex = workerCount;
                workerCount++;
            }
            if (workerIndex < 0) return null;

            var totals = new Dictionary<int, Stock>();
            for (int shelfIndex = 0; shelfIndex < shelves.childCount; shelfIndex++)
            {
                int[] data = ShelfInfo(shelves.GetChild(shelfIndex));
                if (data == null) continue;
                for (int row = 0; row + 1 < data.Length / 2 * 2; row += 2)
                {
                    int id = data[row];
                    if (id < 0) continue;
                    int capacity = Capacity(manager, shelfIndex, id);
                    if (capacity <= 0) continue;
                    Stock stock;
                    if (!totals.TryGetValue(id, out stock)) totals[id] = stock = new Stock();
                    stock.Capacity += capacity;
                    stock.Units += Math.Max(0, Math.Min(capacity, data[row + 1]));
                }
            }

            var tasks = new List<Task>();
            var seen = new HashSet<string>();
            for (int band = 0; band < taskListFields.Length; band++)
            {
                var list = taskListFields[band].GetValue(manager) as IEnumerable;
                if (list == null) continue;
                foreach (object value in list)
                {
                    int[] parsed = ParseTask(value as string);
                    if (parsed == null) continue;
                    string key = parsed[0] + ":" + parsed[1];
                    if (!seen.Add(key) || parsed[0] < 0 || parsed[0] >= shelves.childCount) continue;
                    int[] data = ShelfInfo(shelves.GetChild(parsed[0]));
                    int row = parsed[1] / 2;
                    if (data == null || parsed[1] < 0 || parsed[1] % 2 != 0 ||
                        row >= data.Length / 2 || data[parsed[1]] != parsed[4]) continue;
                    Stock stock;
                    if (!totals.TryGetValue(parsed[4], out stock) || stock.Capacity <= 0) continue;
                    int capacity = Capacity(manager, parsed[0], parsed[4]);
                    if (capacity <= 0 || data[parsed[1] + 1] >= capacity) continue;
                    tasks.Add(new Task { Values = parsed, Order = tasks.Count,
                        ProductFill = (double)stock.Units / stock.Capacity,
                        RowFill = (double)Math.Max(0, data[parsed[1] + 1]) / capacity });
                }
            }
            if (tasks.Count <= workerIndex) return null;
            tasks.Sort((a, b) => {
                int result = a.ProductFill.CompareTo(b.ProductFill);
                if (result == 0) result = a.RowFill.CompareTo(b.RowFill);
                return result == 0 ? a.Order.CompareTo(b.Order) : result;
            });
            return tasks[workerIndex].Values;
        }

        private static int[] ParseTask(string source)
        {
            if (source == null) return null;
            string[] fields = source.Split('|');
            if (fields.Length != 6) return null;
            int[] result = new int[6];
            for (int i = 0; i < 6; i++)
                if (!int.TryParse(fields[i], out result[i])) return null;
            return result;
        }

        private static void ChooseFullBox(object manager, Transform shelves, Transform storage, int[] task)
        {
            if (task[0] < 0 || task[0] >= shelves.childCount || task[2] < 0 ||
                task[2] >= storage.childCount) return;
            int[] display = ShelfInfo(shelves.GetChild(task[0]));
            if (display == null || task[1] < 0 || task[1] + 1 >= display.Length ||
                display[task[1]] != task[4]) return;
            int missing = Capacity(manager, task[0], task[4]) - display[task[1] + 1];
            if (missing <= 0) return;
            int[] original = ShelfInfo(storage.GetChild(task[2]));
            if (original == null || task[3] < 0 || task[3] + 1 >= original.Length ||
                original[task[3]] != task[4] || original[task[3] + 1] >= missing) return;

            object listing = listingInstanceField.GetValue(null);
            if (listing == null) return;
            var products = productsField.GetValue(listing) as IList;
            if (products == null || task[4] < 0 || task[4] >= products.Count) return;
            object product = products[task[4]];
            if (product == null) return;
            if (maxBoxField == null) maxBoxField = AccessTools.Field(product.GetType(), "maxItemsPerBox");
            if (maxBoxField == null) return;
            int full = (int)maxBoxField.GetValue(product);
            if (full < missing) return; // No single box can finish this row.

            for (int shelf = 0; shelf < storage.childCount; shelf++)
            {
                int[] data = ShelfInfo(storage.GetChild(shelf));
                if (data == null) continue;
                for (int offset = 0; offset + 1 < data.Length; offset += 2)
                {
                    if (data[offset] == task[4] && data[offset + 1] == full)
                    {
                        task[2] = shelf;
                        task[3] = offset;
                        task[5] = task[4];
                        return;
                    }
                }
            }
        }

        private static int Capacity(object manager, int shelf, int id)
        {
            return (int)rowCapacityMethod.Invoke(manager, new object[] { shelf, id });
        }

        private static int[] ShelfInfo(Transform shelf)
        {
            Component component = shelf.GetComponent(containerType);
            return component == null ? null : infoField.GetValue(component) as int[];
        }

        private static Transform Root(object value)
        {
            var gameObject = value as GameObject;
            if (gameObject != null) return gameObject.transform;
            var component = value as Component;
            return component == null ? null : component.transform;
        }

        private void OnDestroy()
        {
            if (harmony != null) harmony.UnpatchSelf();
        }
    }
}
