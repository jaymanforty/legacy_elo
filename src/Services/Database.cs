﻿using Discord.WebSocket;
using ELO.CompetitionModels.Legacy;
using ELO.Models;
using ELO.Preconditions.Dev;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;

namespace ELO.Services
{
    public class Database : DbContext
    {
        public static DatabaseConfig Config;

        public static string Serverip;

        public static string Dbname;

        public static string Username;

        public static string Password;

        public static string Port;

        public DbSet<Rank> Ranks { get; set; }

        public DbSet<CommandPermission> Permissions { get; set; }

        public DbSet<Competition> Competitions { get; set; }

        public DbSet<Lobby> Lobbies { get; set; }

        public DbSet<QueuedPlayer> QueuedPlayers { get; set; }

        public DbSet<Player> Players { get; set; }

        public DbSet<Ban> Bans { get; set; }

        public DbSet<GameResult> GameResults { get; set; }

        public DbSet<ScoreUpdate> ScoreUpdates { get; set; }

        public DbSet<TeamCaptain> TeamCaptains { get; set; }

        public DbSet<TeamPlayer> TeamPlayers { get; set; }

        public DbSet<Token> LegacyTokens { get; set; }

        public DbSet<ManualGameResult> ManualGameResults { get; set; }

        public DbSet<ManualGameScoreUpdate> ManualGameScoreUpdates { get; set; }

        public DbSet<Map> Maps { get; set; }

        public DbSet<GameVote> Votes { get; set; }

        public DbSet<PartyMember> PartyMembers { get; set; }

        public DbSet<PremiumService.PremiumRole> PremiumRoles { get; set; }

        public DbSet<Premium.DeletedPremiumUser> DeletedPremiumUsers { get; set; }

        public DbSet<WhitelistedUser> WhitelistedDevelopers { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseNpgsql($"Host={Serverip};Port={Port};Database={Dbname};Username={Username};Password={Password};");
        }

        public Competition GetOrCreateCompetition(ulong guildId)
        {
            var comp = Competitions.FirstOrDefault(x => x.GuildId == guildId);
            if (comp == null)
            {
                comp = new Competition(guildId);
                Competitions.Add(comp);
                SaveChanges();
            }

            return comp;
        }

        public Lobby GetLobbyWithQueue(ISocketMessageChannel channel)
        {
            return Lobbies.AsQueryable().Where(x => x.ChannelId == channel.Id).Include(x => x.Queue).FirstOrDefault();
        }

        public Lobby GetLobby(ISocketMessageChannel channel)
        {
            return Lobbies.Find(channel.Id);
        }

        public Player GetUser(SocketGuildUser user)
        {
            return GetUser(user.Guild.Id, user.Id);
        }

        public Player GetUser(ulong guildId, ulong userId)
        {
            return Players.Find(guildId, userId);
        }

        public GameResult GetLatestGame(Lobby lobby)
        {
            return GameResults.AsQueryable().Where(x => x.LobbyId == lobby.ChannelId).OrderByDescending(x => x.GameId).FirstOrDefault();
        }

        public bool IsCurrentlyPicking(Lobby lobby)
        {
            return GameResults.FirstOrDefault(x => x.LobbyId == lobby.ChannelId && x.GameId == lobby.CurrentGameCount)?.GameState == GameState.Picking;
        }

        public TeamCaptain GetTeamCaptain(GameResult game, int teamId)
        {
            return TeamCaptains.FirstOrDefault(x => x.GuildId == game.GuildId && x.ChannelId == game.LobbyId && x.GameNumber == game.GameId && x.TeamNumber == teamId);
        }

        public TeamCaptain GetTeamCaptain(ulong guildId, ulong channelId, int gameNumber, int teamId)
        {
            return TeamCaptains.FirstOrDefault(x => x.GuildId == guildId && x.ChannelId == channelId && x.GameNumber == gameNumber && x.TeamNumber == teamId);
        }

