using ZombieCafe.Data;

namespace ZombieCafe.Game.Cafe;

/// <summary>
/// A pet's home (Habitat, a Furniture of 0xc0 bytes, FUN_00195aac). It stands in the yard, on the first row outside
/// the room's two open sides. Its picture shows it empty (sub-image 0), with the pet asleep inside (1) or blinking (2);
/// orientation 1 mirrors it (Habitat::updateOrientation). The pet comes out of the cell in front of it and goes back
/// in there for a nap - which never lasts long: a few seconds after it has gone in it is out again.
/// </summary>
public sealed class Habitat : Furniture
{
    /// <summary>+0xa8: the pet that lives here.</summary>
    public CharacterInstance? Pet { get; private set; }
    /// <summary>+0xac: its place among the player's pets (game +0x188); 255 = none.</summary>
    public int PetIndex { get; set; } = 255;
    /// <summary>+0xb8: the picture - 0 empty, 1 the pet inside, 2 the pet inside blinking.</summary>
    public int ImageState { get; private set; }
    /// <summary>+0xbd: the pet is inside.</summary>
    public bool PetInside { get; private set; }

    int timer;       // +0xb0
    bool blink;      // +0xb4
    bool comeOut;    // +0xbc

    public Habitat(GameData data, FurnitureDef def) : base(data, def)
    {
    }

    void Show(int picture)
    {
        ImageState = picture;
        SetOrientation(Orientation);
    }

    /// <summary>FUN_00195ce4: the pet moves in (its number among the player's pets is kept for the save).</summary>
    public void Bind(CharacterInstance pet, int index)
    {
        Pet = pet;
        PetIndex = index;
        pet.Home = this;
        Show(0);
    }

    /// <summary>FUN_000cb270 / FUN_000c3118: the pet has gone (with the habitat into storage, or sold with it).</summary>
    public void Unbind()
    {
        if (Pet?.Home == this)
            Pet.Home = null;
        Pet = null;
        PetIndex = 255;
        PetInside = false;
        comeOut = false;
        Show(0);
    }

    /// <summary>DisappearTask::vf05: the pet is home - it shows inside (+0xbd, picture 1).</summary>
    public void TakeIn()
    {
        PetInside = true;
        Show(1);
    }

    /// <summary>FUN_000c2c50: a pet just bought starts inside, and comes straight out (+0xbc).</summary>
    public void TakeInNew()
    {
        TakeIn();
        comeOut = true;
    }

    /// <summary>
    /// FUN_001959fc: while the pet is inside, the picture goes from sleeping (one to three seconds) to a quarter
    /// second's blink, and three blinks in four end with the pet coming out.
    /// </summary>
    public void Tick(int dtMs, Random rng)
    {
        if (!PetInside)
            return;
        timer -= dtMs;
        if (timer >= 0)
            return;
        int picture;
        if (!blink)
        {
            picture = 1;
            timer = rng.Next(3) * 1000 + 1000;
        }
        else
        {
            picture = 2;
            timer = 250;
            if (rng.Next(4) != 0)
            {
                picture = 0;
                comeOut = true;
            }
        }
        Show(picture);
        blink = !blink;
    }

    /// <summary>FUN_00195e20: time for the pet to come out (answered once).</summary>
    public bool TakeComeOut()
    {
        bool c = comeOut;
        comeOut = false;
        return c;
    }

    /// <summary>FUN_00195e30: the pet is out - the habitat empty again.</summary>
    public void LetOut()
    {
        PetInside = false;
        Show(0);
    }

    /// <summary>
    /// FUN_001642f8 / FUN_001642a8: the cell in front, where the pet comes out and goes in - the +v neighbour for
    /// orientation 0, the -u one for 1 (the original has none for the others and uses cell 0; the port its own cell).
    /// </summary>
    public int DoorCell(Cafe cafe) => Orientation switch
    {
        0 => cafe.Neighbour(Cell, 0),
        1 => cafe.Neighbour(Cell, 1),
        _ => Cell,
    };

    /// <summary>FUN_00195e30: coming out, the pet faces away from the habitat (the orientation as a facing).</summary>
    public Facing OutFacing => (Facing)(Orientation & 3);

    /// <summary>FUN_000bd26c: going in, it faces the habitat (0x1000302 >> orientation * 8).</summary>
    public Facing InFacing => Orientation switch { 0 => Facing.NW, 1 => Facing.NE, 2 => Facing.SE, _ => Facing.SW };
}
