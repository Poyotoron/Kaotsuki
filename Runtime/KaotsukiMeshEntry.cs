using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poyo.Kaotsuki
{
    /// <summary>操作対象として登録したメッシュ 1 件分の設定。</summary>
    [Serializable]
    public sealed class KaotsukiMeshEntry
    {
        public SkinnedMeshRenderer renderer;

        // NOTE: 登録する名前ではなく除外する名前を持つ。アバターの更新で増えたブレンドシェイプを
        //       既定で操作対象に含めるため。名前なら並びが変わっても別の対象を指さない。
        // NOTE: 区切りの手動設定も、並びが変わっても別の対象を指さないように名前で持つ。
        public List<string> excludedBlendShapes = new List<string>();

        // 自動判定に加えて区切りとして扱うブレンドシェイプ名。
        public List<string> addedSeparators = new List<string>();
        // 自動判定で区切りになっても、区切りとして扱わないブレンドシェイプ名。
        public List<string> removedSeparators = new List<string>();
    }
}
