using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poyo.Kaotsuki.Editor
{
    /// <summary>ブレンドシェイプの区切り（カテゴリの境目に置かれたダミー）の判定。</summary>
    internal static class KaotsukiSeparators
    {
        internal const string UnnamedGroup = "（名前なし）";
        private static readonly char[] SeparatorChars = { '-', '=', '－', '＝', '─', '━', '―' };

        // NOTE: 2 文字続くことを条件にするのは、eye-close のような普通の名前の中のハイフンを区切りと誤判定しないため。
        internal static bool IsAutoSeparator(string name)
        {
            if (name == null)
            {
                return false;
            }

            var trimmed = name.Trim();
            return trimmed.Length >= 2 &&
                   (IsSeparatorChar(trimmed[0]) && IsSeparatorChar(trimmed[1]) ||
                    IsSeparatorChar(trimmed[trimmed.Length - 2]) && IsSeparatorChar(trimmed[trimmed.Length - 1]));
        }

        internal static bool IsSeparator(string name, HashSet<string> added, HashSet<string> removed)
        {
            return IsAutoSeparator(name) && (removed == null || !removed.Contains(name)) ||
                   added != null && added.Contains(name);
        }

        internal static bool[] Resolve(Mesh mesh, KaotsukiMeshEntry entry)
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
                result[i] = IsSeparator(mesh.GetBlendShapeName(i), added, removed);
            }

            return result;
        }

        internal static string GroupName(string name)
        {
            var trimmed = (name ?? string.Empty).Trim();
            var start = 0;
            var end = trimmed.Length;
            while (start < end && (IsSeparatorChar(trimmed[start]) || char.IsWhiteSpace(trimmed[start])))
            {
                start++;
            }

            while (end > start && (IsSeparatorChar(trimmed[end - 1]) || char.IsWhiteSpace(trimmed[end - 1])))
            {
                end--;
            }

            return start == end ? UnnamedGroup : trimmed.Substring(start, end - start);
        }

        internal static void SetSeparator(KaotsukiMeshEntry entry, string name, bool separator)
        {
            if (entry.addedSeparators == null)
            {
                entry.addedSeparators = new List<string>();
            }

            if (entry.removedSeparators == null)
            {
                entry.removedSeparators = new List<string>();
            }

            var automatic = IsAutoSeparator(name);
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