        public IEnumerable<TeamPlayer> GetTeamPlayers(ulong guildId, ulong channelId, int gameNumber, int teamId)
        {
            return TeamPlayers.AsQueryable().Where(x => x.GuildId == guildId && x.ChannelId == channelId && x.GameNumber == gameNumber && x.TeamNumber == teamId);
        }

        public IEnumerable<TeamPlayer> GetTeam1(GameResult game)
        {
            return TeamPlayers.AsQueryable().Where(x => x.GuildId == game.GuildId && x.ChannelId == game.LobbyId && x.GameNumber == game.GameId && x.TeamNumber == 1);
        }

        public HashSet<ulong> GetTeamFull(GameResult game, int teamNumber)
        {
            ulong cap = default;
            if (game.GamePickMode == PickMode.Captains_HighestRanked || game.GamePickMode == PickMode.Captains_Random || game.GamePickMode == PickMode.Captains_RandomHighestRanked)
            {
                cap = TeamCaptains.AsQueryable().Where(x => x.GuildId == game.GuildId && x.ChannelId == game.LobbyId && x.GameNumber == game.GameId && x.TeamNumber == teamNumber).Select(x => x.UserId).FirstOrDefault();
            }
            var players = TeamPlayers.AsQueryable().Where(x => x.GuildId == game.GuildId && x.ChannelId == game.LobbyId && x.GameNumber == game.GameId && x.TeamNumber == teamNumber).Select(x => x.UserId).ToHashSet();
            if (cap != default)
            {
                players.Add(cap);
            }
            return players;
        }

        public IEnumerable<TeamPlayer> GetTeam2(GameResult game)
        {
            return TeamPlayers.AsQueryable().Where(x => x.GuildId == game.GuildId && x.ChannelId == game.LobbyId && x.GameNumber == game.GameId && x.TeamNumber == 2);
        }

        public IEnumerable<QueuedPlayer> GetQueuedPlayers(ulong guildId, ulong channelId)
        {
            return QueuedPlayers.AsQueryable().Where(x => x.GuildId == guildId && x.ChannelId == channelId);
        }

        public IEnumerable<QueuedPlayer> GetAllQueuedPlayers()
        {
            return QueuedPlayers.AsQueryable().Where(x => x.GuildId != 0); //Always true or will get all the queued players
        }

        public IEnumerable<QueuedPlayer> GetQueue(GameResult game)
        {
            return QueuedPlayers.AsQueryable().Where(x => x.GuildId == game.GuildId && x.ChannelId == game.LobbyId);
        }

        public IEnumerable<QueuedPlayer> GetQueue(Lobby lobby)
        {
            return QueuedPlayers.AsQueryable().Where(x => x.GuildId == lobby.GuildId && x.ChannelId == lobby.ChannelId);
        }

        public IEnumerable<ScoreUpdate> GetScoreUpdates(ulong guildId, ulong channelId, int gameNumber)
        {
            return ScoreUpdates.AsQueryable().Where(x => x.GuildId == guildId && x.ChannelId == channelId && x.GameNumber == gameNumber);
        }

