using pk3DS.Core.Structures;

namespace pk3DS.Core.Randomizers;

public class EvolutionRandomizer : IRandomizer
{
    private readonly EvolutionSet[] Evolutions;
    private readonly GameConfig Config;

    public readonly SpeciesRandomizer Randomizer;
    public readonly FormRandomizer FormRandomizer;

    public EvolutionRandomizer(GameConfig config, EvolutionSet[] evolutions, SpeciesRandomizer speciesRand = null)
    {
        Config = config;
        Evolutions = evolutions;
        if (speciesRand != null)
        {
            Randomizer = speciesRand;
        }
        else
        {
            Randomizer = new SpeciesRandomizer(Config);
            Randomizer.Initialize();
        }
        FormRandomizer = new FormRandomizer(config);
    }

    public void Execute()
    {
        for (var i = 0; i < Evolutions.Length; i++)
        {
            var evo = Evolutions[i];
            if (evo != null)
                Randomize(evo, i);
        }
    }

    public void ExecuteTrade()
    {
        for (var i = 0; i < Evolutions.Length; i++)
        {
            var evo = Evolutions[i];
            if (evo != null)
                Trade(evo, i);
        }
    }

    public void ExecuteMakeEvosEasier()
    {
        for (var i = 0; i < Evolutions.Length; i++)
        {
            var evo = Evolutions[i];
            if (evo != null)
                MakeEasier(evo);
        }
    }

    public void ExecuteRemoveTimeBased()
    {
        for (var i = 0; i < Evolutions.Length; i++)
        {
            var evo = Evolutions[i];
            if (evo != null)
                RemoveTimeBased(evo);
        }
    }

    public void ExecuteEvolveEveryLevel()
    {
        foreach (var evo in Evolutions)
        {
            if (evo != null)
                MakeEvolveEveryLevel(evo);
        }
    }

    private void Randomize(EvolutionSet evo, int i)
    {
        if (evo?.PossibleEvolutions == null) return;
        var evos = evo.PossibleEvolutions;
        foreach (EvolutionMethod v in evos)
        {
            if (v.Method > 0)
            {
                v.Species = Randomizer.GetRandomSpecies(v.Species, i);
                v.Form = FormRandomizer.GetRandomForme(v.Species);
            }
        }
    }

    private void Trade(EvolutionSet evo, int i)
    {
        if (evo?.PossibleEvolutions == null) return;
        var evos = evo.PossibleEvolutions;
        foreach (EvolutionMethod v in evos)
        {
            if (Config.Generation == 6 && v.Method == 5) // Gen 6 uses Argument rather than Level
            {
                v.Argument = 30;
                v.Method = 4; // trade -> level up
            }
            else if (Config.Generation == 7 && v.Method == 5)
            {
                v.Level = 30;
                v.Method = 4; // trade -> level up
            }

            if (v.Method == 6) // trade with held item -> level up with held item
                v.Method = 19;

            if (v.Method == 7) // trade for opposite -> level up with party
            {
                if (i == 588)
                    v.Argument = 616; // Karrablast with Shelmet
                if (i == 616)
                    v.Argument = 588; // Shelmet with Karrablast
                v.Method = 22;
            }
        }
    }

    private void MakeEvolveEveryLevel(EvolutionSet evo)
    {
        if (evo?.PossibleEvolutions == null) return;
        var evos = evo.PossibleEvolutions;
        foreach (EvolutionMethod v in evos)
        {
            switch (Config.Generation)
            {
                case 6:
                    v.Argument = 1;
                    v.Method = 4;
                    v.Species = 1;
                    break;
                default:
                    v.Argument = 0;
                    v.Form = 0;
                    v.Level = 1;
                    v.Method = 4;
                    v.Species = 1; // will be randomized after
                    break;
            }
        }

        if (evos[1].Species != 0) // has other branched evolutions; remove them
        {
            for (int i = 1; i < evos.Length; i++)
                evos[i] = new EvolutionMethod();
        }
    }

    private void MakeEasier(EvolutionSet evo)
    {
        if (evo?.PossibleEvolutions == null) return;
        var evos = evo.PossibleEvolutions;
        foreach (EvolutionMethod v in evos)
        {
            if (v.Method == 0) continue;

            // Cap high level requirements to 36
            if (v.Method == 4)
            {
                if (Config.Generation == 6 && v.Argument > 36)
                    v.Argument = 36;
                else if (Config.Generation == 7 && v.Level > 36)
                    v.Level = 36;
            }

            // Convert high friendship / affection evolutions to level 25 level-up
            if (v.Method is 1 or 2 or 3 or 29)
            {
                v.Method = 4;
                if (Config.Generation == 6)
                {
                    v.Argument = 25;
                }
                else
                {
                    v.Level = 25;
                    v.Argument = 0;
                }
            }
        }
    }

    private void RemoveTimeBased(EvolutionSet evo)
    {
        if (evo?.PossibleEvolutions == null) return;
        var evos = evo.PossibleEvolutions;
        var toAdd = new System.Collections.Generic.List<EvolutionMethod>();

        foreach (EvolutionMethod v in evos)
        {
            if (v.Method == 0) continue;

            // Morning / Night friendship -> standard friendship
            if (v.Method is 2 or 3)
                v.Method = 1;

            // Held item with Day / Night: provide both Day (19) and Night (20) so it evolves 24/7 without bag soft-lock
            if (v.Method is 19 or 20)
            {
                int alternateMethod = v.Method == 19 ? 20 : 19;
                bool alreadyHasAlt = System.Linq.Enumerable.Any(evos, e => e != null && e.Method == alternateMethod && e.Argument == v.Argument && e.Species == v.Species);
                if (!alreadyHasAlt)
                {
                    toAdd.Add(new EvolutionMethod
                    {
                        Method = alternateMethod,
                        Argument = v.Argument,
                        Species = v.Species,
                        Form = v.Form,
                        Level = v.Level
                    });
                }
            }

            // Rockruff time-specific level-ups (Morning: 32, Night: 33, Dusk: 40) -> standard level-up
            if (v.Method is 32 or 33 or 40)
                v.Method = 4;

            // Version Daytime / Nighttime -> Version Any Time
            if (v.Method is 37 or 38)
                v.Method = 36;
        }

        foreach (var extra in toAdd)
        {
            int emptySlot = System.Array.FindIndex(evos, e => e == null || e.Method == 0);
            if (emptySlot >= 0)
                evos[emptySlot] = extra;
        }
    }
}