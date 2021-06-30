using Discord;
using Discord.Commands;
using Discord.WebSocket;
using ELO.Extensions;
using ELO.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ELO.Preconditions;
using Microsoft.EntityFrameworkCore;
using ELO.Models;

namespace ELO.Modules
{
    public class QueueManagement : ModuleBase<ShardedCommandContext>
    {
        public static Dictionary<ulong, Dictionary<ulong, DateTime>> QueueDelays = new Dictionary<ulong, Dictionary<ulong, DateTime>>();

        public QueueManagement(Random random, LobbyService lobbyService, PremiumService premium)
        {
            Random = random;
            LobbyService = lobbyService;
            Premium = premium;
        }

        public Random Random { get; }

        public LobbyService LobbyService { get; }

        public PremiumService Premium { get; }

        [Command("Join", RunMode = RunMode.Sync)]
        [Alias("JoinLobby", "Join Lobby", "j", "sign", "play", "ready")]
        [Summary("Join the queue in the current lobby.")]
        [RateLimit(1, 5, Measure.Seconds, RateLimitFlags.None)]
        public virtual async Task JoinLobbyAsync()
        {
            using (var db = new Database())
            {
                if (!(Context.User as SocketGuildUser).IsRegistered(out var player))
                {
                    await Context.SimpleEmbedAsync("You must register in order to join a lobby.");
                    return;
                }

                var lobby = db.Lobbies.FirstOrDefault(x => x.ChannelId == Context.Channel.Id);
                if (lobby == null)
                {
                    await Context.SimpleEmbedAsync("This channel is not a lobby.");
                    return;
                }

                if (lobby.IsLocked)
                {
                    await Context.SimpleEmbedAsync("This lobby is frozen!");
                    return;
                }

                var now = DateTime.UtcNow;

                var lastBan = db.Bans.AsQueryable().Where(x => x.UserId == Context.User.Id && x.GuildId == Context.Guild.Id && x.ManuallyDisabled == false).ToArray()
                    .Where(x => x.TimeOfBan + x.Length > now)
                    .OrderByDescending(x => x.ExpiryTime)
                    .FirstOrDefault();
                if (lastBan != null)
                {
                    if (lobby.HideQueue)
                    {
                        await Context.Message.DeleteAsync();
                        await Context.SimpleEmbedAndDeleteAsync($"You are still banned from matchmaking for another: {lastBan.RemainingTime.GetReadableLength()}", Color.Red, TimeSpan.FromSeconds(5));
                        return;
                    }
                    await Context.SimpleEmbedAsync($"{Context.User.Mention} - You are still banned from matchmaking for another: {lastBan.RemainingTime.GetReadableLength()}", Color.Red);
                    return;
                }

                var queue = db.GetQueuedPlayers(Context.Guild.Id, Context.Channel.Id).ToList();

                //Not sure if this is actually needed.
                if (queue.Count >= lobby.PlayersPerTeam * 2)
                {
                    if (lobby.HideQueue)
                    {
                        await Context.Message.DeleteAsync();
                        await Context.SimpleEmbedAndDeleteAsync("Queue is full, wait for teams to be chosen before joining.", Color.Red, TimeSpan.FromSeconds(5));
                        return;
                    }

                    //Queue will be reset after teams are completely picked.
                    await Context.SimpleEmbedAsync($"{Context.User.Mention} - Queue is full, wait for teams to be chosen before joining.", Color.DarkBlue);
                    return;
                }

                var comp = db.GetOrCreateCompetition(Context.Guild.Id);
                if (!comp.AllowMultiQueueing)
                {
                    var queued = db.QueuedPlayers.AsQueryable().Where(x => x.GuildId == Context.Guild.Id && x.UserId == Context.User.Id && x.ChannelId != Context.Channel.Id).ToArray();
                    if (queued.Length > 0)
                    {
                        var guildChannels = queued.Select(x => MentionUtils.MentionChannel(x.ChannelId));

                        if (lobby.HideQueue)
                        {
                            await Context.Message.DeleteAsync();
                            await Context.SimpleEmbedAndDeleteAsync($"MultiQueuing is not enabled in this server.\nPlease leave: {string.Join("\n", guildChannels)}", Color.Red, TimeSpan.FromSeconds(5));
                            return;
                        }
                        await Context.SimpleEmbedAsync($"{Context.User.Mention} - MultiQueuing is not enabled in this server.\nPlease leave: {string.Join("\n", guildChannels)}", Color.Red);
                        return;
                    }
                }

                if (lobby.MinimumPoints != null)
                {
                    if (player.Points < lobby.MinimumPoints)
                    {
                        if (lobby.HideQueue)
                        {
                            await Context.Message.DeleteAsync();
                            await Context.SimpleEmbedAndDeleteAsync($"You need a minimum of {lobby.MinimumPoints} points to join this lobby.", Color.Red, TimeSpan.FromSeconds(5));
                            return;
                        }
                        await Context.SimpleEmbedAsync($"{Context.User.Mention} - You need a minimum of {lobby.MinimumPoints} points to join this lobby.", Color.Red);
                        return;
                    }
                }

                if (db.IsCurrentlyPicking(lobby))
                {
                    if (lobby.HideQueue)
                    {
                        await Context.Message.DeleteAsync();
                        await Context.SimpleEmbedAndDeleteAsync("Current game is picking teams, wait until this is completed.", Color.DarkBlue, TimeSpan.FromSeconds(5));
                        return;
                    }
                    await Context.SimpleEmbedAsync("Current game is picking teams, wait until this is completed.", Color.DarkBlue);
                    return;
                }

                if (queue.Any(x => x.UserId == Context.User.Id))
                {
                    if (lobby.HideQueue)
                    {
                        await Context.Message.DeleteAsync();

                        // await Context.SimpleEmbedAndDeleteAsync("You are already queued.", Color.DarkBlue, TimeSpan.FromSeconds(5));
                        return;
                    }

                    // await Context.SimpleEmbedAsync($"{Context.User.Mention} - You are already queued.", Color.DarkBlue);
                    return;
                }

                if (comp.RequeueDelay.HasValue)
                {
                    if (QueueDelays.ContainsKey(Context.Guild.Id))
                    {
                        var currentGuild = QueueDelays[Context.Guild.Id];
                        if (currentGuild.ContainsKey(Context.User.Id))
                        {
                            var currentUserLastJoin = currentGuild[Context.User.Id];
                            if (currentUserLastJoin + comp.RequeueDelay.Value > DateTime.UtcNow)
                            {
                                var remaining = currentUserLastJoin + comp.RequeueDelay.Value - DateTime.UtcNow;
                                if (lobby.HideQueue)
                                {
                                    await Context.SimpleEmbedAndDeleteAsync($"You cannot requeue for another {remaining.GetReadableLength()}", Color.Red);
                                    return;
                                }
                                await Context.SimpleEmbedAsync($"{Context.User.Mention} - You cannot requeue for another {remaining.GetReadableLength()}", Color.Red);
                                return;
                            }
                            else
                            {
                                currentUserLastJoin = DateTime.UtcNow;
                            }
                        }
                        else
                        {
                            currentGuild.Add(Context.User.Id, DateTime.UtcNow);
                        }
                    }
                    else
                    {
                        var newDict = new Dictionary<ulong, DateTime> { { Context.User.Id, DateTime.UtcNow } };
                        QueueDelays.Add(Context.Guild.Id, newDict);
                    }
                }

                db.QueuedPlayers.Add(new Models.QueuedPlayer
                {
                    UserId = Context.User.Id,
                    ChannelId = lobby.ChannelId,
                    GuildId = lobby.GuildId
                });
                if (queue.Count + 1 >= lobby.PlayersPerTeam * 2)
                {
                    db.SaveChanges();
                    await LobbyService.LobbyFullAsync(Context, lobby);
                    return;
                }

                if (lobby.HideQueue)
                {
                    await Context.Message.DeleteAsync();
                    await Context.SimpleEmbedAsync($"**[{queue.Count + 1}/{lobby.PlayersPerTeam * 2}]** A player joined the queue.", Color.Green);
                }
                else
                {
                    if (Context.User.Id == Context.Guild.OwnerId)
                    {
                        await Context.SimpleEmbedAsync($"**[{queue.Count + 1}/{lobby.PlayersPerTeam * 2}]** {Context.User.Mention} [{player.Points}] joined the queue.", Color.Green);
                    }
                    else
                    {
                        await Context.SimpleEmbedAsync($"**[{queue.Count + 1}/{lobby.PlayersPerTeam * 2}]** {(comp.NameFormat.Contains("score") ? $"{Context.User.Mention}" : $"{Context.User.Mention} [{player.Points}]")} joined the queue.", Color.Green);
                    }
                }

                db.SaveChanges();
            }
        }

