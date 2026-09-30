using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Poyo.Kaotsuki.Editor
{
    [Serializable]
    internal sealed class KaotsukiExpressionFile
    {
        public string format;
        public int version;
        public string mapName;
        public string savedAt;
        public List<KaotsukiExpressionEntry> blendShapes;
    }

    [Serializable]
    internal sealed class KaotsukiExpressionEntry
    {
        public string mesh;
        public string blendShape;
        public float weight;
    }

    internal static class KaotsukiExpression
    {
        /// <summary>%LOCALAPPDATA%\Kaotsuki\Expressions</summary>
        internal static string Folder { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kaotsuki", "Expressions");

        /// <summary>表情ファイルを読み、使えるエントリだけを返す。形式が違えば null。</summary>
        internal static List<KaotsukiExpressionEntry> Load(string path)
        {
            KaotsukiExpressionFile file;
            try
            {
                var text = File.ReadAllText(path, Encoding.UTF8).TrimStart('\uFEFF');
                file = JsonUtility.FromJson<KaotsukiExpressionFile>(text);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
            catch (ArgumentException)
            {
                return null;
            }

            if (file == null || file.format != "kaotsuki-expression" || file.version != 1 || file.blendShapes == null)
            {
                return null;
            }

            return file.blendShapes.FindAll(entry => entry != null &&
                !string.IsNullOrEmpty(entry.mesh) && !string.IsNullOrEmpty(entry.blendShape) &&
                !float.IsNaN(entry.weight) && !float.IsInfinity(entry.weight) && entry.weight >= 0f && entry.weight <= 100f);
        }

        /// <summary>スロットごとのウェイトを照合で決める。</summary>
        internal static float[] Match(IReadOnlyList<KaotsukiSlot> slots, List<KaotsukiExpressionEntry> entries,
            out int applied, out int unmatched)
        {
            var weights = new float[slots.Count];
            var used = new HashSet<int>();
            for (var i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                var match = entries.FindIndex(entry =>
                    string.Equals(entry.mesh, slot.Path, StringComparison.Ordinal) &&
                    string.Equals(entry.blendShape, slot.BlendShape, StringComparison.Ordinal));

                // NOTE: 別のアバターでは顔のメッシュのパスが違うことがある。同じ名前が 1 つだけなら同じブレンドシェイプとみなす。
                if (match < 0)
                {
                    var unique = -1;
                    for (var entryIndex = 0; entryIndex < entries.Count; entryIndex++)
                    {
                        if (!string.Equals(entries[entryIndex].blendShape, slot.BlendShape, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (unique >= 0)
                        {
                            unique = -1;
                            break;
                        }

                        unique = entryIndex;
                    }

                    match = unique;
                }

                if (match >= 0)
                {
                    weights[i] = entries[match].weight;
                    used.Add(match);
                }
                else
                {
                    weights[i] = slot.DefaultWeight;
                }
            }

            applied = used.Count;
            unmatched = entries.Count - applied;
            return weights;
        }
    }
}
