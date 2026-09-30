using System;
using System.Collections.Generic;
using nadena.dev.modular_avatar.core;
using nadena.dev.modular_avatar.core.menu;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using Object = UnityEngine.Object;

namespace Poyo.Kaotsuki.Editor
{
    internal static class KaotsukiSetupMenu
    {
        [MenuItem(KaotsukiInfo.SetupMenuPath, false, KaotsukiInfo.SetupMenuPriority)]
        private static void Setup(MenuCommand command)
        {
            if (IsDuplicateInvocation(command))
            {
                return;
            }

            var roots = new List<Transform>();
            var seen = new HashSet<Transform>();
            foreach (var selected in Selection.gameObjects)
            {
                var descriptor = selected.GetComponentInParent<VRCAvatarDescriptor>(true);
                if (descriptor != null && seen.Add(descriptor.transform))
                {
                    roots.Add(descriptor.transform);
                }
            }

            GameObject lastCreated = null;
            foreach (var root in roots)
            {
                var existing = root.GetComponentsInChildren<KaotsukiReceiver>(true);
                if (existing.Length > 0)
                {
                    Selection.activeGameObject = existing[0].gameObject;
                    EditorUtility.DisplayDialog("Kaotsuki", "このアバターには既に Kaotsuki があります。", "OK");
                    continue;
                }

                var go = new GameObject("Kaotsuki");
                go.transform.SetParent(root, false);
                var receiver = go.AddComponent<KaotsukiReceiver>();
                // NOTE: 素体の Prefab で Setup すると、その Variant（別衣装）にも同じマップ名が継承され、1 つのマップを共有できる。
                receiver.mapName = KaotsukiMapWriter.SanitizeName(root.name);
                // NOTE: 通常の Modular Avatar の手順で、利用者がメニューの置き場所を変えられるようにする。
                go.AddComponent<ModularAvatarMenuInstaller>();
                var item = go.AddComponent<ModularAvatarMenuItem>();
                item.Control = new VRCExpressionsMenu.Control
                {
                    name = "Kaotsuki",
                    type = VRCExpressionsMenu.Control.ControlType.Toggle,
                    parameter = new VRCExpressionsMenu.Control.Parameter { name = KaotsukiInfo.ParamEnabled },
                    value = 1f,
                };
                item.MenuSource = SubmenuSource.Children;
                item.isSynced = true;
                item.isSaved = false;
                item.isDefault = false;

                var body = FindBody(root);
                if (body != null)
                {
                    receiver.meshes.Add(new KaotsukiMeshEntry { renderer = body });
                }
                else
                {
                    Debug.LogWarning(
                        "[Kaotsuki] Body メッシュが見つかりませんでした。Inspector でメッシュを追加してください。",
                        go);
                }

                Undo.RegisterCreatedObjectUndo(go, "Kaotsuki Setup");
                lastCreated = go;
            }

            if (lastCreated != null)
            {
                Selection.activeGameObject = lastCreated;
            }
        }

        [MenuItem(KaotsukiInfo.SetupMenuPath, true)]
        private static bool ValidateSetup()
        {
            return Selection.activeGameObject != null &&
                   Selection.activeGameObject.GetComponentInParent<VRCAvatarDescriptor>(true) != null;
        }

        private static bool IsDuplicateInvocation(MenuCommand command)
        {
            Object[] objects = Selection.objects;
            return objects.Length > 1 && command.context != null && command.context != objects[0];
        }

        private static SkinnedMeshRenderer FindBody(Transform root)
        {
            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (string.Equals(child.name, "Body", StringComparison.Ordinal))
                {
                    var renderer = child.GetComponent<SkinnedMeshRenderer>();
                    if (renderer != null)
                    {
                        return renderer;
                    }
                }
            }

            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (string.Equals(renderer.name, "Body", StringComparison.Ordinal))
                {
                    return renderer;
                }
            }

            return null;
        }
    }
}
