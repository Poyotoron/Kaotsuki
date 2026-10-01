using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace Poyo.Kaotsuki.Editor
{
    internal static class KaotsukiAnimatorBuilder
    {
        internal const string LayerControl = "Kaotsuki Control";
        internal const string LayerReceive = "Kaotsuki Receive";
        internal const string LayerDrive = "Kaotsuki Drive";

        private static int DriveLayerIndex(int channelCount) => 1 + channelCount;

        internal static AnimatorController Build(KaotsukiSlotTable table, bool overrideEyes)
        {
            var controller = new AnimatorController { name = "Kaotsuki" };
            AddParameters(controller, table);
            AddControlLayer(controller, table, overrideEyes);
            for (var channel = 1; channel <= table.ChannelCount; channel++)
            {
                AddReceiveLayer(controller, table, channel);
            }

            AddDriveLayer(controller, table);
            return controller;
        }

        private static void AddParameters(AnimatorController controller, KaotsukiSlotTable table)
        {
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = KaotsukiInfo.ParamEnabled,
                type = AnimatorControllerParameterType.Bool,
                defaultBool = false,
            });
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = KaotsukiInfo.ParamLipSync,
                type = AnimatorControllerParameterType.Bool,
                defaultBool = false,
            });
            for (var channel = 1; channel <= table.ChannelCount; channel++)
            {
                controller.AddParameter(new AnimatorControllerParameter
                {
                    name = KaotsukiInfo.IndexParam(channel),
                    type = AnimatorControllerParameterType.Int,
                    defaultInt = 0,
                });
                controller.AddParameter(new AnimatorControllerParameter
                {
                    name = KaotsukiInfo.ValueParam(channel),
                    type = AnimatorControllerParameterType.Int,
                    defaultInt = 0,
                });
            }

            controller.AddParameter(new AnimatorControllerParameter
            {
                name = KaotsukiInfo.ParamOne,
                type = AnimatorControllerParameterType.Float,
                defaultFloat = 1f,
            });

            foreach (var slot in table.Slots)
            {
                controller.AddParameter(new AnimatorControllerParameter
                {
                    name = KaotsukiInfo.SlotParam(slot.Number),
                    type = AnimatorControllerParameterType.Float,
                    defaultFloat = slot.DefaultWeight / 100f,
                });
            }
        }

        private static void AddControlLayer(
            AnimatorController controller,
            KaotsukiSlotTable table,
            bool overrideEyes)
        {
            var machine = new AnimatorStateMachine { name = LayerControl };
            // NOTE: 読み込み直後にトラッキングを Tracking へ戻すと他のギミックの設定を上書きするため、最初は何もしないステートから始める。
            var init = machine.AddState("Init", new Vector3(0, 0));
            var on = machine.AddState("On", new Vector3(250, -80));
            // NOTE: ON の間も口パクを動かすかは LipSync で選ぶ。On と On LipSync の間を移っても Layer Control は同じ値を入れ直すだけ。
            var onLipSync = machine.AddState("On LipSync", new Vector3(250, 0));
            var off = machine.AddState("Off", new Vector3(250, 80));
            init.writeDefaultValues = true;
            on.writeDefaultValues = true;
            onLipSync.writeDefaultValues = true;
            off.writeDefaultValues = true;
            machine.defaultState = init;

            AddTransition(on, off, (AnimatorConditionMode.IfNot, KaotsukiInfo.ParamEnabled));
            AddTransition(onLipSync, off, (AnimatorConditionMode.IfNot, KaotsukiInfo.ParamEnabled));
            // NOTE: Tracking Control はステートに入ったときに 1 度しか効かない。表情のレイヤーや VRChat が後から口のトラッキングを戻すと（アイテムやカメラを持ったときなど）、口パクが戻ってしまう。ON の間は毎フレーム入り直し、トラッキングとレイヤーの重みを設定し直す。
            AddAnyStateTransition(machine, on, (AnimatorConditionMode.If, KaotsukiInfo.ParamEnabled),
                (AnimatorConditionMode.IfNot, KaotsukiInfo.ParamLipSync));
            AddAnyStateTransition(machine, onLipSync, (AnimatorConditionMode.If, KaotsukiInfo.ParamEnabled),
                (AnimatorConditionMode.If, KaotsukiInfo.ParamLipSync));

            var onBehaviours = new List<StateMachineBehaviour>
            {
                CreateLayerControl(1f, table.ChannelCount),
                CreateTrackingControl(overrideEyes, true, VRC_AnimatorTrackingControl.TrackingType.Animation),
            };
            onLipSync.behaviours = new StateMachineBehaviour[]
            {
                CreateLayerControl(1f, table.ChannelCount),
                CreateTrackingControl(overrideEyes, true, VRC_AnimatorTrackingControl.TrackingType.Tracking),
            };
            var offBehaviours = new List<StateMachineBehaviour>
            {
                CreateLayerControl(0f, table.ChannelCount),
                CreateTrackingControl(overrideEyes, false, VRC_AnimatorTrackingControl.TrackingType.Tracking),
            };

            // NOTE: Index / Value は同期パラメータなので自分だけで 0 に戻す。スロットの値は同期されないので、他のプレイヤーの画面でも既定値に戻す。
            var resetSynced = ScriptableObject.CreateInstance<VRCAvatarParameterDriver>();
            resetSynced.localOnly = true;
            resetSynced.parameters = new List<VRC_AvatarParameterDriver.Parameter>();
            for (var channel = 1; channel <= table.ChannelCount; channel++)
            {
                resetSynced.parameters.Add(new VRC_AvatarParameterDriver.Parameter
                {
                    name = KaotsukiInfo.IndexParam(channel),
                    type = VRC_AvatarParameterDriver.ChangeType.Set,
                    value = 0f,
                });
                resetSynced.parameters.Add(new VRC_AvatarParameterDriver.Parameter
                {
                    name = KaotsukiInfo.ValueParam(channel),
                    type = VRC_AvatarParameterDriver.ChangeType.Set,
                    value = 0f,
                });
            }

            offBehaviours.Add(resetSynced);

            var resetSlots = ScriptableObject.CreateInstance<VRCAvatarParameterDriver>();
            resetSlots.localOnly = false;
            resetSlots.parameters = new List<VRC_AvatarParameterDriver.Parameter>();
            foreach (var slot in table.Slots)
            {
                resetSlots.parameters.Add(new VRC_AvatarParameterDriver.Parameter
                {
                    name = KaotsukiInfo.SlotParam(slot.Number),
                    type = VRC_AvatarParameterDriver.ChangeType.Set,
                    value = slot.DefaultWeight / 100f,
                });
            }

            offBehaviours.Add(resetSlots);
            on.behaviours = onBehaviours.ToArray();
            off.behaviours = offBehaviours.ToArray();

            controller.AddLayer(new AnimatorControllerLayer
            {
                name = LayerControl,
                defaultWeight = 1f,
                stateMachine = machine,
            });
        }

        private static void AddReceiveLayer(AnimatorController controller, KaotsukiSlotTable table, int channel)
        {
            var layerName = channel == 1 ? LayerReceive : LayerReceive + " " + channel;
            var machine = new AnimatorStateMachine { name = layerName };
            var idle = machine.AddState("Idle", new Vector3(0, 0));
            idle.writeDefaultValues = true;
            machine.defaultState = idle;

            foreach (var slot in table.Slots)
            {
                if (slot.Channel != channel)
                {
                    continue;
                }

                var state = machine.AddState(
                    "Slot " + slot.Number.ToString("0000"),
                    new Vector3(250 + 220 * ((slot.Index - 1) % 8), 80 * ((slot.Index - 1) / 8)));
                state.writeDefaultValues = true;

                var driver = ScriptableObject.CreateInstance<VRCAvatarParameterDriver>();
                driver.localOnly = false;
                driver.parameters = new List<VRC_AvatarParameterDriver.Parameter>
                {
                    new VRC_AvatarParameterDriver.Parameter
                    {
                        type = VRC_AvatarParameterDriver.ChangeType.Copy,
                        source = KaotsukiInfo.ValueParam(channel),
                        name = KaotsukiInfo.SlotParam(slot.Number),
                        convertRange = true,
                        sourceMin = 0f,
                        sourceMax = KaotsukiInfo.ValueMax,
                        destMin = 0f,
                        destMax = 1f,
                    },
                };
                state.behaviours = new StateMachineBehaviour[] { driver };

                var transition = machine.AddAnyStateTransition(state);
                transition.AddCondition(AnimatorConditionMode.If, 0f, KaotsukiInfo.ParamEnabled);
                transition.AddCondition(AnimatorConditionMode.Equals, slot.Index, KaotsukiInfo.IndexParam(channel));
                // NOTE: 同じ Index のまま Value だけ変わった場合にも Driver を毎フレーム実行し直す。
                transition.canTransitionToSelf = true;
                transition.hasExitTime = false;
                transition.hasFixedDuration = true;
                transition.duration = 0f;
                transition.exitTime = 0f;
            }

            controller.AddLayer(new AnimatorControllerLayer
            {
                name = layerName,
                defaultWeight = 1f,
                stateMachine = machine,
            });
        }

        private static void AddDriveLayer(AnimatorController controller, KaotsukiSlotTable table)
        {
            var machine = new AnimatorStateMachine { name = LayerDrive };
            var state = machine.AddState("Drive", new Vector3(0, 0));
            state.writeDefaultValues = true;
            machine.defaultState = state;

            var directTree = new BlendTree
            {
                name = LayerDrive,
                blendType = BlendTreeType.Direct,
            };
            var directChildren = new ChildMotion[table.Slots.Count];
            for (var i = 0; i < table.Slots.Count; i++)
            {
                var slot = table.Slots[i];
                var name = "Slot " + slot.Number.ToString("0000");
                var slotTree = new BlendTree
                {
                    name = name,
                    blendType = BlendTreeType.Simple1D,
                    blendParameter = KaotsukiInfo.SlotParam(slot.Number),
                    useAutomaticThresholds = false,
                };
                slotTree.children = new[]
                {
                    new ChildMotion { motion = CreateClip(slot, name + " 0", 0f), threshold = 0f, timeScale = 1f },
                    new ChildMotion { motion = CreateClip(slot, name + " 100", 100f), threshold = 1f, timeScale = 1f },
                };
                directChildren[i] = new ChildMotion
                {
                    motion = slotTree,
                    directBlendParameter = KaotsukiInfo.ParamOne,
                    timeScale = 1f,
                };
            }

            directTree.children = directChildren;
            state.motion = directTree;

            controller.AddLayer(new AnimatorControllerLayer
            {
                name = LayerDrive,
                // NOTE: 重み 0 の間は何も書かず、他のレイヤーの表情をそのまま通す。FX の最後に置かれるので、重み 1 の間は登録したブレンドシェイプを上書きする。
                defaultWeight = 0f,
                stateMachine = machine,
            });
        }

        private static AnimationClip CreateClip(KaotsukiSlot slot, string name, float weight)
        {
            var clip = new AnimationClip { name = name };
            var binding = EditorCurveBinding.FloatCurve(
                slot.Path,
                typeof(SkinnedMeshRenderer),
                "blendShape." + slot.AnimatedBlendShape);
            AnimationUtility.SetEditorCurve(clip, binding, new AnimationCurve(new Keyframe(0f, weight)));
            return clip;
        }

        private static VRCAnimatorLayerControl CreateLayerControl(float weight, int channelCount)
        {
            var control = ScriptableObject.CreateInstance<VRCAnimatorLayerControl>();
            control.playable = VRC_AnimatorLayerControl.BlendableLayer.FX;
            control.layer = DriveLayerIndex(channelCount);
            control.goalWeight = weight;
            control.blendDuration = 0f;
            return control;
        }

        private static VRCAnimatorTrackingControl CreateTrackingControl(
            bool overrideEyes,
            bool eyesAnimation,
            VRC_AnimatorTrackingControl.TrackingType mouth)
        {
            var noChange = VRC_AnimatorTrackingControl.TrackingType.NoChange;
            var control = ScriptableObject.CreateInstance<VRCAnimatorTrackingControl>();
            control.trackingHead = noChange;
            control.trackingLeftHand = noChange;
            control.trackingRightHand = noChange;
            control.trackingHip = noChange;
            control.trackingLeftFoot = noChange;
            control.trackingRightFoot = noChange;
            control.trackingLeftFingers = noChange;
            control.trackingRightFingers = noChange;
            control.trackingEyes = overrideEyes
                ? eyesAnimation
                    ? VRC_AnimatorTrackingControl.TrackingType.Animation
                    : VRC_AnimatorTrackingControl.TrackingType.Tracking
                : noChange;
            control.trackingMouth = mouth;
            return control;
        }

        private static void AddTransition(
            AnimatorState source,
            AnimatorState destination,
            params (AnimatorConditionMode mode, string parameter)[] conditions)
        {
            var transition = source.AddTransition(destination);
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0f;
            transition.exitTime = 0f;
            foreach (var condition in conditions)
            {
                transition.AddCondition(condition.mode, 0f, condition.parameter);
            }
        }

        private static void AddAnyStateTransition(
            AnimatorStateMachine machine,
            AnimatorState destination,
            params (AnimatorConditionMode mode, string parameter)[] conditions)
        {
            var transition = machine.AddAnyStateTransition(destination);
            transition.canTransitionToSelf = true;
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0f;
            transition.exitTime = 0f;
            foreach (var condition in conditions)
            {
                transition.AddCondition(condition.mode, 0f, condition.parameter);
            }
        }
    }
}
