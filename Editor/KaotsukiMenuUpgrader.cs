using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Poyo.Kaotsuki.Editor
{
    internal static class KaotsukiMenuUpgrader
    {
        internal const string OnObjectName = "ON";
        internal const string LipSyncObjectName = "口パク";
        private const string UndoName = "Kaotsuki メニュー";

        internal static void BuildSubMenu(ModularAvatarMenuItem item, string undoName)
        {
            if (item.Control == null)
            {
                item.Control = new VRCExpressionsMenu.Control();
            }

            if (string.IsNullOrEmpty(item.Control.name))
            {
                item.Control.name = "Kaotsuki";
            }

            item.Control.type = VRCExpressionsMenu.Control.ControlType.SubMenu;
            item.Control.parameter = new VRCExpressionsMenu.Control.Parameter { name = string.Empty };
            item.MenuSource = SubmenuSource.Children;
            AddToggle(item.transform, OnObjectName, KaotsukiInfo.ParamEnabled, undoName);
            AddToggle(item.transform, LipSyncObjectName, KaotsukiInfo.ParamLipSync, undoName);
        }

        private static void AddToggle(Transform parent, string name, string parameter, string undoName)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            var item = child.AddComponent<ModularAvatarMenuItem>();
            item.Control = new VRCExpressionsMenu.Control
            {
                name = name,
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = parameter },
                value = 1f,
            };
            item.isSynced = true;
            item.isSaved = false;
            item.isDefault = false;
            Undo.RegisterCreatedObjectUndo(child, undoName);
        }

        internal static bool NeedsUpgrade(KaotsukiReceiver receiver)
        {
            var item = receiver == null ? null : receiver.GetComponent<ModularAvatarMenuItem>();
            return item != null && item.Control != null &&
                   item.Control.type == VRCExpressionsMenu.Control.ControlType.Toggle &&
                   item.Control.parameter != null && item.Control.parameter.name == KaotsukiInfo.ParamEnabled;
        }

        internal static void Upgrade(KaotsukiReceiver receiver)
        {
            if (!NeedsUpgrade(receiver))
            {
                return;
            }

            var item = receiver.GetComponent<ModularAvatarMenuItem>();
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);
            Undo.RecordObject(item, UndoName);
            BuildSubMenu(item, UndoName);
            PrefabUtility.RecordPrefabInstancePropertyModifications(item);
            EditorUtility.SetDirty(item);
            Undo.CollapseUndoOperations(group);
        }
    }
}
