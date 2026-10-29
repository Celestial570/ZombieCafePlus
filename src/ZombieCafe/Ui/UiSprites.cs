namespace ZombieCafe.Ui;

/// <summary>Indices into ipad_ingameUiImages (game +0x224), named from what each sprite shows.</summary>
public static class UiSprites
{
    public const int ArrowDown = 0, ArrowLeft = 1, ArrowRight = 2, ArrowUp = 3;
    public const int Awning = 4, AwningPurple = 5, CloseX = 6, ZcLogo = 7, Fridge = 8, Trophy = 9;
    public const int GreenBarWide = 10, GreenBar = 11, Map = 12, Flag = 13, Energize = 14, Revive = 15;
    public const int Cookbook = 16, RedBarWide = 17, RedBar = 18, ShopBag = 19;
    public const int ToxinPlus = 24, ZombieFace = 25, BottomBar = 26, BigArrow = 27, Cash = 28, CashSmall = 29;
    public const int Alert = 30, Ring = 31, StarSmall = 32, ToxinBarrel = 33, Vial = 34, VialMedium = 35, VialSmall = 36;
    public const int Paper = 37, BarRed = 39, Pin = 40, BarLight = 42, Attack = 43, Infect = 44, Info = 45;
    public const int GreenBlob = 46, Censored = 51, Skull = 52, Rest = 53, BarBlue = 55;
    public const int ThoughtCloud = 71, FaceAngry = 72, FaceSleepy = 73, FaceHappy = 74, SpeechBubble = 75;
    public const int PaperLarge = 76, XpLabel = 77, Dollar = 78, BigX = 84, Move = 85;
    public const int Lock = 106, PaperSquare = 107, MoneyPile = 108, Question = 109, Back = 110, Barrel = 111;
    public const int Clipboard = 112, Discard = 113, Finish = 114, Photo = 115, LockLarge = 116;
    public const int Work = 120, Serve = 121, Ok = 122, Salvage = 123, Next = 124, Rotate = 128, Chef = 129;
    public const int CheckOn = 130, CheckOff = 131, ButtonCook = 158, ButtonOrder = 159, Star = 181;
    public const int Storage = 186, Upgrade = 190, Recipes = 191, Sale = 195, ZombieBook = 198;

    /// <summary>Store tabs: (selected, unselected) sprite per shop tab number 1..5.</summary>
    public static (int On, int Off) Tab(int shopTab) => shopTab switch
    {
        1 => (47, 20), // DECOR
        3 => (48, 21), // FURNITURE
        2 => (49, 22), // UTILITY
        4 => (50, 23), // WALLS/FLOORS
        _ => (80, 79), // SPECIAL
    };

    /// <summary>The order the original store shows its tabs in.</summary>
    public static readonly int[] TabOrder = [1, 3, 2, 4, 5];
}
