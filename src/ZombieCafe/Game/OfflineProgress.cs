using ZombieCafe.Data;
using ZombieCafe.Game.Cafe;
using ZombieCafe.Game.Cafe.Tasks;

namespace ZombieCafe.Game;

/// <summary>
/// What happened while the game was closed (GameStateCafe::load: FUN_000b55d0 for the customers,
/// FUN_000b5c10 for each worker, FUN_000b6748 for the stoves). Customers are estimated with a queueing
/// model instead of being simulated one by one: a quarter of the time away counts, arrivals follow the
/// rating, the seats form a finite queue, each serving zombie handles one customer per 30 seconds, and
/// servings come off the counters.
/// </summary>
public static class OfflineProgress
{
    public sealed record Summary(long ElapsedMs, int Served, int Hungry, int Income, int Tips);

    /// <summary>Simulates <paramref name="elapsedMs"/> of absence. <paramref name="cookingAt"/> maps a cook to its stove.</summary>
    public static Summary Apply(CafeController game, GameData data, long elapsedMs, IReadOnlyDictionary<CharacterInstance, Stove> cookingAt)
    {
        if (elapsedMs <= 0)
            return new Summary(0, 0, 0, 0, 0);
        var (served, hungry, income, tips, servingTime) = Customers(game, data, elapsedMs);
        // FUN_000dc51c: the customers served count towards a quest
        if (game.Hints.IsShown(0x53))
            game.Player.Objectives.Served(served);
        foreach (var w in new[] { game.ChefActor }.Concat(game.Staff))
            Worker(game, data, w, elapsedMs, servingTime, cookingAt.GetValueOrDefault(w));
        // FUN_000b6748: every stove's clock runs for the whole time (unattended dishes burn)
        foreach (var o in game.Cafe.Objects.ToArray())
            if (o is Stove s)
                s.Update(game.Cafe, (int)Math.Min(elapsedMs, int.MaxValue));
        return new Summary(elapsedMs, served, hungry, income, tips);
    }

    /// <summary>FUN_000dc410: chance an arriving customer finds no seat (M/M/1/K-style blocking).</summary>
    static double Blocking(int seats, double lambda, double mu)
    {
        if (lambda >= mu)
        {
            double x = 1.0 / (seats + 1);
            return x * 0.5 + (1 - x) * (1 - Math.Pow(mu / lambda, 0.12));
        }
        double rho = lambda / mu;
        return Math.Pow(rho, seats) * (1 - rho) / (1 - Math.Pow(rho, seats + 1));
    }

    /// <summary>Arrival rate (customers per ms) at the current rating.</summary>
    static double ArrivalRate(GameData data, float rating)
    {
        var k = data.K;
        double interval = k.SpawnDelayMin - (k.SpawnDelayMin - k.SpawnDelayMax) * rating;
        double enter = (k.EnterChanceMin + (k.EnterChanceMax - k.EnterChanceMin) * rating) / 256.0;
        return enter / interval;
    }


    /// <summary>One customer per serving zombie per this many ms (DAT_0042d1c0).</summary>
    static double ServeCapacityMs(GameData data) => data.K.Float(0x42d1c0);

    /// <summary>FUN_000dc51c: <paramref name="time"/> ms of customers, with or without food on the counters.</summary>
    static (int Served, int Hungry, int Income, int Tips) Serve(CafeController game, GameData data, double time,
        int seats, int zombies, int avgTip, bool withFood)
    {
        var k = data.K;
        double capacity = ServeCapacityMs(data);
        var p = game.Player;
        double lambda = ArrivalRate(data, p.EffectiveRating);
        int served = 0, hungry, income = 0, tips = 0;
        if (withFood)
        {
            double over = Math.Clamp((lambda - zombies / capacity) / lambda, 0, 1);
            double ok = 1 - over;
            double serviceMs = ok * (capacity + k.EatTime) + over * k.CustomerPatience;
            double blocked = Blocking(seats, lambda, 1.0 / serviceMs);
            double arrivals = time * lambda;
            served = (int)(arrivals * (1 - blocked) * ok);
            income = TakeServings(game, served);
            hungry = (int)(arrivals * blocked) + (int)(arrivals * (1 - blocked) * over);
            if (income > 0)
            {
                tips = (int)(income * 0.2f * avgTip / 10f) * p.Chef.I44;
                p.Money += income + tips;
            }
            p.Rating += served * k.RatingGain - hungry * k.RatingLoss;
        }
        else
        {
            double blocked = Blocking(seats, lambda, 1.0 / k.CustomerPatience);
            double arrivals = time * lambda;
            hungry = (int)(arrivals * blocked) + (int)(arrivals * (1 - blocked));
            p.Rating -= hungry * k.RatingLoss;
        }
        p.Rating = Math.Clamp(p.Rating, 0f, 1f);
        return (served, hungry, income, tips);
    }

