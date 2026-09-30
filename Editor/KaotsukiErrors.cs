using System;
using System.Collections.Generic;
using nadena.dev.ndmf;
using nadena.dev.ndmf.localization;

namespace Poyo.Kaotsuki.Editor
{
    internal static class KaotsukiErrors
    {
        internal const string Multiple = "kaotsuki.error.multiple";
        internal const string NoSlots = "kaotsuki.warn.no_slots";
        internal const string Truncated = "kaotsuki.warn.truncated";
        internal const string Skipped = "kaotsuki.warn.skipped";
        internal const string MapWrite = "kaotsuki.warn.map_write";
        internal const string MapShared = "kaotsuki.warn.map_shared";
        internal const string MapSharedDetail =
            "マップ「{0}」を使っていた他のアバターとブレンドシェイプの構成が違うため、他のアバターの登録を外しました。" +
            "外れたアバターは、ビルドし直すまで送り手の自動切換えの対象になりません。";

        private static readonly Dictionary<string, string> Messages = new Dictionary<string, string>
        {
            { Multiple, "Kaotsuki が複数あります" },
            { Multiple + ":description", "アバター内の Kaotsuki Receiver を 1 つにしてください。" },
            { NoSlots, "操作できるブレンドシェイプがありません" },
            { NoSlots + ":description", "メッシュを登録し、ブレンドシェイプを 1 つ以上 ON にしてください。" },
            { Truncated, "登録数が上限を超えています" },
            { Truncated + ":description", "3825 件を超えた {0} 件は操作できません。" },
            { Skipped, "登録できないメッシュがあります" },
            { Skipped + ":description", "{0}" },
            { MapWrite, "マップを書き出せませんでした" },
            { MapWrite + ":description", "{0}" },
            { MapShared, "共通マップの構成が変わりました" },
            { MapShared + ":description", MapSharedDetail },
        };

        internal static readonly Localizer Localizer = new Localizer(
            "ja-JP",
            () => new List<(string, Func<string, string>)> { ("ja-JP", Lookup) });

        internal static void Report(ErrorSeverity severity, string key, params object[] args)
        {
            ErrorReport.ReportError(Localizer, severity, key, args);
        }

        private static string Lookup(string key)
        {
            if (key.EndsWith(":hint", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            return Messages.TryGetValue(key, out var message) ? message : null;
        }
    }
}
