using ZombieCafe.Data;

namespace ZombieCafe.Game;

/// <summary>
/// The Two Point build's changes to the store, made once the game data is read: the Toxin Generator sold for cash
/// (the original sold it for real money) and expansions past the original's largest (17 x 16). The new expansions
/// copy the largest one's card and are added at the end of the furniture list, so their ids stay the same from run
/// to run. The map is made taller for them (rows added at the bottom keep every saved cell index as it was).
/// </summary>
public static class TwoPointStore
{
    /// <summary>The bigger rooms: (width, depth, cash price, toxin price).</summary>
    static readonly (byte W, byte D, int Cash, int Toxin)[] Expansions =
    [
        (18, 17, 300_000, 90),
        (19, 18, 350_000, 100),
        (20, 19, 400_000, 110),
        (22, 21, 500_000, 130),
        (24, 23, 650_000, 150),
        (26, 25, 800_000, 175),
    ];

    /// <summary>
    /// Map rows for the biggest room: its yard, the rest spots and the pets' wandering reach rows past it
    /// (row = v - u grows with both the width and the depth).
    /// </summary>
    public const int MapRows = 100;

    public static void Apply(GameData data, Settings settings)
    {
        foreach (var d in data.Furniture)
            if (d.Category == FurnitureCategory.ToxinGenerator && d.ForSale && d.Currency == 2)
            {
                d.Price = settings.ToxinGeneratorPrice;
                d.Currency = 0;
            }

        // the largest expansion the store has, as the pattern for the new ones (cash and toxin twins, as the original)
        var expansions = data.Furniture.Where(d => d.Category == FurnitureCategory.Expansion && d.ForSale).ToList();
        if (expansions.Count == 0)
            return;
        var largest = expansions.MaxBy(d => d.Width * 100 + d.Depth)!;
        foreach (var (w, dep, cash, toxin) in Expansions)
        {
            if (expansions.Any(d => d.Width >= w && d.Depth >= dep))
                continue;
            foreach (var (price, currency) in new[] { (cash, (byte)0), (toxin, (byte)1) })
            {
                var e = largest.Copy();
                e.Id = data.Furniture.Count;
                e.Width = w;
                e.Depth = dep;
                e.Price = price;
                e.Currency = currency;
                e.Name = $"Expand to {w}x{dep}";
                e.Description = $"Makes your cafe {w} by {dep} tiles.";
                data.AddFurniture(e);
            }
        }
        Cafe.Cafe.MinMapHeight = MapRows;
    }
}
