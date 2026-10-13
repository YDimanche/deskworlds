using System.Drawing;

namespace Deskworlds;

public enum WorldId { Riverscape, Reefscape, Bettascape, Plasmascape, Koiscape, Bonfirescape }

public sealed class WorldDef
{
    public WorldId Id { get; }
    public string Key { get; }
    public string Title { get; }
    public string FeedTitle { get; }
    public bool CanFeed { get; }
    public Color BackgroundColor { get; }

    private WorldDef(WorldId id, string key, string title, string feedTitle, bool canFeed, int rgb)
    {
        Id = id;
        Key = key;
        Title = title;
        FeedTitle = feedTitle;
        CanFeed = canFeed;
        BackgroundColor = Color.FromArgb(unchecked((int)(0xFF000000u | (uint)rgb)));
    }

    public static readonly WorldDef[] All =
    {
        new(WorldId.Riverscape,  "riverscape",  "河床",     "投喂",     true,  0x080E0C),
        new(WorldId.Reefscape,   "reefscape",   "珊瑚礁",   "投喂",     true,  0x0B1825),
        new(WorldId.Bettascape,  "bettascape",  "斗鱼",     "投喂",     true,  0x000000),
        new(WorldId.Plasmascape, "plasmascape", "等离子球", "投喂",     false, 0x000000),
        new(WorldId.Koiscape,    "koiscape",    "锦鲤池",   "投喂",     true,  0x050A08),
        new(WorldId.Bonfirescape,"bonfirescape","篝火",     "拨弄火焰", true,  0x000000),
    };

    public static WorldDef FromKey(string? key) =>
        Array.Find(All, w => w.Key == key) ?? All[0];
}