        [Command("Leave", RunMode = RunMode.Sync)]
        [Alias("LeaveLobby", "Leave Lobby", "l", "out", "unsign", "remove", "unready")]
        [Summary("Leave the queue in the current lobby.")]
        [RateLimit(1, 5, Measure.Seconds, RateLimitFlags.None)]
        public virtual async Task LeaveLobbyAsync()
        {
            using (var db = new Database())
            {
                if (!(Context.User as SocketGuildUser).IsRegistered(out var player))
                {
                    await Context.SimpleEmbedAsync("You're not registered.");
                    return;
                }

                var lobby = db.Lobbies.FirstOrDefault(x => x.ChannelId == Context.Channel.Id);
                if (lobby == null)
                {
                    await Context.SimpleEmbedAsync("This channel is not a lobby.");
                    return;
                }

                var queue = db.GetQueuedPlayers(Context.Guild.Id, Context.Channel.Id).ToList();
                var queueMember = queue.FirstOrDefault(x => x.UserId == Context.User.Id);

                // User is not found in queue
                if (queueMember == null)
                {
                    if (lobby.HideQueue)
                    {
                        await Context.Message.DeleteAsync();
                        await Context.SimpleEmbedAndDeleteAsync("You are not queued for the next game.", Color.DarkBlue, TimeSpan.FromSeconds(5));
                    }
                    await Context.SimpleEmbedAsync("You are not queued for the next game.", Color.DarkBlue);
                }
                else
                {
                    var game = db.GetLatestGame(lobby);
                    if (game != null)
                    {
                        if (game.GameState == GameState.Picking)
                        {
                            await Context.SimpleEmbedAsync("Lobby is currently picking teams. You cannot leave a queue while this is happening.", Color.Red);
                            return;
                        }
                    }

                    db.QueuedPlayers.Remove(queueMember);
                    db.SaveChanges();

                    if (lobby.HideQueue)
                    {
                        await Context.Message.DeleteAsync();
                        await Context.SimpleEmbedAsync($"A player left the queue. **[{queue.Count - 1}/{lobby.PlayersPerTeam * 2}]**");
                        return;
                    }
                    else
                    {
                        await Context.SimpleEmbedAsync($"**[{queue.Count - 1}/{lobby.PlayersPerTeam * 2}]** {Context.User.Mention} [{player.Points}] left the queue.", Color.DarkBlue);
                        
                    }
                }
            }
        }

