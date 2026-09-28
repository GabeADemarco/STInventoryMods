using System;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace STAssignedStorageOnly
{
    [BepInPlugin("gabe.supermarkettogether.assignedstorageonly", "Assigned Storage Only", "0.1.0")]
    public sealed class AssignedStorageOnly : BaseUnityPlugin
    {
        private static FieldInfo storageObjectField;
        private static FieldInfo productInfoField;
        private static Type containerType;
        private Harmony harmony;

        private void Awake()
        {
            Type managerType = AccessTools.TypeByName("NPC_Manager");
            containerType = AccessTools.TypeByName("Data_Container");
            if (managerType == null || containerType == null)
            {
                Logger.LogError("NPC_Manager or Data_Container was not found; no storage behavior was changed.");
                return;
            }

            storageObjectField = AccessTools.Field(managerType, "storageOBJ");
            productInfoField = AccessTools.Field(containerType, "productInfoArray");
            MethodInfo findShelf = AccessTools.Method(managerType, "GetFreeStorageContainer", new[] { typeof(int) });
            MethodInfo findRow = AccessTools.Method(managerType, "GetFreeStorageRow", new[] { typeof(int), typeof(int) });
            if (storageObjectField == null || productInfoField == null || findShelf == null || findRow == null)
            {
                Logger.LogError("Storage selection fields or methods were not found; no storage behavior was changed.");
                return;
            }

            harmony = new Harmony("gabe.supermarkettogether.assignedstorageonly");
            harmony.Patch(findShelf, postfix: new HarmonyMethod(typeof(AssignedStorageOnly), nameof(AfterFindShelf)));
            harmony.Patch(findRow, postfix: new HarmonyMethod(typeof(AssignedStorageOnly), nameof(AfterFindRow)));
            Logger.LogInfo("Storage workers will use only empty slots labeled for the box product.");
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
            object value = storageObjectField.GetValue(manager);
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