        public ScoreUpdate GetScoreUpdate(ulong guildId, ulong channelId, int gameNumber, ulong userId)
        {
            return ScoreUpdates.FirstOrDefault(x => x.GuildId == guildId && x.ChannelId == channelId && x.GameNumber == gameNumber && x.UserId == userId);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Rank>(entity =>
            {
                entity.HasKey(e => e.RoleId);
                entity.Property(e => e.Points).IsRequired();
                entity.Property(e => e.WinModifier);
                entity.Property(e => e.LossModifier);
                entity.HasOne(e => e.Competition)
                    .WithMany(e => e.Ranks)
                    .HasForeignKey(e => e.GuildId);
            });

            modelBuilder.Entity<GameVote>(entity =>
            {
                //As users can only have one vote, the team # is not part of the key
                entity.HasKey(e => new { e.GuildId, e.ChannelId, e.UserId, e.GameId });
                entity.HasOne(e => e.Game)
                    .WithMany(e => e.Votes)
                    .HasForeignKey(e => new { e.ChannelId, e.GameId });
            });

            modelBuilder.Entity<Player>(entity =>
            {
                entity.HasKey(e => new { e.GuildId, e.UserId });
                entity.HasOne(e => e.Competition)
                    .WithMany(e => e.Players)
                    .HasForeignKey(e => e.GuildId);
            });

            modelBuilder.Entity<PremiumService.PremiumRole>(entity =>
            {
                entity.HasKey(e => e.RoleId);
            });

            modelBuilder.Entity<Competition>(entity =>
            {
                entity.HasKey(e => e.GuildId);
            });

            modelBuilder.Entity<Map>(entity =>
            {
                entity.HasKey(e => new { e.ChannelId, e.MapName });
                entity.HasOne(e => e.Lobby)
                    .WithMany(e => e.Maps)
                    .HasForeignKey(e => e.ChannelId);
            });

            modelBuilder.Entity<CommandPermission>(entity =>
            {
                entity.HasKey(e => new { e.GuildId, e.CommandName });
            });

            modelBuilder.Entity<Ban>();

            modelBuilder.Entity<Lobby>(entity =>
            {
                entity.HasKey(e => e.ChannelId);
                entity.HasOne(e => e.Competition)
                    .WithMany(e => e.Lobbies)
                    .HasForeignKey(e => e.GuildId);
            });

            modelBuilder.Entity<GameResult>(entity =>
            {
                entity.HasKey(e => new { e.LobbyId, e.GameId });
                entity.HasOne(e => e.Lobby)
                    .WithMany(e => e.GameResults)
                    .HasForeignKey(e => e.LobbyId);
            });

            modelBuilder.Entity<QueuedPlayer>(entity =>
            {
                entity.HasKey(e => new { e.ChannelId, e.UserId });
                entity.HasOne(e => e.Lobby)
                    .WithMany(e => e.Queue)
                    .HasForeignKey(e => e.ChannelId);
            });

            modelBuilder.Entity<TeamPlayer>(entity =>
            {
                entity.HasKey(e => new { e.ChannelId, e.UserId, e.GameNumber, e.TeamNumber });
                entity.HasOne(e => e.Game)
                    .WithMany(e => e.TeamPlayers)
                    .HasForeignKey(e => new { e.ChannelId, e.GameNumber });
            });

            modelBuilder.Entity<TeamCaptain>(entity =>
            {
                entity.HasKey(e => new { e.ChannelId, e.GameNumber, e.TeamNumber });
                entity.HasOne(e => e.Game)
                    .WithMany(e => e.Captains)
                    .HasForeignKey(e => new { e.ChannelId, e.GameNumber });
            });

            modelBuilder.Entity<PartyMember>(entity =>
            {
                entity.HasKey(e => new { e.ChannelId, e.UserId });
            });

            modelBuilder.Entity<ScoreUpdate>(entity =>
            {
                entity.HasKey(e => new { e.ChannelId, e.UserId, e.GameNumber });
                entity.HasOne(e => e.Game)
                    .WithMany(e => e.ScoreUpdates)
                    .HasForeignKey(e => new { e.ChannelId, e.GameNumber });
            });

            modelBuilder.Entity<ManualGameScoreUpdate>(entity =>
            {
                entity.HasKey(e => new { e.GuildId, e.ManualGameId, e.UserId });
                entity.HasOne(e => e.Game)
                    .WithMany(e => e.ScoreUpdates)
                    .HasForeignKey(e => new { e.GuildId, e.ManualGameId });
            });

            modelBuilder.Entity<ManualGameResult>(entity =>
            {
                entity.HasKey(e => new { e.GuildId, e.GameId });
                entity.HasOne(e => e.Comp)
                    .WithMany(e => e.ManualGames)
                    .HasForeignKey(e => e.GuildId);
            });

            modelBuilder.Entity<Premium.DeletedPremiumUser>(entity =>
            {
                entity.HasKey(e => e.UserId);
            });

            modelBuilder.Entity<WhitelistedUser>(entity =>
            {
                entity.HasKey(e => e.UserId);
            });
        }
    }
}