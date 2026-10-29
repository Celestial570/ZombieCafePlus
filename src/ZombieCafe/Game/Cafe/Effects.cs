using Raylib_cs;
using ZombieCafe.Gfx;
using ZombieCafe.Ui;

namespace ZombieCafe.Game.Cafe;

/// <summary>Something in the cafe that runs out on its own (the cafe drops it when <see cref="Expired"/>).</summary>
public interface ITimedObject
{
    void Tick(int dtMs);
    bool Expired { get; }
}

/// <summary>
/// What is left of a customer a zombie ate (BonePile, 100 bytes): the skull and bones sprite (in-game UI
/// image 52), standing for ten seconds and fading out over the last three (K 0x42cdc4 / 0x42cdc8).
/// </summary>
public sealed class BonePile : CafeObject, ITimedObject
{
    int life;   // +0x44

    public BonePile(Cafe cafe, float x, float y)
    {
        life = cafe.Data.K.Int(0x42cdc4);
        SetPosition(cafe, x, y);
    }

    public override CafeObjectType Type => CafeObjectType.BonePile;

    public bool Expired => life <= 0;

    public void Tick(int dtMs) => life -= dtMs;

    public override void Draw(Cafe cafe, GameData data)
    {
        int fade = Math.Max(1, data.K.Int(0x42cdc8));
        byte alpha = (byte)(life < fade ? Math.Max(0, life) * 255 / fade : 255);
        var img = data.IngameUi[UiSprites.Skull];
        // FUN_001ba5a0: centred on the spot, standing on it
        img.Draw(X, Y + img.FullHeight / 2f, Anchor.Bottom | Anchor.HCenter, new Color((byte)255, (byte)255, (byte)255, alpha));
    }
}

/// <summary>
/// SpecialEffect (0x58 bytes, FUN_000e1c68): the flash over a robot pet folding into the Amptron or coming out of it
/// (robotPetDisappearSW / robotPetAppearSW on "000-RobotPetTransformParts"), played once where the pet stands.
/// </summary>
public sealed class TransformEffect : CafeObject, ITimedObject
{
    readonly CharacterInstance on;   // +0x54
    readonly FlashAnimation? anim;

    public override CafeObjectType Type => CafeObjectType.None;

    public TransformEffect(Cafe cafe, CharacterInstance on, bool appear)
    {
        this.on = on;
        var parts = cafe.Data.Sheet("000-RobotPetTransformParts", "000-RobotPetTransformParts.bin");
        if (cafe.Data.Anim(appear ? "robotPetAppearSW.anim" : "robotPetDisappearSW.anim") is { } file)
        {
            anim = new FlashAnimation(file, parts) { LoopCount = 1, MultiStep = false };
            anim.Reset();
            anim.Play();
        }
        // drawn just in front of the pet
        SetPosition(cafe, on.X, on.Y + 1);
    }

    public void Tick(int dtMs) => anim?.Update(dtMs);

    public bool Expired => anim == null || anim.IsFinished;

    public override void Draw(Cafe cafe, GameData data) => anim?.Draw((int)on.X, (int)on.Y, 1f, Color.White);
}

/// <summary>
/// The "censored" sign held over a zombie's meal (CensoredSign, a CafeUIElement: in-game UI image 51 at 70%,
/// centred on the body, drawn over everything in the cafe).
/// </summary>
public sealed class CensoredSign
{
    readonly CharacterInstance body;

    /// <summary>+0xc: take it down.</summary>
    public bool Gone { get; set; }

    public CensoredSign(CharacterInstance body) => this.body = body;

    public void Draw(GameData data)
    {
        // centred on the body as it is drawn (CensoredSign::vf02)
        var img = data.IngameUi[UiSprites.Censored];
        var b = body.BodyBounds;
        float cx = b.Width > 0 ? b.X + b.Width / 2 : body.X, cy = b.Height > 0 ? b.Y + b.Height / 2 : body.Y - 40;
        img.Draw(cx, cy, Anchor.Center, Color.White, Flip.None, 0.7f, 0.7f);
    }
}