        [Command("Expire", RunMode = RunMode.Async)]
        [RequirePermission(PermissionLevel.Registered)]
        [Summary("Expire your queue after a certain time period")]
        public virtual async Task Expire(string timeStr = null)
        {

            //If no time entered tell the user when their queues will expire by looping through all their queues and finding the soonest expire time
            if (timeStr == null)
            {
                using (var db = new Database())
                {
                    var playerQueues = db.QueuedPlayers.AsQueryable().Where(x => x.UserId == Context.User.Id);
                    var soonestTime = DateTime.UtcNow.AddHours(24);

                    if (playerQueues.Count() > 0)
                    {
                        foreach (QueuedPlayer player in playerQueues)
                        {
                            if (player.ExpireAt < soonestTime)
                            {
                                soonestTime = player.ExpireAt;
                            }
                        }

                        TimeSpan timeToExpire = soonestTime.Subtract(DateTime.UtcNow);

                        await Context.Channel.SendMessageAsync(null, false, $"Your queues will expire in **{timeToExpire.Hours}h{timeToExpire.Minutes}m**".QuickEmbed(Color.Blue));
                    }
                    else
                    {
                        await Context.Channel.SendMessageAsync(null, false, $"No Queues found!".QuickEmbed(Color.Red));
                    }
                }
                return;
            }

            //Set the user's Expire time to entered string
            try
            {
                TimeSpan expireTime = TimeSpan.Parse(timeStr);

                //TODO: Configurable default times
                if (expireTime.TotalMinutes < 10)
                {
                    await Context.Channel.SendMessageAsync(null, false, "Minimum is 10 minutes!".QuickEmbed(Color.Red));
                    return;
                }
                if(expireTime.TotalMinutes > 240)
                {
                    await Context.Channel.SendMessageAsync(null, false, "Maximum is 4 hours!".QuickEmbed(Color.Red));
                    return;
                }

                using (var db = new Database())
                {
                    var playerQueues = db.QueuedPlayers.AsQueryable().Where(x => x.UserId == Context.User.Id);

                    foreach (QueuedPlayer player in playerQueues)
                    {
                        player.ExpireAt = DateTime.UtcNow.Add(expireTime);
                        db.QueuedPlayers.Update(player);
                    }

                    if (playerQueues.Count() > 0)
                    {
                        await Context.Channel.SendMessageAsync(null, false, $"Your queues will expire in **{expireTime.Hours}h{expireTime.Minutes}m**".QuickEmbed(Color.Blue));
                    }
                    else
                    {
                        await Context.Channel.SendMessageAsync(null, false, $"No Queues found!".QuickEmbed(Color.Red));
                    }

                    db.SaveChanges();
                }


            }
            catch (FormatException)
            {
                await Context.Channel.SendMessageAsync(null, false, "Syntax Error, must be in format `{hh:mm}`".QuickEmbed(Color.Red));
                return;
            }

            
        }