    /// <summary>FUN_00162be8 / FUN_001c218c: servings taken off the counters; returns what they sell for.</summary>
    static int TakeServings(CafeController game, int n)
    {
        int income = 0;
        foreach (var f in game.Cafe.FindFurniture(FurnitureCategory.Counter, -1))
        {
            if (n <= 0)
                break;
            if (f is not ServingCounter c)
                continue;
            while (n > 0 && c.TotalServings > 0 && c.TakeServing(game.Cafe, true) is { } one)
            {
                income += game.Cafe.Data.Foods[one.Food].PriceWith(one.Modifier);
                n--;
            }
        }
        return income;
    }

    /// <summary>FUN_000b55d0.</summary>
    static (int Served, int Hungry, int Income, int Tips, long ServingTime) Customers(CafeController game, GameData data, long elapsedMs)
    {
        var cafe = game.Cafe;
        var p = game.Player;
        if (!cafe.IsOpen)
            return (0, 0, 0, 0, 0);
        float before = p.Rating;
        // decorations raise the rating by their hourly bonus (FurnitureDef +0x2c)
        foreach (var o in cafe.Objects)
            if (o is Furniture f)
                p.Rating += (float)(elapsedMs * f.Def.F2C / 3.6e6);
        for (int i = 0; i < cafe.Cells.Length; i++)
            for (int side = 0; side < 2; side++)
                if (cafe.Cells[i].Slots[side] is CafeWall { Decor: { } d })
                    p.Rating += (float)(elapsedMs * d.Def.F2C / 3.6e6);
        p.Rating = Math.Min(1f, p.Rating);
        // cash items fill up by their hourly rate, in whole units, to what they hold
        foreach (var o in cafe.Objects)
            if (o is Furniture f && f.EarnsOverTime)
                f.Stored = (int)Math.Min(f.Stored + (long)(f.PerHour * elapsedMs / 3_600_000), f.Def.S28);

        int seats = cafe.FindFurniture(FurnitureCategory.Chair, -1).Count(ch => cafe.TableInFrontOf(ch) != null);
        int zombies = 0, tipSum = 0;
        foreach (var w in new[] { game.ChefActor }.Concat(game.Staff))
            if (w.Character.Job == Job.Serving)
            {
                zombies++;
                tipSum += w.Character.TipRating;
            }
        int avgTip = zombies != 0 ? tipSum / zombies : 0;
        int servings = cafe.TotalServings();

        var k = data.K;
        double time = elapsedMs * k.Float(0x42d140); // only a quarter of the time away counts
        double lambda = ArrivalRate(data, p.EffectiveRating);
        double capacity = ServeCapacityMs(data);
        double over = Math.Clamp((lambda - zombies / capacity) / lambda, 0, 1);
        double ok = 1 - over;
        double serviceMs = ok * (capacity + k.EatTime) + over * k.CustomerPatience;
        double blocked = Blocking(seats, lambda, 1.0 / serviceMs);
        int customers = (int)(time * lambda * (1 - blocked) * ok);

        int served = 0, hungry = 0, income = 0, tips = 0;
        long servingTime = 0;
        if (customers > 0)
        {
            if (customers <= servings)
            {
                servingTime = elapsedMs;
                (served, hungry, income, tips) = Serve(game, data, time, seats, zombies, avgTip, true);
            }
            else
            {
                double fed = time * servings / customers;
                servingTime = (long)(elapsedMs * (double)servings / customers);
                var a = Serve(game, data, fed, seats, zombies, avgTip, true);
                var b = Serve(game, data, time - fed, seats, zombies, avgTip, false);
                (served, hungry, income, tips) = (a.Served + b.Served, a.Hungry + b.Hungry, a.Income + b.Income, a.Tips + b.Tips);
            }
        }
        // serving zombies share the experience
        if (zombies != 0)
        {
            int each = Math.Min(customers, servings) / zombies;
            foreach (var w in new[] { game.ChefActor }.Concat(game.Staff))
                if (w.Character.Job == Job.Serving)
                {
                    w.Character.Xp += k.ServeXp * each;
                    w.Character.Served += each;
                }
        }
        // the rating cannot swing by more than 0.4 in one absence
        p.Rating = Math.Clamp(p.Rating, Math.Max(0f, before - k.Float(0x42d1c4)), Math.Min(1f, before + k.Float(0x42d1c4)));
        return (served, hungry, income, tips, servingTime);
    }

