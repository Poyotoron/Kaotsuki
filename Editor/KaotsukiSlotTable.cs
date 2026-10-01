using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Poyo.Kaotsuki.Editor
{
    internal sealed class KaotsukiSlot
    {
        internal int Number;
        internal int Channel;
        internal int Index;
        internal SkinnedMeshRenderer Renderer;
        internal string Path;
        internal string BlendShape;
        internal KaotsukiTrackingKind Tracking;
        internal string AnimatedBlendShape;
        internal float DefaultWeight;
        internal string Group;
    }

    internal sealed class KaotsukiSlotTable
    {
        internal IReadOnlyList<KaotsukiSlot> Slots { get; }
        internal int ChannelCount =>
            (_slots.Count + KaotsukiInfo.SlotsPerChannel - 1) / KaotsukiInfo.SlotsPerChannel;
        internal int RegisteredCount { get; private set; }
        internal int TruncatedCount { get; private set; }
        internal IReadOnlyList<string> SkippedReasons { get; }

        private readonly List<KaotsukiSlot> _slots = new List<KaotsukiSlot>();
        private readonly List<string> _skippedReasons = new List<string>();

        private KaotsukiSlotTable()
        {
            Slots = _slots;
            SkippedReasons = _skippedReasons;
        }

        internal static KaotsukiSlotTable Build(KaotsukiReceiver receiver, Transform avatarRoot)
        {
            var table = new KaotsukiSlotTable();
            if (receiver == null || avatarRoot == null || receiver.meshes == null)
            {
                return table;
            }

            var tracking = KaotsukiTrackingShapes.Resolve(avatarRoot);
            var acceptedRenderers = new HashSet<SkinnedMeshRenderer>();
            for (var i = 0; i < receiver.meshes.Count; i++)
            {
                var entry = receiver.meshes[i];
                if (entry == null || entry.renderer == null)
                {
                    table._skippedReasons.Add((i + 1) + " 番目: メッシュが設定されていません");
                    continue;
                }

                var renderer = entry.renderer;
                var mesh = renderer.sharedMesh;
                if (mesh == null)
                {
                    table._skippedReasons.Add(renderer.name + ": Mesh がありません");
                    continue;
                }

                if (!renderer.transform.IsChildOf(avatarRoot))
                {
                    table._skippedReasons.Add(renderer.name + ": アバターの外にあります");
                    continue;
                }

                if (!acceptedRenderers.Add(renderer))
                {
                    table._skippedReasons.Add(renderer.name + ": 重複して登録されています");
                    continue;
                }

                var excluded = entry.excludedBlendShapes == null
                    ? new HashSet<string>()
                    : new HashSet<string>(entry.excludedBlendShapes);
                var path = AnimationUtility.CalculateTransformPath(renderer.transform, avatarRoot);
                var auto = KaotsukiSeparators.ResolveAuto(mesh);
                var separators = KaotsukiSeparators.Resolve(mesh, entry, auto);
                var frozen = KaotsukiFrozenShapes.Resolve(renderer);
                var group = string.Empty;

                for (var blendShapeIndex = 0; blendShapeIndex < mesh.blendShapeCount; blendShapeIndex++)
                {
                    var blendShape = mesh.GetBlendShapeName(blendShapeIndex);
                    // NOTE: 区切りは中身の無いダミーなので操作対象にしない。除外されていても、グループ分けには使う。
                    if (separators[blendShapeIndex])
                    {
                        group = KaotsukiSeparators.GroupName(blendShape, auto[blendShapeIndex]);
                        continue;
                    }

                    // NOTE: 固定されるブレンドシェイプはビルドの後段でメッシュから消えるので、動かしても何も起きない。アニメーションに入れると警告も出るため、スロットにしない。
                    if (frozen.Contains(blendShape))
                    {
                        continue;
                    }

                    if (excluded.Contains(blendShape))
                    {
                        continue;
                    }

                    table.RegisteredCount++;
                    if (table._slots.Count >= KaotsukiInfo.MaxSlots)
                    {
                        table.TruncatedCount++;
                        continue;
                    }

                    var kind = tracking.KindOf(renderer, blendShape);
                    var number = table._slots.Count + 1;
                    table._slots.Add(new KaotsukiSlot
                    {
                        Number = number,
                        Channel = (number - 1) / KaotsukiInfo.SlotsPerChannel + 1,
                        Index = (number - 1) % KaotsukiInfo.SlotsPerChannel + 1,
                        Renderer = renderer,
                        Path = path,
                        BlendShape = blendShape,
                        Tracking = kind,
                        AnimatedBlendShape = blendShape,
                        // NOTE: 口パク・まばたきは複製を動かすので、複製の既定値 0 を既定値にする。元の値はメッシュに残るので見た目は変わらない。
                        DefaultWeight = kind != KaotsukiTrackingKind.None
                            ? 0f
                            : Mathf.Clamp(renderer.GetBlendShapeWeight(blendShapeIndex), 0f, 100f),
                        Group = group,
                    });
                }
            }

            return table;
        }

        internal static Transform FindAvatarRoot(Component component)
        {
            if (component == null)
            {
                return null;
            }

            var descriptor = component.GetComponentInParent<VRCAvatarDescriptor>(true);
            return descriptor == null ? null : descriptor.transform;
        }
    }
}
