using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Poyo.Kaotsuki.Editor
{
    internal static class KaotsukiMapWriter
    {
        /// <summary>%LOCALAPPDATA%\Kaotsuki\Maps</summary>
        internal static string MapFolder { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kaotsuki",
            "Maps");

        internal static string SanitizeAvatarName(string name)
        {
            var sanitized = name ?? string.Empty;
            while (sanitized.EndsWith("(Clone)", StringComparison.Ordinal))
            {
                sanitized = sanitized.Substring(0, sanitized.Length - "(Clone)".Length);
            }

            sanitized = sanitized.Trim();
            var invalid = new System.Collections.Generic.HashSet<char>(Path.GetInvalidFileNameChars());
            var chars = sanitized.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (invalid.Contains(chars[i]))
                {
                    chars[i] = '_';
                }
            }

            sanitized = new string(chars);
            return sanitized.Length == 0 ? "Avatar" : sanitized;
        }

        internal static string GetMapPath(GameObject avatarRoot)
        {
            return Path.Combine(MapFolder, SanitizeAvatarName(avatarRoot.name) + ".json");
        }

        /// <summary>マップを書き出し、書いたファイルの絶対パスを返す。I/O の失敗は例外のまま投げる。</summary>
        internal static string Write(KaotsukiSlotTable table, GameObject avatarRoot)
        {
            var pipeline = avatarRoot.GetComponent<VRC.Core.PipelineManager>();
            var map = new KaotsukiMap
            {
                avatarName = SanitizeAvatarName(avatarRoot.name),
                blueprintId = pipeline == null || pipeline.blueprintId == null ? string.Empty : pipeline.blueprintId,
                generatedAt = DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
            };

            for (var channel = 1; channel <= table.ChannelCount; channel++)
            {
                map.parameters.channels.Add(new KaotsukiMapChannel
                {
                    index = KaotsukiInfo.IndexParam(channel),
                    value = KaotsukiInfo.ValueParam(channel),
                });
            }

            foreach (var slot in table.Slots)
            {
                map.slots.Add(new KaotsukiMapSlot
                {
                    channel = slot.Channel,
                    index = slot.Index,
                    mesh = slot.Path,
                    blendShape = slot.BlendShape,
                    defaultWeight = slot.DefaultWeight,
                });
            }

            Directory.CreateDirectory(MapFolder);
            var path = GetMapPath(avatarRoot);
            File.WriteAllText(path, JsonUtility.ToJson(map, true), new UTF8Encoding(false));
            return path;
        }
    }
}