    /// <summary>FUN_000b5c10: a worker's absence — cooking goes on, serving tires, resting refreshes.</summary>
    static void Worker(CafeController game, GameData data, CharacterInstance w, long elapsedMs, long servingTime, Stove? stove)
    {
        var c = w.Character;
        var k = data.K;
        float rate = k.Float(0x42d14c); // 0.04
        long left = elapsedMs;
        if (c.Role == Role.Staff && w.IsKnockedOut && c.Job != Job.Other)
        {
            c.Job = Job.Other;
            c.ReanimateMs = k.Int(0x42cf10); // 8 hours
        }
        while (left > 0)
        {
            switch (c.Job)
            {
                case Job.Serving:
                {
                    int zombies = Math.Max(1, new[] { game.ChefActor }.Concat(game.Staff).Count(x => x.Character.Job == Job.Serving));
                    c.Drain(data, (int)Math.Min(int.MaxValue, servingTime / zombies * rate));
                    left = 0;
                    break;
                }
                case Job.Cooking when stove != null && stove.Dish != null && stove.Busy && !stove.IsDone:
                {
                    int remaining = stove.CookSeconds * 1000 - stove.Progress;
                    double speed = game.Player.Chef.F40 * stove.Def.F34;
                    if (left * speed < remaining)
                    {
                        stove.Work(game.Cafe, w, (int)(left * speed));
                        c.Drain(data, (int)Math.Min(int.MaxValue, left * rate));
                        w.AddTask(new CookTask(w, stove, game.Cafe));
                        return;
                    }
                    stove.Work(game.Cafe, w, remaining);
                    c.Drain(data, (int)(remaining * rate));
                    left -= (long)(remaining / Math.Max(speed, 1e-6));
                    c.Job = Job.None;
                    break;
                }
                case Job.Resting:
                {
                    // FUN_000b5c10 case 3: rest restores energy at the chef's zombie-regen rate plus one per
                    // toxin barrel, a hundredth more for each Rage Bull (GameStateCafe +0x247)
                    double regen = left * rate * (game.Cafe.ToxinBarrels + game.Player.Chef.F48 + game.RestBonus)
                        * (game.PetCount(PetDef.RageBull) * 0.01 + 1.0);
                    c.Energy = Math.Min(c.MaxEnergy(data), c.Energy + (float)(regen * k.EnergyDrainPerMs));
                    left = 0;
                    break;
                }
                case Job.Other when c.ReanimateMs > 0:
                    c.ReanimateMs = (int)Math.Max(0, c.ReanimateMs - left);
                    if (c.ReanimateMs == 0)
                    {
                        c.Job = Job.None;
                        c.Energy = Math.Clamp(k.Int(0x42d3d8), 0, c.MaxEnergy(data));
                    }
                    left = 0;
                    break;
                case Job.Attacking:
                    c.Job = Job.None;
                    break;
                default:
                    left = 0;
                    break;
            }
        }
    }
}
