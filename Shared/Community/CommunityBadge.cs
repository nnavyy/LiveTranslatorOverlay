namespace LiveTranslatorOverlay.Core;

public sealed record CommunityBadge(string Name, string Color, string Body, string Detail)
{
    private static readonly CommunityBadge Supporter = new(
        "Supporter", "#FFB995",
        "M6,2 H22 L21,9 L18,12 C22,16 21,22 17,25 C12,28 6,24 6,19 C6,16 7,14 10,12 L7,9 Z",
        "M14,23 L9.7,19 C7.2,16.3 11.6,13.8 14,17 C16.4,13.8 20.8,16.3 18.3,19 Z");

    private static readonly CommunityBadge Insider = new(
        "Insider", "#BDA6FF",
        "M14,1 L25,7 L27,18 L21,24 L14,27 L7,24 L1,18 L3,7 Z",
        "M14,5 L16.8,11.2 L23,14 L16.8,16.8 L14,23 L11.2,16.8 L5,14 L11.2,11.2 Z");

    public static CommunityBadge? ForOffer(string offerId) => offerId switch
    {
        "supporter" => Supporter,
        "insider" => Insider,
        _ => null
    };

    public static IEnumerable<CommunityBadge> For(CommunityRank entry)
    {
        if (entry.Medals?.Contains(Supporter.Name) == true) yield return Supporter;
        if (entry.Medals?.Contains(Insider.Name) == true) yield return Insider;
    }
}
