using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace STAssignedStorageOnly
{
    [BepInPlugin("gabe.supermarkettogether.assignedstorageonly", "Assigned Storage Only", "0.2.0")]
    public sealed class AssignedStorageOnly : BaseUnityPlugin
    {
        private static FieldInfo storageObjectField;
        private static FieldInfo productInfoField;
        private static FieldInfo boxesObjectField, shelvesObjectField, boxProductField;
        private static Type containerType, boxType;
        private static MethodInfo rowCapacityMethod, priorityIndexMethod;
        private static ConfigEntry<bool> prioritizeLowStock;
        private static AssignedStorageOnly instance;
        private Harmony harmony;

        private void Awake()
        {
            instance = this;
            prioritizeLowStock = Config.Bind("StorageWorkers", "PrioritizeLowShelfStock", true,
                "Choose ground boxes for products with the lowest overall shelf fill percentage first.");
            Type managerType = AccessTools.TypeByName("NPC_Manager");
            containerType = AccessTools.TypeByName("Data_Container");
            boxType = AccessTools.TypeByName("BoxData");
            if (managerType == null || containerType == null)
            {
                Logger.LogError("NPC_Manager or Data_Container was not found; no storage behavior was changed.");
                return;
            }

            storageObjectField = AccessTools.Field(managerType, "storageOBJ");
            productInfoField = AccessTools.Field(containerType, "productInfoArray");
            MethodInfo findShelf = AccessTools.Method(managerType, "GetFreeStorageContainer", new[] { typeof(int) });
            MethodInfo findRow = AccessTools.Method(managerType, "GetFreeStorageRow", new[] { typeof(int), typeof(int) });
            boxesObjectField = AccessTools.Field(managerType, "boxesOBJ");
            shelvesObjectField = AccessTools.Field(managerType, "shelvesOBJ");
            boxProductField = boxType == null ? null : AccessTools.Field(boxType, "productID");
            rowCapacityMethod = AccessTools.Method(managerType, "GetMaxProductsPerRow", new[] { typeof(int), typeof(int) });
            priorityIndexMethod = AccessTools.Method(managerType, "GetPriorityIndex", new[] { typeof(GameObject), typeof(int) });
            MethodInfo chooseBox = AccessTools.Method(managerType, "GetRandomGroundBoxAllowedInStorage", new[] { typeof(GameObject) });
            if (storageObjectField == null || productInfoField == null || findShelf == null || findRow == null)
            {
                Logger.LogError("Storage selection fields or methods were not found; no storage behavior was changed.");
                return;
            }

            harmony = new Harmony("gabe.supermarkettogether.assignedstorageonly");
            harmony.Patch(findShelf, postfix: new HarmonyMethod(typeof(AssignedStorageOnly), nameof(AfterFindShelf)));
            harmony.Patch(findRow, postfix: new HarmonyMethod(typeof(AssignedStorageOnly), nameof(AfterFindRow)));
            if (boxesObjectField != null && shelvesObjectField != null && boxProductField != null &&
                rowCapacityMethod != null && priorityIndexMethod != null && chooseBox != null)
                harmony.Patch(chooseBox, postfix: new HarmonyMethod(typeof(AssignedStorageOnly), nameof(AfterChooseBox)));
            else
                Logger.LogWarning("Box-priority game members unavailable; labeled-slot rule remains active.");
            Logger.LogInfo("Storage workers use labeled slots and prioritize products low on shelves.");
        }

        private static void AfterChooseBox(object __instance, GameObject __0, ref GameObject __result)
        {
            if (!prioritizeLowStock.Value) return;
            try
            {
                Transform ground = Root(boxesObjectField.GetValue(__instance));
                Transform storage = StorageRoot(__instance);
                Transform shelves = Root(shelvesObjectField.GetValue(__instance));
                if (ground == null || storage == null || shelves == null) return;

                int workerIndex = 0;
                // Preserve the game's distinct worker choices by using its own priority index.
                workerIndex = (int)priorityIndexMethod.Invoke(__instance, new object[] { __0, 3 });

                var fill = new Dictionary<int, Stock>();
                for (int shelf = 0; shelf < shelves.childCount; shelf++)
                {
                    int[] data = ProductInfo(shelves.GetChild(shelf));
                    if (data == null) continue;
                    for (int row = 0; row + 1 < data.Length; row += 2)
                    {
                        int id = data[row];
                        if (id < 0) continue;
                        int capacity = (int)rowCapacityMethod.Invoke(__instance, new object[] { shelf, id });
                        if (capacity <= 0) continue;
                        Stock stock;
                        if (!fill.TryGetValue(id, out stock)) fill[id] = stock = new Stock();
                        stock.Capacity += capacity;
                        stock.Units += Math.Max(0, Math.Min(capacity, data[row + 1]));
                    }
                }

                var freeLabels = FreeLabels(storage);
                var candidates = new List<Candidate>();
                for (int i = 0; i < ground.childCount; i++)
                {
                    GameObject box = ground.GetChild(i).gameObject;
                    Component boxData = box.GetComponent(boxType);
                    if (boxData == null) continue;
                    int id = (int)boxProductField.GetValue(boxData);
                    if (!freeLabels.Contains(id)) continue;
                    Stock stock;
                    double ratio = fill.TryGetValue(id, out stock) && stock.Capacity > 0
                        ? (double)stock.Units / stock.Capacity : 1.0;
                    candidates.Add(new Candidate { Box = box, Ratio = ratio, Order = i });
                }
                if (candidates.Count == 0) return;
                candidates.Sort((a, b) => {
                    int result = a.Ratio.CompareTo(b.Ratio);
                    return result == 0 ? a.Order.CompareTo(b.Order) : result;
                });
                __result = workerIndex >= 0 && workerIndex < candidates.Count
                    ? candidates[workerIndex].Box : candidates[UnityEngine.Random.Range(0, candidates.Count)].Box;
            }
            catch (Exception ex)
            {
                instance.Logger.LogWarning("Ground-box priority unavailable; vanilla box choice kept: " + ex);
            }
        }

        private sealed class Stock { internal long Units, Capacity; }
        private sealed class Candidate { internal GameObject Box; internal double Ratio; internal int Order; }

        private static HashSet<int> FreeLabels(Transform storage)
        {
            var products = new HashSet<int>();
            for (int shelf = 0; shelf < storage.childCount; shelf++)
            {
                Transform container = storage.GetChild(shelf);
                int[] data = ProductInfo(container);
                Transform boxes = container.Find("BoxContainer");
                if (data == null || boxes == null) continue;
                int count = Math.Min(data.Length / 2, boxes.childCount);
                for (int row = 0; row < count; row++)
                    if (data[row * 2] >= 0 && data[row * 2 + 1] <= 0 &&
                        boxes.GetChild(row).childCount == 0) products.Add(data[row * 2]);
            }
            return products;
        }

        // These two game methods are called by NPC_Manager.EmployeeNPCControl.
        // Keep the game's preferred shelf and row whenever they are assigned and free.
        // Its existing -1 result handles the case where no suitable slot exists.
        private static void AfterFindShelf(object __instance, int __0, ref int __result)
        {
            if (__result < 0)
                return;

            Transform root = StorageRoot(__instance);
            if (root == null || __result >= root.childCount)
                return;

            Transform shelf = root.GetChild(__result);
            int[] info = ProductInfo(shelf);
            Transform boxes = shelf.Find("BoxContainer");
            if (info == null || boxes == null)
                return; // Unknown game layout: preserve the original behavior.

            int rows = Math.Min(info.Length / 2, boxes.childCount);
            for (int row = 0; row < rows; row++)
            {
                if (info[row * 2] == __0 && info[row * 2 + 1] <= 0 &&
                    boxes.GetChild(row).childCount == 0)
                    return;
            }

            __result = -1;
        }

        private static void AfterFindRow(object __instance, int __0, int __1, ref int __result)
        {
            if (__result < 0)
                return;

            Transform root = StorageRoot(__instance);
            if (root == null || __0 < 0 || __0 >= root.childCount)
                return;

            Transform shelf = root.GetChild(__0);
            int[] info = ProductInfo(shelf);
            Transform boxes = shelf.Find("BoxContainer");
            if (info == null || boxes == null)
                return;

            int offset = __result * 2;
            if (offset < 0 || offset + 1 >= info.Length || __result >= boxes.childCount)
                return;

            if (info[offset] != __1 || info[offset + 1] > 0 ||
                boxes.GetChild(__result).childCount != 0)
                __result = -1;
        }

        private static Transform StorageRoot(object manager)
        {
            return Root(storageObjectField.GetValue(manager));
        }

        private static Transform Root(object value)
        {
            GameObject gameObject = value as GameObject;
            if (gameObject != null)
                return gameObject.transform;
            Component component = value as Component;
            return component != null ? component.transform : null;
        }

        private static int[] ProductInfo(Transform shelf)
        {
            Component data = shelf.GetComponent(containerType);
            return data == null ? null : productInfoField.GetValue(data) as int[];
        }

        private void OnDestroy()
        {
            if (harmony != null)
                harmony.UnpatchSelf();
        }
    }
}
