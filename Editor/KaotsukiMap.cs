using System;
using System.Collections.Generic;

namespace Poyo.Kaotsuki.Editor
{
    [Serializable]
    internal sealed class KaotsukiMap
    {
        public string format = "kaotsuki-map";
        public int version = 1;
        public string mapName;
        public string avatarName;
        public string blueprintId;
        public List<KaotsukiMapAvatar> avatars = new List<KaotsukiMapAvatar>();
        public string generatedAt;
        public KaotsukiMapParameters parameters = new KaotsukiMapParameters();
        public int valueMax = KaotsukiInfo.ValueMax;
        public List<KaotsukiMapSlot> slots = new List<KaotsukiMapSlot>();
    }

    [Serializable]
    internal sealed class KaotsukiMapAvatar
    {
        public string name;
        public string blueprintId;
    }

    [Serializable]
    internal sealed class KaotsukiMapParameters
    {
        public string enabled = KaotsukiInfo.ParamEnabled;
        public List<KaotsukiMapChannel> channels = new List<KaotsukiMapChannel>();
    }

    [Serializable]
    internal sealed class KaotsukiMapChannel
    {
        public string index;
        public string value;
    }

    [Serializable]
    internal sealed class KaotsukiMapSlot
    {
        public int channel;
        public int index;
        public string mesh;
        public string blendShape;
        public float defaultWeight;
        public string group;
    }
}
