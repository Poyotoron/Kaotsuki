using System.Collections.Generic;
using UnityEngine;

namespace Poyo.Kaotsuki
{
    /// <summary>アバターの表情を OSC から操作するための設定。アップロード時には取り除かれる。</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Kaotsuki/Kaotsuki Receiver")]
    public sealed class KaotsukiReceiver : MonoBehaviour, VRC.SDKBase.IEditorOnly
    {
        public List<KaotsukiMeshEntry> meshes = new List<KaotsukiMeshEntry>();

        // ON の間、まばたき・視線を止める。
        public bool overrideEyes = true;
        // ON の間、リップシンクを止める。
        public bool overrideMouth = true;
    }
}
