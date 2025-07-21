using PKHeX.Core;
using SysBot.Base;
using System;

namespace SysBot.Pokemon.Twitch;

public static class TwitchCommandsHelper<T> where T : PKM, new()
{
    // Helper functions for commands
    public static bool AddToWaitingList(string setstring, string display, string username, ulong mUserId, bool sub, out string msg)
    {
        if (!TwitchBot<T>.Info.GetCanQueue())
        {
            msg = "Drink a coffee & be patient! I am not currently accepting trade requests!";
            return false;
        }

        var set = ShowdownUtil.ConvertToShowdown(setstring);
        if (set == null)
        {
            msg = $"Skipping trade, @{username}: Empty nickname provided for the species.";
            return false;
        }
        var template = AutoLegalityWrapper.GetTemplate(set);
        if (template.Species == 0)
        {
            msg = $"Skipping trade, @{username}: Please Verify the Trade Command & Check your Format!";
            return false;
        }

        if (set.InvalidLines.Count != 0)
        {
            msg = $"Skipping trade, @{username}: Unable to legalize Showdown Set:\n{string.Join("\n", set.InvalidLines)}";
            return false;
        }

        try
        {
            var sav = AutoLegalityWrapper.GetTrainerInfo<T>();
            var isEggRequest = set.Nickname.Equals("Egg", StringComparison.OrdinalIgnoreCase) && Breeding.CanHatchAsEgg(set.Species);
            var pkm = isEggRequest ? sav.GetLegalEgg(set, out var result) : sav.GetLegal(template, out result);

            if (!pkm.CanBeTraded())
            {
                msg = $"Bruh, @{username}: That Pokémon is Blocked from Trading!";
                return false;
            }

            if (pkm is T pk)
            {
                var valid = new LegalityAnalysis(pkm).Valid;
                if (valid)
                {
                    var tq = new TwitchQueue<T>(pk, new PokeTradeTrainerInfo(display, mUserId), username, sub);
                    TwitchBot<T>.QueuePool.RemoveAll(z => z.UserName == username); // remove old requests if any
                    TwitchBot<T>.QueuePool.Add(tq);
                    msg = $"@{username} - Added to the Cafe Waiting Queue. Please Whisper Your Trade Code To Me! Your Request From The Waiting Queue Will be Removed If You Are Too Slow!";
                    return true;
                }
            }

            var reason = result == "Timeout" ? "Yikes! This coffee is too hot! Set took too long to generate." : "Unable to legalize the Pokémon.";
            msg = $"Skipping trade, @{username}: {reason}";
        }
        catch (Exception ex)
        {
            LogUtil.LogSafe(ex, nameof(TwitchCommandsHelper<T>));
            msg = $"Skipping trade, @{username}: An unexpected problem occurred and we spilled the coffee. :(";
        }
        return false;
    }

    public static string ClearTrade(string user)
    {
        var result = TwitchBot<T>.Info.ClearTrade(user);
        return GetClearTradeMessage(result);
    }

    public static string ClearTrade(ulong userID)
    {
        var result = TwitchBot<T>.Info.ClearTrade(userID);
        return GetClearTradeMessage(result);
    }

    private static string GetClearTradeMessage(QueueResultRemove result)
    {
        return result switch
        {
            QueueResultRemove.CurrentlyProcessing => "Bruh, drink your coffee & chill. Looks like you're currently being processed! Did not remove from queue.",
            QueueResultRemove.CurrentlyProcessingRemoved => "Looks like you're currently being processed! Removed from queue.",
            QueueResultRemove.Removed => "Removed you from the queue.",
            _ => "Bruh, you are not currently in the queue.",
        };
    }

    public static string GetCode(ulong parse)
    {
        var detail = TwitchBot<T>.Info.GetDetail(parse);
        return detail == null
            ? "Bruh, you are not currently in the queue."
            : $"Your Trade Code is {detail.Trade.Code:0000 0000}";
    }
}