        [Command("FreezeQueue", RunMode = RunMode.Async)]
        [Alias("Freeze")]
        [RequirePermission(PermissionLevel.Moderator)]
        [Summary("Freeze the specified lobby to disallow players from joining")]
        public virtual async Task FreezeQueueAsync(ISocketMessageChannel channel = null)
        {
            if (channel == null)
            {
                channel = Context.Channel;
            }

            using (var db = new Database())
            {

                var lobby = db.Lobbies.FirstOrDefault(x => x.ChannelId == channel.Id);
                if (lobby == null)
                {
                    await Context.SimpleEmbedAsync("This channel is not a lobby.");
                    return;
                }

                if (lobby.IsLocked)
                {
                    await Context.SimpleEmbedAsync("This lobby is already frozen!");
                    return;
                }

                lobby.IsLocked = true;

                db.Lobbies.Update(lobby);

                db.SaveChanges();

                await Context.SimpleEmbedAsync($"<#{channel.Id}> queue is now frozen!");
            }
        }

        [Command("UnFreezeQueue", RunMode = RunMode.Async)]
        [Alias("UnFreeze")]
        [RequirePermission(PermissionLevel.Moderator)]
        [Summary("Freeze the specified lobby to disallow players from joining")]
        public virtual async Task UnFreezeQueueAsync(ISocketMessageChannel channel = null)
        {
            if (channel == null)
            {
                channel = Context.Channel;
            }

            using (var db = new Database())
            {

                var lobby = db.Lobbies.FirstOrDefault(x => x.ChannelId == channel.Id);
                if (lobby == null)
                {
                    await Context.SimpleEmbedAsync("This channel is not a lobby.");
                    return;
                }

                if (!lobby.IsLocked)
                {
                    await Context.SimpleEmbedAsync("This lobby is already unfrozen!");
                    return;
                }

                lobby.IsLocked = false;

                db.Lobbies.Update(lobby);

                db.SaveChanges();

                await Context.SimpleEmbedAsync($"<#{channel.Id}> queue is now unfrozen!");
            }
        }


        [Command("Map", RunMode = RunMode.Async)]
        [RequirePermission(PermissionLevel.Moderator)]
        [Summary("Select a random map for the lobby map list")]
        public virtual async Task Map2Async()
        {
            using (var db = new Database())
            {
                var comp = db.GetOrCreateCompetition(Context.Guild.Id);

                var lobby = db.Lobbies.FirstOrDefault(x => x.ChannelId == Context.Channel.Id);
                if (lobby == null)
                {
                    await Context.SimpleEmbedAsync("This channel is not a lobby.");
                    return;
                }

                // Select a random map from the db
                var maps = db.Maps.AsQueryable().Where(x => x.ChannelId == Context.Channel.Id).ToArray();

                var map = maps.OrderByDescending(m => Random.Next()).FirstOrDefault();
                if (map != null)
                {
                    var embed = new EmbedBuilder
                    {
                        Color = Color.Blue
                    };

                    embed.AddField("**Selected Map**", $"**{map.MapName}**");
                    await ReplyAsync("", false, embed.Build());
                }
                else
                {
                    await Context.SimpleEmbedAndDeleteAsync("There are no maps added in this lobby", Color.DarkOrange);
                }
            }
        }
    }
}