using ZombieCafe.Data;

namespace ZombieCafe.Game;

/// <summary>
/// A batch of one recipe (original 0x1c-byte struct, FUN_0016c9c4): the food, servings left and the
/// modifier (0 none, 1 Spicy, 2 Very Spicy, 3 Fancy, 4 Very Fancy, 5 Bulk, 6 Fresh, 7 Frozen, 8 Quick,
/// 9 Very Quick). Orders for friends also carry the friend's name; the port has no friends network yet.
/// </summary>
public sealed class Dish
{
    public int Food { get; }
    public int Servings { get; set; }
    public byte Modifier { get; }
    /// <summary>+0x0c: who ordered it (empty for the player's own cooking).</summary>
    public string OrderedBy { get; set; } = "";

    public Dish(int food, int servings, byte modifier)
    {
        Food = food;
        Servings = servings;
        Modifier = modifier;
    }

    /// <summary>FUN_0016c9c4: a fresh batch with the recipe's servings (doubled for Bulk).</summary>
    public static Dish Cook(GameData data, int food, byte modifier) =>
        new(food, data.Foods[food].ServingsWith(modifier), modifier);

    public FoodDef Def(GameData data) => data.Foods[Food];

    /// <summary>FUN_0016d7fc: "Spicy Mystery Meat" etc.</summary>
    public string DisplayName(GameData data)
    {
        var name = data.Foods[Food].Name;
        return Modifier == 0 ? name : data.Strings[StringTable.ModifierNames + Modifier] + " " + name;
    }
}
