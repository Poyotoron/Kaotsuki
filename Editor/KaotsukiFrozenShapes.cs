using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Poyo.Kaotsuki.Editor
{
    /// <summary>AAO Avatar Optimizer の Freeze BlendShapes で固定されるブレンドシェイプ。</summary>
    internal static class KaotsukiFrozenShapes
    {
        // NOTE: AAO Avatar Optimizer は任意の連携先で、参照を持たない。コンポーネントの型が internal で直接使えないため、
        //       型名で探し、シリアライズされた固定対象の一覧を AAO と同じ順（元の一覧 → Prefab の各段 → シーン上の変更）で組み立てる。
        //       入っていなければ何も見つからないだけで、形式が変わって読めないときも固定なしとして扱う。
        private const string FreezeTypeName = "Anatawa12.AvatarOptimizer.FreezeBlendShape";

        internal static HashSet<string> Resolve(SkinnedMeshRenderer renderer)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (renderer == null)
            {
                return result;
            }

            foreach (var component in renderer.GetComponents<Component>())
            {
                if (component == null || component.GetType().FullName != FreezeTypeName)
                {
                    continue;
                }

                using (var serialized = new SerializedObject(component))
                {
                    var set = serialized.FindProperty("shapeKeysSet");
                    if (set == null)
                    {
                        continue;
                    }

                    AddAll(result, set.FindPropertyRelative("mainSet"));
                    var layers = set.FindPropertyRelative("prefabLayers");
                    if (layers != null && layers.isArray)
                    {
                        for (var i = 0; i < layers.arraySize; i++)
                        {
                            ApplyLayer(result, layers.GetArrayElementAtIndex(i));
                        }
                    }

                    var usingOnScene = set.FindPropertyRelative("usingOnSceneLayer");
                    if (usingOnScene != null && usingOnScene.boolValue)
                    {
                        ApplyLayer(result, set.FindPropertyRelative("onSceneLayer"));
                    }
                }
            }

            return result;
        }

        private static void AddAll(HashSet<string> result, SerializedProperty array)
        {
            if (array == null || !array.isArray)
            {
                return;
            }

            for (var i = 0; i < array.arraySize; i++)
            {
                var value = array.GetArrayElementAtIndex(i).stringValue;
                if (!string.IsNullOrEmpty(value))
                {
                    result.Add(value);
                }
            }
        }

        private static void RemoveAll(HashSet<string> result, SerializedProperty array)
        {
            if (array == null || !array.isArray)
            {
                return;
            }

            for (var i = 0; i < array.arraySize; i++)
            {
                var value = array.GetArrayElementAtIndex(i).stringValue;
                if (!string.IsNullOrEmpty(value))
                {
                    result.Remove(value);
                }
            }
        }

        private static void ApplyLayer(HashSet<string> result, SerializedProperty layer)
        {
            if (layer == null)
            {
                return;
            }

            RemoveAll(result, layer.FindPropertyRelative("removes"));
            AddAll(result, layer.FindPropertyRelative("additions"));
        }
    }
}
