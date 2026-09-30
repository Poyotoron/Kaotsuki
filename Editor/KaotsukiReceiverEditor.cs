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
            MapName,
            OverrideEyes,
            OverrideMouth,
            Renderer,
            RemoveMesh,
            SetBlendShape,
            SetAll,
            SetSeparator,
            SetGroup,
            AddMesh,
        }

        private enum ActionKind
        {
            None,
            Export,
            Launch,
            OpenFolder,
            CreateClip,
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
            internal bool[] Separator = Array.Empty<bool>();
            internal GUIContent[] EditLabels = Array.Empty<GUIContent>();
            internal int[] Ungrouped = Array.Empty<int>();
            internal GroupView[] Groups = Array.Empty<GroupView>();
            internal bool EditSeparators;
        }

        private sealed class GroupView
        {
            internal GUIContent Label = Empty;
            internal GUIContent Count = Empty;
            internal int[] Members = Array.Empty<int>();
            internal bool Expanded;
        }

        private static readonly GUIContent MapHeading = new GUIContent("マップ");
        private static readonly GUIContent MapNameLabel = new GUIContent("マップ名");
        private static readonly GUIContent MapNameHint = new GUIContent("同じマップ名のアバターは、送り手で 1 つのマップを共有します。空欄ならアバター名を使います。");
        private static readonly GUIContent EditSeparatorsLabel = new GUIContent("区切りを編集");
        private const string EditSeparatorsHelp = "チェックしたブレンドシェイプを区切りとして扱います。区切りは操作対象になりません。";
        private static readonly GUIContent GroupOnLabel = new GUIContent("ON");
        private static readonly GUIContent GroupOffLabel = new GUIContent("OFF");
        private static readonly GUILayoutOption[] GroupButtonOptions = { GUILayout.Width(40f) };
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
        private static readonly GUIContent CreateClipLabel = new GUIContent("表情ファイルから AnimationClip を作る…");
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

            var change = ChangeKind.None;
            var changeMeshIndex = -1;
            var changeBlendShape = -1;
            var changeGroupIndex = -1;
            var changeString = string.Empty;
            var changeBool = false;
            SkinnedMeshRenderer changeRenderer = null;
            var action = ActionKind.None;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(MapHeading, EditorStyles.boldLabel);
            var mapName = EditorGUILayout.DelayedTextField(MapNameLabel, receiver.mapName ?? string.Empty);
            if (!string.Equals(mapName, receiver.mapName ?? string.Empty, StringComparison.Ordinal))
            {
                change = ChangeKind.MapName;
                changeString = mapName;
            }

            EditorGUILayout.LabelField(MapNameHint, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(OptionsHeading, EditorStyles.boldLabel);

            var eyes = EditorGUILayout.Toggle(OverrideEyesLabel, receiver.overrideEyes);
            if (change == ChangeKind.None && eyes != receiver.overrideEyes)
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
                    bool allOn;
                    bool allOff;
                    using (new EditorGUI.DisabledScope(view.EditSeparators))
                    {
                        allOn = GUILayout.Button(AllOnLabel);
                        allOff = GUILayout.Button(AllOffLabel);
                    }

                    var editSeparators = GUILayout.Toggle(view.EditSeparators, EditSeparatorsLabel, GUI.skin.button);
                    EditorGUILayout.EndHorizontal();
                    if (editSeparators != view.EditSeparators)
                    {
                        view.EditSeparators = editSeparators;
                        UpdateVisibility(view);
                    }

                    if (view.EditSeparators)
                    {
                        EditorGUILayout.HelpBox(EditSeparatorsHelp, MessageType.Info);
                    }

                    var search = EditorGUILayout.TextField(SearchLabel, view.Search);
                    if (!string.Equals(search, view.Search, StringComparison.Ordinal))
                    {
                        view.Search = search;
                        UpdateVisibility(view);
                    }

                    view.Scroll = EditorGUILayout.BeginScrollView(view.Scroll, ScrollOptions);
                    if (view.EditSeparators || view.Search.Length != 0 || view.Groups.Length == 0)
                    {
                        for (var blendShapeIndex = 0; blendShapeIndex < view.Labels.Length; blendShapeIndex++)
                        {
                            if (!view.Visible[blendShapeIndex])
                            {
                                continue;
                            }

                            var previous = view.EditSeparators ? view.Separator[blendShapeIndex] : view.Enabled[blendShapeIndex];
                            var enabled = EditorGUILayout.ToggleLeft(
                                view.EditSeparators ? view.EditLabels[blendShapeIndex] : view.Labels[blendShapeIndex], previous);
                            if (change == ChangeKind.None && enabled != previous)
                            {
                                change = view.EditSeparators ? ChangeKind.SetSeparator : ChangeKind.SetBlendShape;
                                changeMeshIndex = i;
                                changeBlendShape = blendShapeIndex;
                                changeBool = enabled;
                            }
                        }
                    }
                    else
                    {
                        DrawMembers(view, view.Ungrouped, i, ref change, ref changeMeshIndex, ref changeBlendShape, ref changeBool);
                        for (var groupIndex = 0; groupIndex < view.Groups.Length; groupIndex++)
                        {
                            var group = view.Groups[groupIndex];
                            EditorGUILayout.BeginHorizontal();
                            group.Expanded = EditorGUILayout.Foldout(group.Expanded, group.Label, true);
                            EditorGUILayout.LabelField(group.Count, CountLabelOptions);
                            var groupOn = GUILayout.Button(GroupOnLabel, GroupButtonOptions);
                            var groupOff = GUILayout.Button(GroupOffLabel, GroupButtonOptions);
                            EditorGUILayout.EndHorizontal();
                            if (change == ChangeKind.None && (groupOn || groupOff))
                            {
                                change = ChangeKind.SetGroup;
                                changeMeshIndex = i;
                                changeGroupIndex = groupIndex;
                                changeBool = groupOn;
                            }

                            if (group.Expanded)
                            {
                                EditorGUI.indentLevel++;
                                DrawMembers(view, group.Members, i, ref change, ref changeMeshIndex, ref changeBlendShape, ref changeBool);
                                EditorGUI.indentLevel--;
                            }
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

                if (GUILayout.Button(CreateClipLabel))
                {
                    action = ActionKind.CreateClip;
                }
            }

            ApplyChange(
                receiver,
                change,
                changeMeshIndex,
                changeBlendShape,
                changeBool,
                changeRenderer,
                changeGroupIndex,
                changeString);
            ApplyAction(action);
        }

        private static void DrawMembers(MeshView view, int[] members, int meshIndex,
            ref ChangeKind change, ref int changeMeshIndex, ref int changeBlendShape, ref bool changeBool)
        {
            foreach (var index in members)
            {
                var enabled = EditorGUILayout.ToggleLeft(view.Labels[index], view.Enabled[index]);
                if (change == ChangeKind.None && enabled != view.Enabled[index])
                {
                    change = ChangeKind.SetBlendShape;
                    changeMeshIndex = meshIndex;
                    changeBlendShape = index;
                    changeBool = enabled;
                }
            }
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
            SkinnedMeshRenderer renderer,
            int groupIndex,
            string stringValue)
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
                case ChangeKind.MapName:
                    receiver.mapName = stringValue.Trim();
                    break;
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
                    SetAll(receiver.meshes[meshIndex], _views[meshIndex], boolValue);
                    break;
                case ChangeKind.SetSeparator:
                    KaotsukiSeparators.SetSeparator(receiver.meshes[meshIndex],
                        _views[meshIndex].Labels[blendShapeIndex].text, boolValue);
                    break;
                case ChangeKind.SetGroup:
                    foreach (var member in _views[meshIndex].Groups[groupIndex].Members)
                    {
                        SetBlendShape(receiver.meshes[meshIndex], _views[meshIndex].Labels[member].text, boolValue);
                    }
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

        private static void SetAll(KaotsukiMeshEntry entry, MeshView view, bool enabled)
        {
            if (entry.excludedBlendShapes == null)
            {
                entry.excludedBlendShapes = new List<string>();
            }

            var mesh = entry.renderer.sharedMesh;
            for (var i = 0; i < mesh.blendShapeCount; i++)
            {
                if (view.Separator[i])
                {
                    continue;
                }

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
                case ActionKind.CreateClip:
                    if (KaotsukiExpressionClipWriter.Run((KaotsukiReceiver)target, _avatarRoot))
                    {
                        GUIUtility.ExitGUI();
                    }
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
                var result = KaotsukiMapWriter.Write(table, receiver, _avatarRoot.gameObject);
                if (result.RemovedOtherAvatars)
                {
                    Debug.LogWarning("[Kaotsuki] " + string.Format(KaotsukiErrors.MapSharedDetail, result.MapName));
                }

                return result.Path;
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
                    EditSeparators = old != null && old.EditSeparators,
                };

                var total = mesh == null ? 0 : mesh.blendShapeCount;
                var registered = 0;
                var operable = 0;
                view.Labels = new GUIContent[total];
                view.EditLabels = new GUIContent[total];
                view.Enabled = new bool[total];
                view.Visible = new bool[total];
                view.Separator = KaotsukiSeparators.Resolve(mesh, entry);
                var ungrouped = new List<int>();
                var groups = new List<GroupView>();
                var members = new List<List<int>>();
                var excluded = entry == null || entry.excludedBlendShapes == null
                    ? null
                    : new HashSet<string>(entry.excludedBlendShapes);
                for (var blendShapeIndex = 0; blendShapeIndex < total; blendShapeIndex++)
                {
                    var name = mesh.GetBlendShapeName(blendShapeIndex);
                    var enabled = excluded == null || !excluded.Contains(name);
                    view.Labels[blendShapeIndex] = new GUIContent(name);
                    view.EditLabels[blendShapeIndex] = KaotsukiSeparators.IsAutoSeparator(name)
                        ? new GUIContent(name + "（自動）")
                        : view.Labels[blendShapeIndex];
                    view.Enabled[blendShapeIndex] = enabled;
                    if (view.Separator[blendShapeIndex])
                    {
                        groups.Add(new GroupView { Label = new GUIContent(KaotsukiSeparators.GroupName(name)) });
                        members.Add(new List<int>());
                        continue;
                    }

                    if (groups.Count == 0)
                    {
                        ungrouped.Add(blendShapeIndex);
                    }
                    else
                    {
                        members[members.Count - 1].Add(blendShapeIndex);
                    }

                    operable++;
                    if (enabled)
                    {
                        registered++;
                    }
                }

                view.Ungrouped = ungrouped.ToArray();
                view.Groups = groups.ToArray();
                for (var groupIndex = 0; groupIndex < view.Groups.Length; groupIndex++)
                {
                    var group = view.Groups[groupIndex];
                    group.Members = members[groupIndex].ToArray();
                    var enabledCount = 0;
                    foreach (var member in group.Members)
                    {
                        if (view.Enabled[member])
                        {
                            enabledCount++;
                        }
                    }

                    group.Count = new GUIContent(enabledCount + " / " + group.Members.Length);
                    group.Expanded = old != null && old.Groups.Length == view.Groups.Length && old.Groups[groupIndex].Expanded;
                }

                view.Header = new GUIContent("登録数 " + registered + " / " + operable);
                UpdateVisibility(view);
                _views.Add(view);
            }
        }

        private static void UpdateVisibility(MeshView view)
        {
            for (var i = 0; i < view.Visible.Length; i++)
            {
                view.Visible[i] = (view.EditSeparators || !view.Separator[i]) &&
                                  (view.Search.Length == 0 ||
                                   view.Labels[i].text.IndexOf(view.Search, StringComparison.OrdinalIgnoreCase) >= 0);
            }
        }
    }
}
