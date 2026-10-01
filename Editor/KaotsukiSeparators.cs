using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poyo.Kaotsuki.Editor
{
    /// <summary>自動判定で区切りになった理由。</summary>
    internal enum KaotsukiSeparatorRule
    {
        None,
        // 先頭か末尾に区切り文字が 2 つ続く。
        Characters,
        // 先頭か末尾が記号で、中身が空。
        EmptySymbol,
    }

    /// <summary>ブレンドシェイプの区切り（カテゴリの境目に置かれたダミー）の判定。</summary>
    internal static class KaotsukiSeparators
    {
        internal const string UnnamedGroup = "（名前なし）";
        private static readonly char[] SeparatorChars = { '-', '=', '－', '＝', '─', '━', '―' };

        internal static KaotsukiSeparatorRule[] ResolveAuto(Mesh mesh)
        {
            if (mesh == null)
            {
                return Array.Empty<KaotsukiSeparatorRule>();
            }

            var result = new KaotsukiSeparatorRule[mesh.blendShapeCount];
            Vector3[] vertices = null;
            Vector3[] normals = null;
            Vector3[] tangents = null;
            for (var i = 0; i < result.Length; i++)
            {
                var trimmed = mesh.GetBlendShapeName(i).Trim();
                // NOTE: 2 文字続くことを条件にするのは、eye-close のような普通の名前の中のハイフンを区切りと誤判定しないため。
                if (trimmed.Length >= 2 &&
                    (IsSeparatorChar(trimmed[0]) && IsSeparatorChar(trimmed[1]) ||
                     IsSeparatorChar(trimmed[trimmed.Length - 2]) && IsSeparatorChar(trimmed[trimmed.Length - 1])))
                {
                    result[i] = KaotsukiSeparatorRule.Characters;
                    continue;
                }

                // NOTE: 区切りに使う記号は作者ごとに違い、決まった文字では拾いきれない。区切りは中身の無いダミーなので、中身が空であることを条件にして、記号で始まる・終わる普通のブレンドシェイプを誤判定しないようにする。
                if (trimmed.Length == 0 || !IsSymbol(trimmed[0]) && !IsSymbol(trimmed[trimmed.Length - 1]))
                {
                    continue;
                }

                if (vertices == null)
                {
                    vertices = new Vector3[mesh.vertexCount];
                    normals = new Vector3[mesh.vertexCount];
                    tangents = new Vector3[mesh.vertexCount];
                }

                // NOTE: 全ブレンドシェイプの頂点を読むと重いので、名前の条件に当たったものだけ中身を調べる。
                if (IsEmpty(mesh, i, vertices, normals, tangents))
                {
                    result[i] = KaotsukiSeparatorRule.EmptySymbol;
                }
            }

            return result;
        }

        private static bool IsSymbol(char value) => !char.IsLetterOrDigit(value) && !char.IsWhiteSpace(value);

        private static bool IsEmpty(Mesh mesh, int index, Vector3[] vertices, Vector3[] normals, Vector3[] tangents)
        {
            for (var frame = 0; frame < mesh.GetBlendShapeFrameCount(index); frame++)
            {
                mesh.GetBlendShapeFrameVertices(index, frame, vertices, normals, tangents);
                foreach (var vertex in vertices)
                {
                    if (vertex.sqrMagnitude > 1e-12f)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        internal static bool[] Resolve(Mesh mesh, KaotsukiMeshEntry entry, KaotsukiSeparatorRule[] auto)
        {
            if (mesh == null)
            {
                return Array.Empty<bool>();
            }

            var added = entry?.addedSeparators == null ? null : new HashSet<string>(entry.addedSeparators);
            var removed = entry?.removedSeparators == null ? null : new HashSet<string>(entry.removedSeparators);
            var result = new bool[mesh.blendShapeCount];
            for (var i = 0; i < result.Length; i++)
            {
                var name = mesh.GetBlendShapeName(i);
                var automatic = auto[i] != KaotsukiSeparatorRule.None;
                result[i] = automatic && (removed == null || !removed.Contains(name)) ||
                            added != null && added.Contains(name);
            }

            return result;
        }

        // NOTE: 区切り文字で判定したものは、Mouth (L) のような括弧を残すため記号を取り除かない。
        internal static string GroupName(string name, KaotsukiSeparatorRule rule)
        {
            var trimmed = (name ?? string.Empty).Trim();
            var start = 0;
            var end = trimmed.Length;
            while (start < end && (IsSeparatorChar(trimmed[start]) || char.IsWhiteSpace(trimmed[start]) ||
                                   rule == KaotsukiSeparatorRule.EmptySymbol && IsSymbol(trimmed[start])))
            {
                start++;
            }

            while (end > start && (IsSeparatorChar(trimmed[end - 1]) || char.IsWhiteSpace(trimmed[end - 1]) ||
                                  rule == KaotsukiSeparatorRule.EmptySymbol && IsSymbol(trimmed[end - 1])))
            {
                end--;
            }

            return start == end ? UnnamedGroup : trimmed.Substring(start, end - start);
        }

        internal static void SetSeparator(KaotsukiMeshEntry entry, string name, bool separator, bool automatic)
        {
            if (entry.addedSeparators == null)
            {
                entry.addedSeparators = new List<string>();
            }

            if (entry.removedSeparators == null)
            {
                entry.removedSeparators = new List<string>();
            }

            var overrides = automatic ? entry.removedSeparators : entry.addedSeparators;
            var other = automatic ? entry.addedSeparators : entry.removedSeparators;
            other.RemoveAll(value => string.Equals(value, name, StringComparison.Ordinal));
            if (separator != automatic)
            {
                if (!overrides.Contains(name))
                {
                    overrides.Add(name);
                }
            }
            else
            {
                overrides.RemoveAll(value => string.Equals(value, name, StringComparison.Ordinal));
            }
        }

        private static bool IsSeparatorChar(char value)
        {
            return Array.IndexOf(SeparatorChars, value) >= 0;
        }
    }
}
