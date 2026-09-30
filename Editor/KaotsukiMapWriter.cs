using System;
using System.Collections.Generic;
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

        internal readonly struct WriteResult
        {
            internal readonly string Path;
            internal readonly string MapName;
            internal readonly bool RemovedOtherAvatars;

            internal WriteResult(string path, string mapName, bool removedOtherAvatars)
            {
                Path = path;
                MapName = mapName;
                RemovedOtherAvatars = removedOtherAvatars;
            }
        }

        internal static string SanitizeName(string name)
        {
            var sanitized = name ?? string.Empty;
            while (sanitized.EndsWith("(Clone)", StringComparison.Ordinal))
            {
                sanitized = sanitized.Substring(0, sanitized.Length - "(Clone)".Length);
            }

            sanitized = sanitized.Trim();
            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
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

        /// <summary>マップ名。mapName が空ならアバター名。</summary>
        internal static string ResolveMapName(KaotsukiReceiver receiver, GameObject avatarRoot)
        {
            var name = receiver.mapName?.Trim();
            return SanitizeName(string.IsNullOrEmpty(name) ? avatarRoot.name : name);
        }

        internal static string GetMapPath(KaotsukiReceiver receiver, GameObject avatarRoot)
        {
            return Path.Combine(MapFolder, ResolveMapName(receiver, avatarRoot) + ".json");
        }

        /// <summary>マップを書き出す。I/O の失敗は例外のまま投げる。</summary>
        internal static WriteResult Write(KaotsukiSlotTable table, KaotsukiReceiver receiver, GameObject avatarRoot)
        {
            var pipeline = avatarRoot.GetComponent<VRC.Core.PipelineManager>();
            var mapName = ResolveMapName(receiver, avatarRoot);
            var map = new KaotsukiMap
            {
                mapName = mapName,
                avatarName = SanitizeName(avatarRoot.name),
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
                    group = slot.Group,
                });
            }

            var current = new KaotsukiMapAvatar { name = map.avatarName, blueprintId = map.blueprintId };
            var path = GetMapPath(receiver, avatarRoot);
            var existing = TryReadExisting(path);
            var removed = false;
            if (existing != null && SameStructure(existing, map))
            {
                foreach (var avatar in existing.avatars)
                {
                    if (avatar != null)
                    {
                        map.avatars.Add(new KaotsukiMapAvatar { name = avatar.name, blueprintId = avatar.blueprintId });
                    }
                }

                Merge(map.avatars, current);
            }
            else
            {
                map.avatars.Add(current);
                removed = existing != null && existing.avatars.Exists(avatar => avatar != null && !Matches(avatar, current));
            }

            Directory.CreateDirectory(MapFolder);
            File.WriteAllText(path, JsonUtility.ToJson(map, true), new UTF8Encoding(false));
            return new WriteResult(path, mapName, removed);
        }

        private static KaotsukiMap TryReadExisting(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            KaotsukiMap map;
            // NOTE: 読めない既存ファイルは共有の相手が分からないので、今のアバターだけのマップとして書き直す。
            try
            {
                map = JsonUtility.FromJson<KaotsukiMap>(File.ReadAllText(path, Encoding.UTF8));
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

            if (map == null || map.format != "kaotsuki-map" || map.version != 1 || map.slots == null)
            {
                return null;
            }

            if (map.avatars == null || map.avatars.Count == 0)
            {
                map.avatars = new List<KaotsukiMapAvatar>();
                if (!string.IsNullOrEmpty(map.avatarName))
                {
                    map.avatars.Add(new KaotsukiMapAvatar { name = map.avatarName, blueprintId = map.blueprintId ?? string.Empty });
                }
            }

            return map;
        }

        // NOTE: 衣装違いで体型の既定値だけが違うアバターでも共有できるように、既定値は比べない。
        private static bool SameStructure(KaotsukiMap a, KaotsukiMap b)
        {
            if (a.slots.Count != b.slots.Count)
            {
                return false;
            }

            for (var i = 0; i < a.slots.Count; i++)
            {
                var left = a.slots[i];
                var right = b.slots[i];
                if (left == null || right == null || left.channel != right.channel || left.index != right.index ||
                    !string.Equals(left.mesh, right.mesh, StringComparison.Ordinal) ||
                    !string.Equals(left.blendShape, right.blendShape, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Matches(KaotsukiMapAvatar entry, KaotsukiMapAvatar current)
        {
            return !string.IsNullOrEmpty(current.blueprintId) &&
                   string.Equals(entry.blueprintId, current.blueprintId, StringComparison.Ordinal) ||
                   string.IsNullOrEmpty(entry.blueprintId) && string.Equals(entry.name, current.name, StringComparison.Ordinal);
        }

        private static void Merge(List<KaotsukiMapAvatar> list, KaotsukiMapAvatar current)
        {
            if (!string.IsNullOrEmpty(current.blueprintId))
            {
                var sameId = list.Find(avatar => string.Equals(avatar.blueprintId, current.blueprintId, StringComparison.Ordinal));
                if (sameId != null)
                {
                    sameId.name = current.name;
                    return;
                }
            }

            var sameName = list.FindIndex(avatar => string.IsNullOrEmpty(avatar.blueprintId) &&
                                                   string.Equals(avatar.name, current.name, StringComparison.Ordinal));
            if (sameName >= 0)
            {
                list[sameName] = current;
            }
            else
            {
                list.Add(current);
            }
        }
    }
}
