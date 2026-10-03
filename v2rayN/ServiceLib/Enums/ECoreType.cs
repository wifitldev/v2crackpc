namespace ServiceLib.Enums;

public static class ECoreTypeExtensions
{
    /// <summary>
    /// Name shown to the user. The app itself is "v2crackN", never "v2rayN";
    /// everything else keeps the core's own name (Xray, sing_box, ...).
    /// </summary>
    public static string ToDisplayName(this ECoreType type)
    {
        return type == ECoreType.v2rayN ? Global.AppName : type.ToString();
    }
}

public enum ECoreType
{
    v2fly = 1,
    Xray = 2,
    v2fly_v5 = 4,
    mihomo = 13,
    hysteria = 21,
    naiveproxy = 22,
    tuic = 23,
    sing_box = 24,
    juicity = 25,
    hysteria2 = 26,
    brook = 27,
    overtls = 28,
    shadowquic = 29,
    mieru = 30,
    v2rayN = 99
}
