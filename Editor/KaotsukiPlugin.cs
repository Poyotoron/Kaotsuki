using System.IO;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using nadena.dev.ndmf.fluent;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

[assembly: ExportsPlugin(typeof(Poyo.Kaotsuki.Editor.KaotsukiPlugin))]

namespace Poyo.Kaotsuki.Editor
{
    public sealed class KaotsukiPlugin : Plugin<KaotsukiPlugin>
    {
        public override string QualifiedName => KaotsukiInfo.PackageId;
        public override string DisplayName => KaotsukiInfo.DisplayName;

        protected override void Configure()
        {
            // NOTE: 生成した Modular Avatar のコンポーネントを Modular Avatar 自身に処理させるため、先に走らせる。
            InPhase(BuildPhase.Generating)
                .BeforePlugin("nadena.dev.modular-avatar")
                .Run("Generate receiver", Generate);
        }

        private static void Generate(BuildContext ctx)
        {
            var receivers = ctx.AvatarRootObject.GetComponentsInChildren<KaotsukiReceiver>(true);
            if (receivers.Length == 0)
            {
                return;
            }

            if (receivers.Length > 1)
            {
                KaotsukiErrors.Report(ErrorSeverity.Error, KaotsukiErrors.Multiple);
                return;
            }

            var receiver = receivers[0];
            var table = KaotsukiSlotTable.Build(receiver, ctx.AvatarRootTransform);
            if (table.SkippedReasons.Count > 0)
            {
                KaotsukiErrors.Report(
                    ErrorSeverity.NonFatal,
                    KaotsukiErrors.Skipped,
                    string.Join("\n", table.SkippedReasons));
            }

            if (table.TruncatedCount > 0)
            {
                KaotsukiErrors.Report(ErrorSeverity.NonFatal, KaotsukiErrors.Truncated, table.TruncatedCount);
            }

            if (table.Slots.Count == 0)
            {
                KaotsukiErrors.Report(ErrorSeverity.NonFatal, KaotsukiErrors.NoSlots);
                return;
            }

            KaotsukiTrackingShapes.CreateCopies(table);
            var controller = KaotsukiAnimatorBuilder.Build(table, receiver.overrideEyes);
            var merge = receiver.gameObject.AddComponent<ModularAvatarMergeAnimator>();
            merge.animator = controller;
            merge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
            merge.pathMode = MergeAnimatorPathMode.Absolute;
            merge.matchAvatarWriteDefaults = false;
            // NOTE: 他の Merge Animator より後ろに並べ、ON の間は登録済みの表情を確実に上書きする。
            merge.layerPriority = 10000;
            merge.deleteAttachedAnimator = false;
            merge.mergeAnimatorMode = MergeAnimatorMode.Append;

            var parameters = receiver.gameObject.AddComponent<ModularAvatarParameters>();
            parameters.parameters.Add(CreateParameter(KaotsukiInfo.ParamEnabled, ParameterSyncType.Bool));
            parameters.parameters.Add(CreateParameter(KaotsukiInfo.ParamLipSync, ParameterSyncType.Bool));
            for (var channel = 1; channel <= table.ChannelCount; channel++)
            {
                parameters.parameters.Add(CreateParameter(KaotsukiInfo.IndexParam(channel), ParameterSyncType.Int));
                parameters.parameters.Add(CreateParameter(KaotsukiInfo.ValueParam(channel), ParameterSyncType.Int));
            }

            try
            {
                var result = KaotsukiMapWriter.Write(table, receiver, ctx.AvatarRootObject);
                Debug.Log("[Kaotsuki] マップを書き出しました: " + result.Path);
                if (result.RemovedOtherAvatars)
                {
                    KaotsukiErrors.Report(ErrorSeverity.NonFatal, KaotsukiErrors.MapShared, result.MapName);
                }
            }
            catch (IOException e)
            {
                KaotsukiErrors.Report(ErrorSeverity.NonFatal, KaotsukiErrors.MapWrite, e.Message);
            }
            catch (System.UnauthorizedAccessException e)
            {
                KaotsukiErrors.Report(ErrorSeverity.NonFatal, KaotsukiErrors.MapWrite, e.Message);
            }
        }

        private static ParameterConfig CreateParameter(string name, ParameterSyncType type)
        {
            return new ParameterConfig
            {
                nameOrPrefix = name,
                syncType = type,
                defaultValue = 0f,
                saved = false,
                localOnly = false,
                hasExplicitDefaultValue = true,
            };
        }
    }
}
