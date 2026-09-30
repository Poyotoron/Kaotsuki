namespace Poyo.Kaotsuki.Editor
{
    /// <summary>パッケージ共通の定数。</summary>
    internal static class KaotsukiInfo
    {
        internal const string PackageId = "net.maaaaa.kaotsuki";
        internal const string DisplayName = "Kaotsuki";

        internal const string ParamEnabled = "Kaotsuki/Enabled";
        internal const string ParamLipSync = "Kaotsuki/LipSync";
        internal const string ParamIndex = "Kaotsuki/Index";
        internal const string ParamValue = "Kaotsuki/Value";

        internal const string ParamOne = "Kaotsuki/One";
        internal const string ParamSlotPrefix = "Kaotsuki/Slot/";

        internal const int SlotsPerChannel = 255;
        // NOTE: 同期パラメータの上限 256 bit に、Enabled と LipSync（2 bit）と Index + Value（16 bit）の組が収まる最大数。
        internal const int MaxChannels = 15;
        internal const int FixedSyncBits = 2;
        internal const int MaxSlots = SlotsPerChannel * MaxChannels;
        internal const int ValueMax = 255;

        internal const string SetupMenuPath = "GameObject/Kaotsuki/Setup";
        internal const int SetupMenuPriority = 30;
        internal const string OpenSenderMenuPath = "Tools/Kaotsuki/Open Sender";
        internal const string OpenMapFolderMenuPath = "Tools/Kaotsuki/Open Map Folder";

        internal const string SenderExeRelativePath = "Bin~/KaotsukiSender.exe";

        /// <summary>枠の Index パラメータ名（1 → Kaotsuki/Index、2 → Kaotsuki/Index2 …）。</summary>
        // NOTE: 255 件以下のアバターとの互換性を保つため、1 枠目には番号を付けない。
        internal static string IndexParam(int channel) => channel == 1 ? ParamIndex : ParamIndex + channel;

        internal static string ValueParam(int channel) => channel == 1 ? ParamValue : ParamValue + channel;

        /// <summary>スロット番号から内部パラメータ名を作る（例: 7 → Kaotsuki/Slot/0007）。</summary>
        internal static string SlotParam(int number) => ParamSlotPrefix + number.ToString("0000");
    }
}
