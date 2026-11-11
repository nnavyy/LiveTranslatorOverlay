namespace LiveTranslatorOverlay.Core;

public static class CommunityPodium
{
    public const string StageFill = "#2C2638";
    public const string StageOutline = "#514460";
    public const string StageInk = "#F3E9FF";
    public const string AvatarFill = "#E8DDFB";
    public const string AvatarInk = "#342354";
    public const string AvatarFace = "M11,23 Q11,17 17,17 L27,17 Q33,17 33,23 L33,28 Q33,35 26,35 L18,35 Q11,35 11,28 Z";
    public const string AvatarDetails = "M8,25 L8,20 C8,12 14,7 22,7 C30,7 36,12 36,20 L36,25 M8,22 L8,29 M36,22 L36,29 M17,24 L17,25 M27,24 L27,25 M19,29 Q22,32 25,29";

    public static int Column(int index, int count) => count == 3 ? index switch { 0 => 1, 1 => 0, _ => 2 } : index;

    public static (int Height, string Fill, string Ink) Step(int rank) => rank switch
    {
        1 => (87, "#4F391F", "#FFE1A3"),
        2 => (73, "#453557", "#E9DAFF"),
        _ => (59, "#4A3039", "#FFCEC1")
    };
}
