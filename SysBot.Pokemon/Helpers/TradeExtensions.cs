using System;
using System.Linq;
using PKHeX.Core;
using SysBot.Base;

namespace SysBot.Pokemon;

public interface ITradePartner
{
    public uint TID7 { get; }
    public uint SID7 { get; }
    public string OT { get; }
    public int Game { get; }
    public int Gender { get; }
    public int Language { get; }
}

public class TradeExtensions<T> where T : PKM, new()
{
    public static bool TrySetPartnerDetails(RoutineExecutor<PokeBotState> executor, ITradePartner partner, PokeTradeDetail<T> trade, PokeTradeHubConfig config, out T pkm)
    {
        void Log(string msg) => executor.Log(msg);

        var original = trade.TradeData;
        pkm = (T)original.Clone();

        //Invalid trade request. Ditto is often requested for Masuda method, better to not apply partner details.
        if ((Species)pkm.Species is Species.None or Species.Ditto || trade.Type is not PokeTradeType.Specific)
        {
            Log("Can not apply Partner details: Not a specific trade request.");
            return false;
        }

        //Current handler cannot be past gen OT
        if (pkm.Generation != pkm.Format && !config.Legality.ForceTradePartnerDetails)
        {
            Log("Can not apply Partner details: Current handler cannot be different gen OT.");
            return false;
        }

        if (pkm is IHomeTrack track && track.Tracker != 0)
        {
            //Better to not override OT data that has already been registered to Home servers
            if (!config.Legality.ResetHOMETracker)
            {
                Log("Can not apply Partner details: the Pokémon already has a set Home Tracker.");
                return false;
            }
            else
            {
                track.Tracker = 0;
            }
        }

        //Only override trainer details if user didn't specify OT details in the Showdown/PK9 request
        if (HasRequestedTrainerDetails(pkm))
        {
            Log("Can not apply Partner details: Requested Pokémon already has set Trainer details.");
            return false;
        }

        pkm.OriginalTrainerName = partner.OT;
        pkm.OriginalTrainerGender = (byte)partner.Gender;
        pkm.TrainerTID7 = partner.TID7;
        pkm.TrainerSID7 = partner.SID7;
        pkm.Language = partner.Language;
        pkm.Version = (GameVersion)partner.Game;

        if (!original.IsNicknamed)
            pkm.ClearNickname();

        if (original.IsShiny)
            pkm.PID = (uint)((pkm.TID16 ^ pkm.SID16 ^ (pkm.PID & 0xFFFF) ^ original.ShinyXor) << 16) | (pkm.PID & 0xFFFF);

        if (!pkm.ChecksumValid)
            pkm.RefreshChecksum();

        var la = new LegalityAnalysis(pkm);
        if (!la.Valid && la.Results.Any(l => l.Identifier is CheckIdentifier.TrashBytes))
        {
            pkm = (T)FixTrashChars(pkm);

            if (!pkm.ChecksumValid)
                pkm.RefreshChecksum();

            la = new LegalityAnalysis(pkm);
        }

        if (!la.Valid)
        {
            if (config.Legality.ForceTradePartnerDetails)
                pkm.Version = original.Version;

            if (!pkm.ChecksumValid)
                pkm.RefreshChecksum();

            la = new LegalityAnalysis(pkm);

            if (!la.Valid)
            {
                Log("Can not apply Partner details:");
                Log(la.Report());
                return false;
            }
        }

        Log($"Applying trade partner details: {partner.OT} ({(partner.Gender == 0 ? "M" : "F")}), " +
                $"TID: {partner.TID7:000000}, SID: {partner.SID7:0000}, {(LanguageID)partner.Language} ({pkm.Version})");

        return true;
    }

    public static PKM FixTrashChars(T pkm)
    {
        const int MaxTrashCount = 0x1A;
        var offset = pkm switch
        {
            var t when t is PK8 => 0xF8,
            var t when t is PB8 => 0xF8,
            var t when t is PA8 => 0x110,
            var t when t is PK9 => 0xF8,
            _ => throw new ArgumentException("Invalid type", nameof(pkm)),
        };

        var data = pkm.Data;
        for (int i = offset; i < (offset + MaxTrashCount); i++)
        {
            if (i >= (pkm.OriginalTrainerName.Length * 2) + offset)
                data[i] = 0;
        }

        return (T)Activator.CreateInstance(typeof(T), data)!;
    }

    private static bool HasRequestedTrainerDetails(T requested)
    {
        var host_trainer = AutoLegalityWrapper.GetTrainerInfo<T>();

        if (!requested.OriginalTrainerName.Equals(host_trainer.OT))
            return true;

        if (requested.TID16 != host_trainer.TID16)
            return true;

        if (requested.SID16 != host_trainer.SID16)
            return true;

        if (requested.Language != host_trainer.Language)
            return true;

        return false;
    }

    public static string GetPokemonImageURL(T pkm, bool canGmax, bool fullSize)
    {
        bool md = false;
        bool fd = false;
        string[] baseLink;
        if (fullSize)
            baseLink = "https://raw.githubusercontent.com/zyro670/HomeImages/master/512x512/poke_capture_0001_000_mf_n_00000000_f_n.png".Split('_');
        else baseLink = "https://raw.githubusercontent.com/zyro670/HomeImages/master/128x128/poke_capture_0001_000_mf_n_00000000_f_n.png".Split('_');

        if (Enum.IsDefined(typeof(GenderDependent), pkm.Species) && !canGmax && pkm.Form is 0)
        {
            if (pkm.Gender == 0 && pkm.Species != (int)Species.Torchic)
                md = true;
            else fd = true;
        }

        int form = pkm.Species switch
        {
            (int)Species.Sinistea or (int)Species.Polteageist or (int)Species.Rockruff or (int)Species.Mothim => 0,
            (int)Species.Alcremie when pkm.IsShiny || canGmax => 0,
            _ => pkm.Form,

        };

        if (pkm.Species is (ushort)Species.Sneasel)
        {
            if (pkm.Gender is 0)
                md = true;
            else fd = true;
        }

        if (pkm.Species is (ushort)Species.Basculegion)
        {
            if (pkm.Gender is 0)
            {
                md = true;
                pkm.Form = 0;
            }
            else
            {
                pkm.Form = 1;
            }

            string s = pkm.IsShiny ? "r" : "n";
            string g = md && pkm.Gender is not 1 ? "md" : "fd";
            return $"https://raw.githubusercontent.com/zyro670/HomeImages/master/128x128/poke_capture_0" + $"{pkm.Species}" + "_00" + $"{pkm.Form}" + "_" + $"{g}" + "_n_00000000_f_" + $"{s}" + ".png";
        }

        baseLink[2] = pkm.Species < 10 ? $"000{pkm.Species}" : pkm.Species < 100 && pkm.Species > 9 ? $"00{pkm.Species}" : pkm.Species >= 1000 ? $"{pkm.Species}" : $"0{pkm.Species}";
        baseLink[3] = pkm.Form < 10 ? $"00{form}" : $"0{form}";
        baseLink[4] = pkm.PersonalInfo.OnlyFemale ? "fo" : pkm.PersonalInfo.OnlyMale ? "mo" : pkm.PersonalInfo.Genderless ? "uk" : fd ? "fd" : md ? "md" : "mf";
        baseLink[5] = canGmax ? "g" : "n";
        baseLink[6] = "0000000" + (pkm.Species == (int)Species.Alcremie && !canGmax ? pkm.Data[0xD0] : 0);
        baseLink[8] = pkm.IsShiny ? "r.png" : "n.png";
        return string.Join("_", baseLink);
    }
}
