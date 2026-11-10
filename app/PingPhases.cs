namespace UiProto;

/// <summary>
/// Модель фаз пинга: набор фаз по выбранному способу, порядок приоритета показа
/// и выбор лучшего замера по результату всех фаз.
/// </summary>
public static class PingPhases
{
    public enum Phase { Tcping, Realping, UdpTest, HttpHead, Icmp }

    /// <summary>Фазы, которые нужно запустить для выбранного способа пинга.</summary>
    public static Phase[] PhasesFor(string method) => method switch
    {
        "get" => [Phase.Realping],
        "head" => [Phase.HttpHead],
        "tcp" => [Phase.Tcping],
        "icmp" => [Phase.Icmp],
        _ => [Phase.Tcping, Phase.Realping, Phase.UdpTest],
    };

    /// <summary>
    /// Приоритет показа: первое положительное значение в этом порядке и будет
    /// итоговым результатом.
    /// </summary>
    public static Phase[] DisplayFor(string method) => method switch
    {
        "get" => [Phase.Realping],
        "head" => [Phase.HttpHead],
        "tcp" => [Phase.Tcping],
        "icmp" => [Phase.Icmp],
        _ => [Phase.Realping, Phase.Tcping, Phase.UdpTest],
    };

    /// <summary>
    /// Первое положительное значение в порядке приоритета показа; -1, если ни
    /// одной фазы с положительным результатом нет.
    /// </summary>
    public static int BestResult(IReadOnlyDictionary<Phase, int> values, IReadOnlyList<Phase> displayPriority)
    {
        foreach (var ph in displayPriority)
        {
            if (values.TryGetValue(ph, out var v) && v > 0)
                return v;
        }
        return -1;
    }
}
