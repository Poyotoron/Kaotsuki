using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Poyo.Kaotsuki.Editor
{
    [CustomEditor(typeof(KaotsukiReceiver))]
    internal sealed class KaotsukiReceiverEditor : UnityEditor.Editor
    {
        private enum ChangeKind
        {
            None,
            OverrideEyes,
            OverrideMouth,
            Renderer,
            RemoveMesh,
            SetBlendShape,
            SetAll,
            AddMesh,
        }

        private enum ActionKind
        {
            None,
            Export,
            Launch,
            OpenFolder,
        }

        // NOTE: GUIContent.none はエディタ全体で共有されるため、フィールドに保持して書き換えると他の画面の描画にまで文字が出る。
        private static readonly GUIContent Empty = new GUIContent(string.Empty);

        private sealed class MeshView
        {
            internal Mesh Mesh;
            internal GUIContent[] Labels = Array.Empty<GUIContent>();
            internal bool[] Enabled = Array.Empty<bool>();
            internal GUIContent Header = Empty;
            internal bool Expanded;
            internal string Search = string.Empty;
            internal bool[] Visible = Array.Empty<bool>();
            internal Vector2 Scroll;
        }

        private static readonly GUIContent OptionsHeading = new GUIContent("オプション");
        private static readonly GUIContent MeshesHeading = new GUIContent("メッシュ");
        private static readonly GUIContent OverrideEyesLabel = new GUIContent("ON の間まばたき・視線を止める");
        private static readonly GUIContent OverrideMouthLabel = new GUIContent("ON の間リップシンクを止める");
        private static readonly GUIContent AllOnLabel = new GUIContent("すべて ON");
        private static readonly GUIContent AllOffLabel = new GUIContent("すべて OFF");
        private static readonly GUIContent SearchLabel = new GUIContent("検索");
        private static readonly GUIContent RemoveLabel = new GUIContent("×");
        private static readonly GUIContent AddMeshLabel = new GUIContent("メッシュを追加");
        private static readonly GUIContent ExportLabel = new GUIContent("マップを書き出す");
        private static readonly GUIContent LaunchLabel = new GUIContent("送り手を起動");
        private static readonly GUIContent OpenFolderLabel = new GUIContent("マップのフォルダを開く");
        private static readonly GUILayoutOption[] RemoveOptions = { GUILayout.Width(22f) };
        private static readonly GUILayoutOption[] CountLabelOptions = { GUILayout.ExpandWidth(false) };
        private static readonly GUILayoutOption[] ScrollOptions = { GUILayout.MaxHeight(360f) };

        private readonly List<MeshView> _views = new List<MeshView>();
        private KaotsukiSlotTable _table;
        private GUIContent _countLabel = Empty;
        private string _channelMessage;
        private string _truncatedMessage;
        private string _skippedMessage;
        private Transform _avatarRoot;

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
            RebuildCache();
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        private void OnUndoRedo()
        {
            RebuildCache();
            Repaint();
        }

        public override void OnInspectorGUI()
        {
            if (serializedObject.UpdateIfRequiredOrScript())
            {
                RebuildCache();
            }

            var receiver = (KaotsukiReceiver)target;
            if (CacheNeedsRebuild(receiver))
            {
                RebuildCache();
            }

            if (_avatarRoot == null)
            {
                EditorGUILayout.HelpBox(
                    "アバター（VRC Avatar Descriptor）の中に置いてください。",
                    MessageType.Error);
            }

            EditorGUILayout.LabelField(_countLabel);
            if (_channelMessage != null)
            {
                EditorGUILayout.HelpBox(_channelMessage, MessageType.Info);
            }

            if (_truncatedMessage != null)
            {
                EditorGUILayout.HelpBox(_truncatedMessage, MessageType.Warning);
            }

            if (_skippedMessage != null)
            {
                EditorGUILayout.HelpBox(_skippedMessage, MessageType.Warning);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(OptionsHeading, EditorStyles.boldLabel);

            var change = ChangeKind.None;
            var changeMeshIndex = -1;
            var changeBlendShape = -1;
            var changeBool = false;
            SkinnedMeshRenderer changeRenderer = null;
            var action = ActionKind.None;

            var eyes = EditorGUILayout.Toggle(OverrideEyesLabel, receiver.overrideEyes);
            if (eyes != receiver.overrideEyes)
            {
                change = ChangeKind.OverrideEyes;
                changeBool = eyes;
            }

            var mouth = EditorGUILayout.Toggle(OverrideMouthLabel, receiver.overrideMouth);
            if (change == ChangeKind.None && mouth != receiver.overrideMouth)
            {
                change = ChangeKind.OverrideMouth;
                changeBool = mouth;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(MeshesHeading, EditorStyles.boldLabel);
            if (receiver.meshes != null)
            {
                for (var i = 0; i < receiver.meshes.Count; i++)
                {
                    var entry = receiver.meshes[i];
                    var view = _views[i];
                    var renderer = entry == null ? null : entry.renderer;

                    EditorGUILayout.BeginHorizontal();
                    view.Expanded = EditorGUILayout.Foldout(view.Expanded, GUIContent.none, true);
                    var replacement = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(
                        GUIContent.none,
                        renderer,
                        typeof(SkinnedMeshRenderer),
                        true);
                    EditorGUILayout.LabelField(view.Header, CountLabelOptions);
                    var remove = GUILayout.Button(RemoveLabel, RemoveOptions);
                    EditorGUILayout.EndHorizontal();

                    if (change == ChangeKind.None && replacement != renderer)
                    {
                        change = ChangeKind.Renderer;
                        changeMeshIndex = i;
                        changeRenderer = replacement;
                    }
                    else if (change == ChangeKind.None && remove)
                    {
                        change = ChangeKind.RemoveMesh;
                        changeMeshIndex = i;
                    }

                    if (!view.Expanded)
                    {
                        continue;
                    }

                    if (renderer == null || renderer.sharedMesh == null)
                    {
                        EditorGUILayout.HelpBox("メッシュを設定してください。", MessageType.Info);
                        continue;
                    }

                    EditorGUILayout.BeginHorizontal();
                    var allOn = GUILayout.Button(AllOnLabel);
                    var allOff = GUILayout.Button(AllOffLabel);
                    EditorGUILayout.EndHorizontal();

                    var search = EditorGUILayout.TextField(SearchLabel, view.Search);
                    if (!string.Equals(search, view.Search, StringComparison.Ordinal))
                    {
                        view.Search = search;
                        UpdateVisibility(view);
                    }

                    view.Scroll = EditorGUILayout.BeginScrollView(view.Scroll, ScrollOptions);
                    for (var blendShapeIndex = 0; blendShapeIndex < view.Labels.Length; blendShapeIndex++)
                    {
                        if (!view.Visible[blendShapeIndex])
                        {
                            continue;
                        }

                        var enabled = EditorGUILayout.ToggleLeft(
                            view.Labels[blendShapeIndex],
                            view.Enabled[blendShapeIndex]);
                        if (change == ChangeKind.None && enabled != view.Enabled[blendShapeIndex])
                        {
                            change = ChangeKind.SetBlendShape;
                            changeMeshIndex = i;
                            changeBlendShape = blendShapeIndex;
                            changeBool = enabled;
                        }
                    }

                    EditorGUILayout.EndScrollView();

                    if (change == ChangeKind.None && (allOn || allOff))
                    {
                        change = ChangeKind.SetAll;
                        changeMeshIndex = i;
                        changeBool = allOn;
                    }
                }
            }

            var added = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(
                AddMeshLabel,
                null,
                typeof(SkinnedMeshRenderer),
                true);
            if (change == ChangeKind.None && added != null)
            {
                change = ChangeKind.AddMesh;
                changeRenderer = added;
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_avatarRoot == null))
            {
                if (GUILayout.Button(ExportLabel))
                {
                    action = ActionKind.Export;
                }

                if (GUILayout.Button(LaunchLabel))
                {
                    action = ActionKind.Launch;
                }

                if (GUILayout.Button(OpenFolderLabel))
                {
                    action = ActionKind.OpenFolder;
                }
            }

            ApplyChange(
                receiver,
                change,
                changeMeshIndex,
                changeBlendShape,
                changeBool,
                changeRenderer);
            ApplyAction(action);
        }

        private bool CacheNeedsRebuild(KaotsukiReceiver receiver)
        {
            var count = receiver.meshes == null ? 0 : receiver.meshes.Count;
            if (_views.Count != count)
            {
                return true;
            }

            for (var i = 0; i < count; i++)
            {
                var entry = receiver.meshes[i];
                var renderer = entry == null ? null : entry.renderer;
                var mesh = renderer == null ? null : renderer.sharedMesh;
                var view = _views[i];
                if (view.Mesh != mesh || view.Labels.Length != (mesh == null ? 0 : mesh.blendShapeCount))
                {
                    return true;
                }
            }

            return false;
        }

        private void ApplyChange(
            KaotsukiReceiver receiver,
            ChangeKind kind,
            int meshIndex,
            int blendShapeIndex,
            bool boolValue,
            SkinnedMeshRenderer renderer)
        {
            if (kind == ChangeKind.None)
            {
                return;
            }

            if (kind == ChangeKind.AddMesh)
            {
                if (_avatarRoot == null || !renderer.transform.IsChildOf(_avatarRoot))
                {
                    Debug.LogWarning("[Kaotsuki] アバターの外にあるメッシュは追加できません。");
                    return;
                }

                if (receiver.meshes != null && receiver.meshes.Exists(entry => entry != null && entry.renderer == renderer))
                {
                    Debug.LogWarning("[Kaotsuki] このメッシュは既に登録されています。");
                    return;
                }
            }

            Undo.RecordObject(receiver, "Kaotsuki");
            switch (kind)
            {
                case ChangeKind.OverrideEyes:
                    receiver.overrideEyes = boolValue;
                    break;
                case ChangeKind.OverrideMouth:
                    receiver.overrideMouth = boolValue;
                    break;
                case ChangeKind.Renderer:
                    EnsureEntry(receiver, meshIndex).renderer = renderer;
                    break;
                case ChangeKind.RemoveMesh:
                    receiver.meshes.RemoveAt(meshIndex);
                    break;
                case ChangeKind.SetBlendShape:
                    SetBlendShape(
                        receiver.meshes[meshIndex],
                        _views[meshIndex].Labels[blendShapeIndex].text,
                        boolValue);
                    break;
                case ChangeKind.SetAll:
                    SetAll(receiver.meshes[meshIndex], boolValue);
                    break;
                case ChangeKind.AddMesh:
                    if (receiver.meshes == null)
                    {
                        receiver.meshes = new List<KaotsukiMeshEntry>();
                    }

                    receiver.meshes.Add(new KaotsukiMeshEntry { renderer = renderer });
                    break;
            }

            PrefabUtility.RecordPrefabInstancePropertyModifications(receiver);
            EditorUtility.SetDirty(receiver);
            RebuildCache(kind == ChangeKind.AddMesh ? receiver.meshes.Count - 1 : -1);
        }

        private static KaotsukiMeshEntry EnsureEntry(KaotsukiReceiver receiver, int index)
        {
            if (receiver.meshes[index] == null)
            {
                receiver.meshes[index] = new KaotsukiMeshEntry();
            }

            return receiver.meshes[index];
        }

        private static void SetBlendShape(KaotsukiMeshEntry entry, string name, bool enabled)
        {
            if (entry.excludedBlendShapes == null)
            {
                entry.excludedBlendShapes = new List<string>();
            }

            if (enabled)
            {
                entry.excludedBlendShapes.Remove(name);
            }
            else if (!entry.excludedBlendShapes.Contains(name))
            {
                entry.excludedBlendShapes.Add(name);
            }
        }

        private static void SetAll(KaotsukiMeshEntry entry, bool enabled)
        {
            if (entry.excludedBlendShapes == null)
            {
                entry.excludedBlendShapes = new List<string>();
            }

            var mesh = entry.renderer.sharedMesh;
            for (var i = 0; i < mesh.blendShapeCount; i++)
            {
                var name = mesh.GetBlendShapeName(i);
                if (enabled)
                {
                    entry.excludedBlendShapes.Remove(name);
                }
                else if (!entry.excludedBlendShapes.Contains(name))
                {
                    entry.excludedBlendShapes.Add(name);
                }
            }
        }

        private void ApplyAction(ActionKind action)
        {
            switch (action)
            {
                case ActionKind.Export:
                {
                    var path = ExportMap(out var dialogShown);
                    if (path != null)
                    {
                        Debug.Log("[Kaotsuki] マップを書き出しました: " + path);
                    }

                    if (dialogShown)
                    {
                        GUIUtility.ExitGUI();
                    }

                    break;
                }
                case ActionKind.Launch:
                {
                    var path = ExportMap(out var dialogShown);
                    if (path != null)
                    {
                        KaotsukiSenderLauncher.Launch(path);
                        GUIUtility.ExitGUI();
                    }
                    else if (dialogShown)
                    {
                        GUIUtility.ExitGUI();
                    }

                    break;
                }
                case ActionKind.OpenFolder:
                    KaotsukiSenderLauncher.OpenMapFolder();
                    break;
            }
        }

        private string ExportMap(out bool dialogShown)
        {
            dialogShown = false;
            var receiver = (KaotsukiReceiver)target;
            var table = KaotsukiSlotTable.Build(receiver, _avatarRoot);
            if (table.Slots.Count == 0)
            {
                EditorUtility.DisplayDialog("Kaotsuki", "操作できるブレンドシェイプがありません。", "OK");
                dialogShown = true;
                return null;
            }

            try
            {
                return KaotsukiMapWriter.Write(table, _avatarRoot.gameObject);
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Kaotsuki", "マップを書き出せませんでした。\n" + e.Message, "OK");
                dialogShown = true;
                return null;
            }
        }

        private void RebuildCache(int expandIndex = -1)
        {
            var receiver = (KaotsukiReceiver)target;
            var oldViews = new List<MeshView>(_views);
            _views.Clear();
            _avatarRoot = KaotsukiSlotTable.FindAvatarRoot(receiver);
            _table = KaotsukiSlotTable.Build(receiver, _avatarRoot);
            var channelCount = _table.ChannelCount;
            var syncBits = 1 + 16 * channelCount;
            _countLabel = new GUIContent(channelCount == 0
                ? "登録数: 0"
                : "登録数: " + _table.RegisteredCount + "（枠 " + channelCount + "・同期 " + syncBits + " bit）");
            _channelMessage = channelCount >= 2
                ? "登録数が 255 を超えたため、枠を " + channelCount + " 個使います（同期パラメータ " + syncBits +
                  " bit）。使わないブレンドシェイプを OFF にすると減らせます。"
                : null;
            _truncatedMessage = _table.TruncatedCount > 0
                ? "登録数が 3825 を超えています。超えた分（" + _table.TruncatedCount + " 件）は操作できません。"
                : null;
            _skippedMessage = _table.SkippedReasons.Count > 0
                ? string.Join("\n", _table.SkippedReasons)
                : null;

            if (receiver.meshes == null)
            {
                return;
            }

            for (var i = 0; i < receiver.meshes.Count; i++)
            {
                var old = i < oldViews.Count ? oldViews[i] : null;
                var entry = receiver.meshes[i];
                var renderer = entry == null ? null : entry.renderer;
                var mesh = renderer == null ? null : renderer.sharedMesh;
                var view = new MeshView
                {
                    Mesh = mesh,
                    Expanded = i == expandIndex || old != null && old.Expanded,
                    Search = old == null ? string.Empty : old.Search,
                    Scroll = old == null ? Vector2.zero : old.Scroll,
                };

                var total = mesh == null ? 0 : mesh.blendShapeCount;
                var registered = 0;
                view.Labels = new GUIContent[total];
                view.Enabled = new bool[total];
                view.Visible = new bool[total];
                var excluded = entry == null || entry.excludedBlendShapes == null
                    ? null
                    : new HashSet<string>(entry.excludedBlendShapes);
                for (var blendShapeIndex = 0; blendShapeIndex < total; blendShapeIndex++)
                {
                    var name = mesh.GetBlendShapeName(blendShapeIndex);
                    var enabled = excluded == null || !excluded.Contains(name);
                    view.Labels[blendShapeIndex] = new GUIContent(name);
                    view.Enabled[blendShapeIndex] = enabled;
                    if (enabled)
                    {
                        registered++;
                    }
                }

                view.Header = new GUIContent("登録数 " + registered + " / " + total);
                UpdateVisibility(view);
                _views.Add(view);
            }
        }

        private static void UpdateVisibility(MeshView view)
        {
            for (var i = 0; i < view.Visible.Length; i++)
            {
                view.Visible[i] = view.Search.Length == 0 ||
                                  view.Labels[i].text.IndexOf(view.Search, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }
    }
}
