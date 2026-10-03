namespace ServiceLib.Common;

/// <summary>
/// Country helpers: flag emoji for a country code, and digging the country out of
/// a node remark or out of the IP info produced by a speed test.
/// </summary>
public static class CountryExtension
{
    /// <summary>
    /// ISO-3166-1 alpha-2 codes (plus "EU"), so that an ordinary word in a node
    /// name is not mistaken for a country code.
    /// </summary>
    private static readonly HashSet<string> CountryCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AD", "AE", "AF", "AG", "AI", "AL", "AM", "AO", "AQ", "AR", "AS", "AT", "AU", "AW", "AX", "AZ",
        "BA", "BB", "BD", "BE", "BF", "BG", "BH", "BI", "BJ", "BL", "BM", "BN", "BO", "BQ", "BR", "BS",
        "BT", "BV", "BW", "BY", "BZ",
        "CA", "CC", "CD", "CF", "CG", "CH", "CI", "CK", "CL", "CM", "CN", "CO", "CR", "CU", "CV", "CW",
        "CX", "CY", "CZ",
        "DE", "DJ", "DK", "DM", "DO", "DZ",
        "EC", "EE", "EG", "EH", "ER", "ES", "ET", "EU",
        "FI", "FJ", "FK", "FM", "FO", "FR",
        "GA", "GB", "GD", "GE", "GF", "GG", "GH", "GI", "GL", "GM", "GN", "GP", "GQ", "GR", "GS", "GT",
        "GU", "GW", "GY",
        "HK", "HM", "HN", "HR", "HT", "HU",
        "ID", "IE", "IL", "IM", "IN", "IO", "IQ", "IR", "IS", "IT",
        "JE", "JM", "JO", "JP",
        "KE", "KG", "KH", "KI", "KM", "KN", "KP", "KR", "KW", "KY", "KZ",
        "LA", "LB", "LC", "LI", "LK", "LR", "LS", "LT", "LU", "LV", "LY",
        "MA", "MC", "MD", "ME", "MF", "MG", "MH", "MK", "ML", "MM", "MN", "MO", "MP", "MQ", "MR", "MS",
        "MT", "MU", "MV", "MW", "MX", "MY", "MZ",
        "NA", "NC", "NE", "NF", "NG", "NI", "NL", "NO", "NP", "NR", "NU", "NZ",
        "OM",
        "PA", "PE", "PF", "PG", "PH", "PK", "PL", "PM", "PN", "PR", "PS", "PT", "PW", "PY",
        "QA",
        "RE", "RO", "RS", "RU", "RW",
        "SA", "SB", "SC", "SD", "SE", "SG", "SH", "SI", "SJ", "SK", "SL", "SM", "SN", "SO", "SR", "SS",
        "ST", "SV", "SX", "SY", "SZ",
        "TC", "TD", "TF", "TG", "TH", "TJ", "TK", "TL", "TM", "TN", "TO", "TR", "TT", "TW", "TZ",
        "UA", "UG", "UM", "US", "UY", "UZ",
        "VA", "VC", "VE", "VG", "VI", "VN", "VU",
        "WF", "WS",
        "YE", "YT",
        "ZA", "ZM", "ZW",
    };

    /// <summary>
    /// What people write in node names instead of the ISO code (UK, USA, RUS, ...).
    /// </summary>
    private static readonly Dictionary<string, string> CountryAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        { "UK", "GB" },
        { "ENG", "GB" },
        { "GBR", "GB" },
        { "USA", "US" },
        { "RUS", "RU" },
        { "BLR", "BY" },
        { "UKR", "UA" },
        { "DEU", "DE" },
        { "NLD", "NL" },
        { "POL", "PL" },
        { "FRA", "FR" },
        { "CHE", "CH" },
        { "SWE", "SE" },
        { "NOR", "NO" },
        { "FIN", "FI" },
        { "DNK", "DK" },
        { "ESP", "ES" },
        { "ITA", "IT" },
        { "AUT", "AT" },
        { "CZE", "CZ" },
        { "HUN", "HU" },
        { "ROU", "RO" },
        { "BGR", "BG" },
        { "SRB", "RS" },
        { "HRV", "HR" },
        { "GRC", "GR" },
        { "TUR", "TR" },
        { "KAZ", "KZ" },
        { "AZE", "AZ" },
        { "ARM", "AM" },
        { "GEO", "GE" },
        { "MDA", "MD" },
        { "LTU", "LT" },
        { "LVA", "LV" },
        { "EST", "EE" },
        { "AUS", "AU" },
        { "CAN", "CA" },
        { "BRA", "BR" },
        { "IND", "IN" },
        { "JPN", "JP" },
        { "CHN", "CN" },
        { "KOR", "KR" },
        { "SGP", "SG" },
        { "HKG", "HK" },
        { "MYS", "MY" },
        { "THA", "TH" },
        { "IDN", "ID" },
        { "VNM", "VN" },
        { "PHL", "PH" },
        { "MEX", "MX" },
        { "IRN", "IR" },
        { "ARE", "AE" },
        { "SAU", "SA" },
        { "ISR", "IL" },
        { "EGY", "EG" },
        { "ZAF", "ZA" },
        { "NZL", "NZ" },
    };

    /// <summary>
    /// Flag emoji for a plain two letter country code: "RU" -> flag of Russia.
    /// Anything else gives an empty string.
    /// </summary>
    public static string ToFlag(this string? countryCode)
    {
        if (countryCode.IsNullOrEmpty())
        {
            return string.Empty;
        }

        var code = countryCode.Trim().ToUpperInvariant();
        if (code.Length != 2 || code.Any(c => c is < 'A' or > 'Z'))
        {
            return string.Empty;
        }

        // regional indicator symbols: A -> U+1F1E6, B -> U+1F1E7, ...
        return string.Concat(code.Select(c => char.ConvertFromUtf32(0x1F1E6 + (c - 'A'))));
    }

    /// <summary>
    /// Flag emoji for a country code, or null when the code is not a plain two letter one.
    /// </summary>
    public static string? CountryToEmoji(this string? countryCode)
    {
        var flag = countryCode.ToFlag();
        return flag.IsNullOrEmpty() ? null : flag;
    }

    /// <summary>
    /// Country code of a node: the tested IP info wins (that is the real exit
    /// country), the remark is only a fallback.
    /// </summary>
    public static string? ResolveCountryCode(string? ipInfo, string? remarks)
    {
        // "(US) 1.2.3.4" or "<flag>(US) 1.2.3.4" - what a completed IP test stores
        if (ipInfo.IsNotEmpty())
        {
            var match = Regex.Match(ipInfo, @"\(([A-Za-z]{2})\)");
            if (match.Success)
            {
                var code = NormalizeCountryCode(match.Groups[1].Value);
                if (code != null)
                {
                    return code;
                }
            }
        }

        if (remarks.IsNullOrEmpty())
        {
            return null;
        }

        // leading flag emoji: "<flag> UK"
        var flag = CountryFromLeadingFlag(remarks);
        if (flag != null)
        {
            return flag;
        }

        // leading token: "RU - node", "US US", "<sep>DE node"
        var token = Regex.Match(remarks, @"^\s*[^\p{L}\p{N}]*([A-Za-z]{2,3})(?![\p{L}\p{N}])");
        if (token.Success)
        {
            return NormalizeCountryCode(token.Groups[1].Value);
        }

        return null;
    }

    /// <summary>
    /// Flag emoji to show in the "Location" column.
    /// </summary>
    public static string ResolveLocationFlag(string? ipInfo, string? remarks)
    {
        return ResolveCountryCode(ipInfo, remarks).ToFlag();
    }

    private static string? NormalizeCountryCode(string? code)
    {
        if (code.IsNullOrEmpty())
        {
            return null;
        }

        var normalized = code.Trim().ToUpperInvariant();
        if (CountryAliases.TryGetValue(normalized, out var alias))
        {
            normalized = alias;
        }

        return CountryCodes.Contains(normalized) ? normalized : null;
    }

    private static string? CountryFromLeadingFlag(string text)
    {
        var s = text.TrimStart();
        if (s.Length < 4 || !char.IsHighSurrogate(s[0]) || !char.IsLowSurrogate(s[1]))
        {
            return null;
        }

        var first = char.ConvertToUtf32(s, 0);
        if (first is < 0x1F1E6 or > 0x1F1FF)
        {
            return null;
        }

        if (!char.IsHighSurrogate(s[2]) || !char.IsLowSurrogate(s[3]))
        {
            return null;
        }

        var second = char.ConvertToUtf32(s, 2);
        if (second is < 0x1F1E6 or > 0x1F1FF)
        {
            return null;
        }

        return $"{(char)('A' + (first - 0x1F1E6))}{(char)('A' + (second - 0x1F1E6))}";
    }
}
