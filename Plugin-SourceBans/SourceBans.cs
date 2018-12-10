// <copyright file="SourceBans.cs" company="Steve Guidetti">
// Copyright (c) Steve Guidetti. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.
// </copyright>

namespace SevenMod.Plugin.SourceBans
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Text;
    using System.Timers;
    using SevenMod.Admin;
    using SevenMod.Console;
    using SevenMod.ConVar;
    using SevenMod.Core;
    using SevenMod.Database;

    /// <summary>
    /// Plugin that integrates SevenMod with the SourceBans backend.
    /// </summary>
    public sealed class SourceBans : PluginAbstract, IDisposable
    {
        /// <summary>
        /// A <see cref="DateTime"/> object representing the Unix epoch.
        /// </summary>
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1);

        /// <summary>
        /// The memory cache.
        /// </summary>
        private readonly SBCache cache = new SBCache();

        /// <summary>
        /// The list of players to be rechecked for bans.
        /// </summary>
        private readonly List<string> recheckPlayers = new List<string>();

        /// <summary>
        /// Represents the database connection.
        /// </summary>
        private Database database;

        /// <summary>
        /// Represents the backup queue database connection.
        /// </summary>
        private SQLiteDatabase backupDatabase;

        /// <summary>
        /// The timer to retry loading the admin users.
        /// </summary>
        private Timer adminRetryTimer;

        /// <summary>
        /// The timer to retry checking for bans.
        /// </summary>
        private Timer banRecheckTimer;

        /// <summary>
        /// The timer to run the failed ban queue.
        /// </summary>
        private Timer queueTimer;

        /// <summary>
        /// The value of the SBWebsite <see cref="ConVar"/>.
        /// </summary>
        private ConVarValue website;

        /// <summary>
        /// The value of the SBAddban <see cref="ConVar"/>.
        /// </summary>
        private ConVarValue addban;

        /// <summary>
        /// The value of the SBUnban <see cref="ConVar"/>.
        /// </summary>
        private ConVarValue unban;

        /// <summary>
        /// The value of the SBDatabasePrefix <see cref="ConVar"/>.
        /// </summary>
        private ConVarValue databasePrefix;

        /// <summary>
        /// The value of the SBRetryTime <see cref="ConVar"/>.
        /// </summary>
        private ConVarValue retryTime;

        /// <summary>
        /// The value of the SBProcessQueueTime <see cref="ConVar"/>.
        /// </summary>
        private ConVarValue processQueueTime;

        /// <summary>
        /// The value of the SBBackupConfigs <see cref="ConVar"/>.
        /// </summary>
        private ConVarValue backupConfigs;

        /// <summary>
        /// The value of the SBEnableAdmins <see cref="ConVar"/>.
        /// </summary>
        private ConVarValue enableAdmins;

        /// <summary>
        /// The value of the SBRequireSiteLogin <see cref="ConVar"/>.
        /// </summary>
        private ConVarValue requireSiteLogin;

        /// <summary>
        /// The value of the SBServerId <see cref="ConVar"/>.
        /// </summary>
        private ConVarValue serverId;

        /// <inheritdoc/>
        public override PluginInfo Info => new PluginInfo
        {
            Name = "SourceBans",
            Author = "SevenMod",
            Description = "Integrates SevenMod with the SourceBans backend.",
            Version = "0.1.0.0",
            Website = "https://github.com/SevenMod/Plugin-SourceBans"
        };

        /// <inheritdoc/>
        public override void OnLoadPlugin()
        {
            this.website = this.CreateConVar("SBWebsite", string.Empty, "Website address to tell the player where to go for unban, etc").Value;
            this.addban = this.CreateConVar("SBAddban", "True", "Allow or disallow admins access to addban command").Value;
            this.unban = this.CreateConVar("SBUnban", "True", "Allow or disallow admins access to unban command").Value;
            this.databasePrefix = this.CreateConVar("SBDatabasePrefix", "sb", "The Table prefix you set while installing the webpanel").Value;
            this.retryTime = this.CreateConVar("SBRetryTime", "45.0", "How many seconds to wait before retrying when a players ban fails to be checked", true, 15, true, 60).Value;
            this.processQueueTime = this.CreateConVar("SBProcessQueueTime", "5", "How often should we process the failed ban queue in minutes").Value;
            this.backupConfigs = this.CreateConVar("SBBackupConfigs", "True", "Enable backing up config files after getting admins from database").Value;
            this.enableAdmins = this.CreateConVar("SBEnableAdmins", "True", "Enable admin part of the plugin").Value;
            this.requireSiteLogin = this.CreateConVar("SBRequireSiteLogin", "False", "Require the admin to login once into website").Value;
            this.serverId = this.CreateConVar("SBServerId", "0", "This is the ID of this server (Check in the admin panel -> servers to find the ID of this server)").Value;

            this.AutoExecConfig(true, "SourceBans");
        }

        /// <inheritdoc/>
        public override void OnConfigsExecuted()
        {
            this.database = Database.Connect("sourcebans");

            this.backupDatabase = Database.OpenSQLiteDatabase("sourcebans-queue");
            this.backupDatabase.TFastQuery("CREATE TABLE IF NOT EXISTS queue (auth TEXT PRIMARY KEY ON CONFLICT REPLACE, ip TEXT, name TEXT, duration INTEGER, start_time INTEGER, reason TEXT, admin_auth TEXT, admin_ip TEXT);");

            PluginManager.Unload("BaseBans");

            this.RegAdminCmd("rehash", AdminFlags.RCON, "Reload SQL admins").Executed += this.OnRehashCommandExecuted;
            this.RegAdminCmd("ban", AdminFlags.Ban, "sm ban <#userid|name> <minutes|0> [reason]").Executed += this.OnBanCommandExecuted;
            this.RegAdminCmd("banip", AdminFlags.Ban, "sm banip <ip|#userid|name> <time> [reason]").Executed += this.OnBanipCommandExecuted;
            this.RegAdminCmd("addban", AdminFlags.RCON, "sm addban <time> <steamid> [reason]").Executed += this.OnAddbanCommandExecuted;
            this.RegAdminCmd("unban", AdminFlags.Unban, "sm unban <steamid|ip> [reason]").Executed += this.OnUnbanCommandExecuted;

            this.retryTime.ConVar.ValueChanged += this.OnRetryTimeChanged;
            this.processQueueTime.ConVar.ValueChanged += this.OnProcessQueueTimeChanged;
            this.enableAdmins.ConVar.ValueChanged += this.OnEnableAdminsChanged;
            this.requireSiteLogin.ConVar.ValueChanged += this.OnRequireSiteLoginChanged;
            this.serverId.ConVar.ValueChanged += this.OnServerIdChanged;

            this.ProcessBanQueue();
        }

        /// <inheritdoc/>
        public override void OnReloadAdmins()
        {
            if (!this.enableAdmins.AsBool || this.adminRetryTimer != null)
            {
                return;
            }

            if (this.cache.GetAdmins(out var list, out var expired) && !expired)
            {
                foreach (var admin in list)
                {
                    AdminManager.AddAdmin(admin.Identity, admin.Immunity, admin.Flags);
                }

                return;
            }

            this.LoadAdmins();
        }

        /// <inheritdoc/>
        public override bool OnPlayerLogin(SMClient client, StringBuilder rejectReason)
        {
            if (this.cache.GetPlayerStatus(client.PlayerId, out var banned, out var expired) && !expired)
            {
                if (banned)
                {
                    rejectReason.AppendLine($"You have been banned from this server, check {this.website.AsString} for more info");
                    return false;
                }

                return true;
            }

            if (!this.recheckPlayers.Contains(client.PlayerId))
            {
                this.CheckBans(client);
            }

            return base.OnPlayerLogin(client, rejectReason);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            ((IDisposable)this.database).Dispose();
            ((IDisposable)this.backupDatabase).Dispose();
            ((IDisposable)this.adminRetryTimer).Dispose();
            ((IDisposable)this.banRecheckTimer).Dispose();
            ((IDisposable)this.queueTimer).Dispose();
        }

        /// <summary>
        /// Converts a SteamID64 to a SteamID.
        /// </summary>
        /// <param name="playerId">The SteamID64.</param>
        /// <returns>The SteamID.</returns>
        private static string GetAuth(string playerId)
        {
            long.TryParse(playerId, out var id);
            id -= 76561197960265728L;
            var p1 = id % 2;
            var p2 = (id - p1) / 2;

            return $"STEAM_0:{p1}:{p2}";
        }

        /// <summary>
        /// Gets the current time as a Unix timestamp.
        /// </summary>
        /// <returns>The number of seconds since the Unix epoch.</returns>
        private static long GetTime()
        {
            return (long)DateTime.UtcNow.Subtract(Epoch).TotalSeconds;
        }

        /// <summary>
        /// Loads the admin users from the database.
        /// </summary>
        private void LoadAdmins()
        {
            this.database.TQuery($"SELECT name, flags, immunity FROM {this.databasePrefix.AsString}_srvgroups ORDER BY id").QueryCompleted += this.OnGroupsQueryCompleted;
        }

        /// <summary>
        /// Checks a player for bans in the database.
        /// </summary>
        /// <param name="client">The <see cref="SMClient"/> object representing the client to check.</param>
        private void CheckBans(SMClient client)
        {
            var prefix = this.databasePrefix.AsString;
            var auth = GetAuth(client.PlayerId).Substring(8);
            var ip = this.database.Escape(client.Ip);
            this.database.TQuery($"SELECT bid, ip FROM {prefix}_bans WHERE ((type = 0 AND authid REGEXP '^STEAM_[0-9]:{auth}$') OR (type = 1 AND ip = '{ip}')) AND (length = '0' OR ends > UNIX_TIMESTAMP()) AND RemoveType IS NULL", client).QueryCompleted += this.OnCheckPlayerBansQueryCompleted;
        }

        /// <summary>
        /// Called when the value of the SBRetryTime <see cref="ConVar"/> is changed.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">A <see cref="ConVarChangedEventArgs"/> object containing the event data.</param>
        private void OnRetryTimeChanged(object sender, ConVarChangedEventArgs e)
        {
            if (this.adminRetryTimer != null)
            {
                this.adminRetryTimer.Interval = this.retryTime.AsFloat * 1000;
            }

            if (this.banRecheckTimer != null)
            {
                this.banRecheckTimer.Interval = this.retryTime.AsFloat * 1000;
            }
        }

        /// <summary>
        /// Called when the value of the SBProcessQueueTime <see cref="ConVar"/> is changed.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">A <see cref="ConVarChangedEventArgs"/> object containing the event data.</param>
        private void OnProcessQueueTimeChanged(object sender, ConVarChangedEventArgs e)
        {
            if (this.queueTimer != null)
            {
                this.queueTimer.Interval = this.processQueueTime.AsFloat * 60000;
            }
        }

        /// <summary>
        /// Called when the value of the SBEnableAdmins <see cref="ConVar"/> is changed.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">A <see cref="ConVarChangedEventArgs"/> object containing the event data.</param>
        private void OnEnableAdminsChanged(object sender, ConVarChangedEventArgs e)
        {
            AdminManager.ReloadAdmins();
        }

        /// <summary>
        /// Called when the value of the SBRequireSiteLogin <see cref="ConVar"/> is changed.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">A <see cref="ConVarChangedEventArgs"/> object containing the event data.</param>
        private void OnRequireSiteLoginChanged(object sender, ConVarChangedEventArgs e)
        {
            AdminManager.ReloadAdmins();
        }

        /// <summary>
        /// Called when the value of the SBServerId <see cref="ConVar"/> is changed.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">A <see cref="ConVarChangedEventArgs"/> object containing the event data.</param>
        private void OnServerIdChanged(object sender, ConVarChangedEventArgs e)
        {
            AdminManager.ReloadAdmins();
        }

        /// <summary>
        /// Called when the rehash admin command is executed.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">An <see cref="AdminCommandEventArgs"/> object containing the event data.</param>
        private void OnRehashCommandExecuted(object sender, AdminCommandEventArgs e)
        {
            if (this.enableAdmins.AsBool)
            {
                AdminManager.ReloadAdmins();
            }
        }

        /// <summary>
        /// Called when the ban admin command is executed.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">An <see cref="AdminCommandEventArgs"/> object containing the event data.</param>
        private void OnBanCommandExecuted(object sender, AdminCommandEventArgs e)
        {
            if (e.Arguments.Count < 2)
            {
                this.ReplyToCommand(e.Client, "Usage: sm ban <#userid|name> <time|0> [reason]");
                return;
            }

            if (!uint.TryParse(e.Arguments[1], out var duration))
            {
                this.ReplyToCommand(e.Client, "Invaid ban duration");
                return;
            }

            if (duration == 0 && !AdminManager.CheckAccess(e.Client, AdminFlags.Unban))
            {
                this.ReplyToCommand(e.Client, "You do not have perm ban permission.");
                return;
            }

            if (this.ParseSingleTargetString(e.Client, e.Arguments[0], out var target))
            {
                var auth = GetAuth(target.PlayerId);
                var name = this.database.Escape(target.PlayerName);
                duration *= 60;
                var reason = (e.Arguments.Count > 2) ? this.database.Escape(string.Join(" ", e.Arguments.GetRange(2, e.Arguments.Count - 2).ToArray())) : string.Empty;
                var adminAuth = (e.Client != null) ? GetAuth(e.Client.PlayerId) : "STEAM_ID_SERVER";
                var adminIp = (e.Client != null) ? e.Client.Ip : string.Empty;
                this.InsertBan(auth, target.Ip, name, GetTime(), duration, reason, adminAuth, adminIp);

                this.ServerCommand($"kick {target.PlayerId} \"You have been banned from this server, check {this.website.AsString} for more info\"");

                this.ReplyToCommand(e.Client, $"{name} has been banned.");
            }
        }

        /// <summary>
        /// Called when the banip admin command is executed.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">An <see cref="AdminCommandEventArgs"/> object containing the event data.</param>
        private void OnBanipCommandExecuted(object sender, AdminCommandEventArgs e)
        {
            if (e.Arguments.Count < 2)
            {
                this.ReplyToCommand(e.Client, "Usage: sm banip <ip|#userid|name> <time> [reason]");
                return;
            }

            if (!uint.TryParse(e.Arguments[1], out var duration))
            {
                this.ReplyToCommand(e.Client, "Invaid ban duration");
                return;
            }

            if (duration == 0 && !AdminManager.CheckAccess(e.Client, AdminFlags.Unban))
            {
                this.ReplyToCommand(e.Client, "You do not have perm ban permission.");
                return;
            }

            if (this.ParseSingleTargetString(e.Client, e.Arguments[0], out var target))
            {
                var name = this.database.Escape(target.PlayerName);
                duration *= 60;
                var reason = (e.Arguments.Count > 2) ? this.database.Escape(string.Join(" ", e.Arguments.GetRange(2, e.Arguments.Count - 2).ToArray())) : string.Empty;
                var adminAuth = (e.Client != null) ? GetAuth(e.Client.PlayerId) : "STEAM_ID_SERVER";
                var adminIp = (e.Client != null) ? e.Client.Ip : string.Empty;
                this.InsertBan(string.Empty, target.Ip, name, GetTime(), duration, reason, adminAuth, adminIp);

                this.ServerCommand($"kick {target.PlayerId} \"You have been banned by this server, check {this.website.AsString} for more info\"");

                this.ReplyToCommand(e.Client, $"{name} has been banned.");
            }
        }

        /// <summary>
        /// Called when the addban admin command is executed.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">An <see cref="AdminCommandEventArgs"/> object containing the event data.</param>
        private void OnAddbanCommandExecuted(object sender, AdminCommandEventArgs e)
        {
            if (!this.addban.AsBool)
            {
                this.ReplyToCommand(e.Client, "The addban command is disabled.");
                return;
            }

            if (e.Arguments.Count < 2)
            {
                this.ReplyToCommand(e.Client, "Usage: sm addban <time> <steamid> [reason]");
                return;
            }

            if (!uint.TryParse(e.Arguments[0], out var duration))
            {
                this.ReplyToCommand(e.Client, "Invaid ban duration");
                return;
            }

            if (duration == 0 && !AdminManager.CheckAccess(e.Client, AdminFlags.Unban))
            {
                return;
            }

            if (SteamUtils.NormalizeSteamId(e.Arguments[1], out var playerId))
            {
                var auth = GetAuth(playerId);
                duration *= 60;
                var reason = (e.Arguments.Count > 2) ? this.database.Escape(string.Join(" ", e.Arguments.GetRange(2, e.Arguments.Count - 2).ToArray())) : string.Empty;
                var adminAuth = (e.Client != null) ? GetAuth(e.Client.PlayerId) : "STEAM_ID_SERVER";
                var adminIp = (e.Client != null) ? e.Client.Ip : string.Empty;
                this.InsertBan(auth, string.Empty, string.Empty, GetTime(), duration, reason, adminAuth, adminIp);

                this.ServerCommand($"kick {playerId} \"You are banned from this server, check {this.website.AsString} for more info\"");

                this.ReplyToCommand(e.Client, $"{e.Arguments[1]} has been banned.");
            }
            else
            {
                this.ReplyToCommand(e.Client, $"{e.Arguments[1]} is not a valid SteamID.");
            }
        }

        /// <summary>
        /// Called when the unban admin command is executed.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">An <see cref="AdminCommandEventArgs"/> object containing the event data.</param>
        private void OnUnbanCommandExecuted(object sender, AdminCommandEventArgs e)
        {
            if (!this.unban.AsBool)
            {
                this.ReplyToCommand(e.Client, $"The unban command is disabled. You must use the Web interface at {this.website.AsString}.");
                return;
            }

            if (e.Arguments.Count < 1)
            {
                this.ReplyToCommand(e.Client, "Usage: sm unban <steamid|ip> [reason]");
                return;
            }

            var prefix = this.databasePrefix.AsString;
            if (SteamUtils.NormalizeSteamId(e.Arguments[0], out var playerId))
            {
                var auth = GetAuth(playerId);
                this.database.TQuery($"SELECT bid FROM {prefix}_bans WHERE (type = 0 AND authid = '{auth}') AND (length = '0' OR ends > UNIX_TIMESTAMP()) AND RemoveType IS NULL", e).QueryCompleted += this.OnUnbanQueryCompleted;
            }
            else
            {
                var ip = this.database.Escape(e.Arguments[0]);
                this.database.TQuery($"SELECT bid FROM {prefix}_bans WHERE (type = 1 AND ip = '{ip}') AND (length = '0' OR ends > UNIX_TIMESTAMP()) AND RemoveType IS NULL", e).QueryCompleted += this.OnUnbanQueryCompleted;
            }
        }

        /// <summary>
        /// Inserts a new ban into the database.
        /// </summary>
        /// <param name="auth">The auth ID of the player.</param>
        /// <param name="ip">The IP address of the player.</param>
        /// <param name="name">The escaped name of the player.</param>
        /// <param name="startTime">The Unix timestamp of the time the ban was initiated.</param>
        /// <param name="duration">The duration of the ban in seconds.</param>
        /// <param name="reason">The escaped reason for the ban.</param>
        /// <param name="adminAuth">The auth ID of the admin invoking the ban.</param>
        /// <param name="adminIp">The IP address of the admin invoking the ban.</param>
        private void InsertBan(string auth, string ip, string name, long startTime, uint duration, string reason, string adminAuth, string adminIp)
        {
            var prefix = this.databasePrefix.AsString;
            var type = string.IsNullOrEmpty(auth) ? 1 : 0;
            var data = new Dictionary<string, object>
            {
                { "auth", auth },
                { "ip", ip },
                { "name", name },
                { "startTime", startTime },
                { "duration", duration },
                { "reason", reason },
                { "adminAuth", adminAuth },
                { "adminIp", adminIp },
            };
            var ends = startTime + duration;
            this.database.TFastQuery($"INSERT INTO {prefix}_bans (type, authid, ip, name, created, ends, length, reason, aid, adminIp, sid, country) VALUES ({type}, '{auth}', '{ip}', '{name}', {startTime}, {ends}, {duration}, '{reason}', IFNULL((SELECT aid FROM {prefix}_admins WHERE authid = '{adminAuth}' OR authid REGEXP '^STEAM_[0-9]:{adminAuth.Substring(8)}$'), '0'), '{adminIp}', {this.serverId.AsInt}, ' ')", data).QueryCompleted += this.OnInsertBanQueryCompleted;

            if (ends <= GetTime())
            {
                this.cache.SetPlayerStatus(auth, true, duration);
            }
        }

        /// <summary>
        /// Called when the insert ban query has completed.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">A <see cref="QueryCompletedEventArgs"/> object containing the event data.</param>
        private void OnInsertBanQueryCompleted(object sender, QueryCompletedEventArgs e)
        {
            var data = (Dictionary<string, object>)e.Data;
            if (!e.Success)
            {
                this.QueueFailedBan(data["auth"].ToString(), data["ip"].ToString(), data["name"].ToString(), (long)data["startTime"], (uint)data["duration"], data["reason"].ToString(), data["adminAuth"].ToString(), data["adminIp"].ToString());
            }
            else
            {
                this.backupDatabase.TFastQuery($"DELETE FROM queue WHERE auth = '{data["auth"].ToString()}';");
            }
        }

        /// <summary>
        /// Called when the admin groups query has completed.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">A <see cref="QueryCompletedEventArgs"/> object containing the event data.</param>
        private void OnGroupsQueryCompleted(object sender, QueryCompletedEventArgs e)
        {
            if (!e.Success)
            {
                this.adminRetryTimer = new Timer(this.retryTime.AsFloat * 1000);
                this.adminRetryTimer.Elapsed += this.OnAdminRetryTimerElapsed;
                this.adminRetryTimer.Enabled = true;
                return;
            }

            var groups = new Dictionary<string, GroupInfo>();
            foreach (DataRow row in e.Results.Rows)
            {
                var name = row.ItemArray.GetValue(0).ToString();
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var flags = row.ItemArray.GetValue(1).ToString();
                int.TryParse(row.ItemArray.GetValue(2).ToString(), out var immunity);

                groups.Add(name, new GroupInfo(name, immunity, flags));
            }

            var prefix = this.databasePrefix.AsString;
            var queryLastLogin = this.requireSiteLogin.AsBool ? "lastvisit IS NOT NULL AND lastvisit != '' AND " : string.Empty;
            this.database.TQuery($"SELECT authid, (SELECT name FROM {prefix}_srvgroups WHERE name = srv_group AND flags != '') AS srv_group, srv_flags, immunity FROM {prefix}_admins_servers_groups AS asg LEFT JOIN {prefix}_admins AS a ON a.aid = asg.admin_id WHERE {queryLastLogin}server_id = {this.serverId.AsInt} OR srv_group_id = ANY(SELECT group_id FROM {prefix}_servers_groups WHERE server_id = {this.serverId.AsInt}) GROUP BY aid, authid, srv_password, srv_group, srv_flags, user", groups).QueryCompleted += this.OnAdminsQueryCompleted;
        }

        /// <summary>
        /// Called when the admin users query has completed.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">A <see cref="QueryCompletedEventArgs"/> object containing the event data.</param>
        private void OnAdminsQueryCompleted(object sender, QueryCompletedEventArgs e)
        {
            if (!e.Success)
            {
                this.adminRetryTimer = new Timer(this.retryTime.AsFloat * 1000);
                this.adminRetryTimer.Elapsed += this.OnAdminRetryTimerElapsed;
                this.adminRetryTimer.Enabled = true;
                return;
            }

            var groups = (Dictionary<string, GroupInfo>)e.Data;
            var admins = new List<SBCache.Admin>();
            foreach (DataRow row in e.Results.Rows)
            {
                var identity = row.ItemArray.GetValue(0).ToString();
                var groupName = row.ItemArray.GetValue(1).ToString();
                var flags = row.ItemArray.GetValue(2).ToString();
                int.TryParse(row.ItemArray.GetValue(3).ToString(), out var immunity);

                if (groups.TryGetValue(groupName, out var group))
                {
                    flags += group.Flags;
                    immunity = Math.Max(immunity, group.Immunity);
                }

                admins.Add(new SBCache.Admin(identity, flags, immunity));
                AdminManager.AddAdmin(identity, immunity, flags);
            }

            this.cache.SetAdminList(admins, 60 * 5);
        }

        /// <summary>
        /// Called when the admin user list retry timer elapses.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">An <see cref="ElapsedEventArgs"/> object containing the event data.</param>
        private void OnAdminRetryTimerElapsed(object sender, ElapsedEventArgs e)
        {
            this.adminRetryTimer.Dispose();
            this.adminRetryTimer = null;
            this.LoadAdmins();
        }

        /// <summary>
        /// Called when the player bans check query has completed.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">A <see cref="QueryCompletedEventArgs"/> object containing the event data.</param>
        private void OnCheckPlayerBansQueryCompleted(object sender, QueryCompletedEventArgs e)
        {
            var client = (SMClient)e.Data;
            if (!e.Success)
            {
                this.recheckPlayers.Add(client.PlayerId);
                if (this.banRecheckTimer == null)
                {
                    this.banRecheckTimer = new Timer(this.retryTime.AsFloat * 1000);
                    this.banRecheckTimer.Elapsed += this.OnBanRecheckTimerElapsed;
                    this.banRecheckTimer.Enabled = true;
                }

                return;
            }

            if (e.Results.Rows.Count > 0)
            {
                var prefix = this.databasePrefix.AsString;
                var bid = e.Results.Rows[0].ItemArray.GetValue(0).ToString();
                var ip = e.Results.Rows[0].ItemArray.GetValue(1).ToString();

                if (string.IsNullOrEmpty(e.Results.Rows[0].ItemArray.GetValue(1).ToString()))
                {
                    this.database.TFastQuery($"UPDATE {prefix}_bans SET `ip` = '{ip}' WHERE `bid` = '{bid}'");
                }

                var auth = GetAuth(client.PlayerId).Substring(8);
                var name = this.database.Escape(client.PlayerName);
                this.database.TFastQuery($"INSERT INTO {prefix}_banlog (sid, time, name, bid) VALUES ({this.serverId.AsInt}, UNIX_TIMESTAMP(), '{name}', (SELECT bid FROM {prefix}_bans WHERE ((type = 0 AND authid REGEXP '^STEAM_[0-9]:{auth}$') OR(type = 1 AND ip = '{client.Ip}')) AND RemoveType IS NULL LIMIT 0, 1))");

                this.ServerCommand($"kick {client.PlayerId} \"You have been banned from this server, check {this.website.AsString} for more info\"");
            }

            this.cache.SetPlayerStatus(client.PlayerId, e.Results.Rows.Count > 0, 60 * 5);
        }

        /// <summary>
        /// Called when the player ban retry timer elapses.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">An <see cref="ElapsedEventArgs"/> object containing the event data.</param>
        private void OnBanRecheckTimerElapsed(object sender, ElapsedEventArgs e)
        {
            this.banRecheckTimer.Dispose();
            this.banRecheckTimer = null;

            var list = this.recheckPlayers.ToArray();
            this.recheckPlayers.Clear();
            foreach (var playerId in list)
            {
                var client = ClientHelper.ForPlayerId(playerId);
                if (client != null)
                {
                    this.CheckBans(client);
                }
            }
        }

        /// <summary>
        /// Called when the unban check query has completed.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">A <see cref="QueryCompletedEventArgs"/> object containing the event data.</param>
        private void OnUnbanQueryCompleted(object sender, QueryCompletedEventArgs e)
        {
            if (e.Results.Rows.Count > 0)
            {
                var args = (AdminCommandEventArgs)e.Data;
                var prefix = this.databasePrefix.AsString;
                var bid = e.Results.Rows[0].ItemArray.GetValue(0).ToString();
                var reason = (args.Arguments.Count > 1) ? this.database.Escape(string.Join(" ", args.Arguments.GetRange(1, args.Arguments.Count - 1).ToArray())) : string.Empty;
                var adminAuth = (args.Client != null) ? GetAuth(args.Client.PlayerId) : "STEAM_ID_SERVER";
                var adminIp = (args.Client != null) ? args.Client.Ip : string.Empty;
                this.database.TFastQuery($"UPDATE {prefix}_bans SET RemovedBy = (SELECT aid FROM {prefix}_admins WHERE authid = '{adminAuth}' OR authid REGEXP '^STEAM_[0-9]:{adminAuth.Substring(8)}$'), RemoveType = 'U', RemovedOn = UNIX_TIMESTAMP(), ureason = '{reason}' WHERE bid = {bid}");

                this.ReplyToCommand(args.Client, $"{args.Arguments[0]} has been unbanned.");
            }
        }

        /// <summary>
        /// Stores a failed ban in the local database to be retried later.
        /// </summary>
        /// <param name="auth">The auth ID of the player.</param>
        /// <param name="ip">The IP address of the player.</param>
        /// <param name="name">The escaped name of the player.</param>
        /// <param name="startTime">The Unix timestamp of the time the ban was initiated.</param>
        /// <param name="duration">The duration of the ban in seconds.</param>
        /// <param name="reason">The escaped reason for the ban.</param>
        /// <param name="adminAuth">The auth ID of the admin invoking the ban.</param>
        /// <param name="adminIp">The IP address of the admin invoking the ban.</param>
        private void QueueFailedBan(string auth, string ip, string name, long startTime, uint duration, string reason, string adminAuth, string adminIp)
        {
            auth = this.backupDatabase.Escape(auth);
            reason = this.backupDatabase.Escape(reason);
            name = this.backupDatabase.Escape(name);
            ip = this.backupDatabase.Escape(ip);
            adminAuth = this.backupDatabase.Escape(adminAuth);
            adminIp = this.backupDatabase.Escape(adminIp);
            this.backupDatabase.TFastQuery($"INSERT INTO queue (auth, ip, name, duration, start_time, reason, admin_auth, admin_ip) VALUES ('{auth}', '{ip}', '{name}', {duration}, {startTime}, '{reason}', '{adminAuth}', '{adminIp}');").QueryCompleted += this.OnQueueBanQueryCompleted;

            this.StartQueueTimer();
        }

        /// <summary>
        /// Called when the queue failed ban query has completed.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">A <see cref="QueryCompletedEventArgs"/> object containing the event data.</param>
        private void OnQueueBanQueryCompleted(object sender, QueryCompletedEventArgs e)
        {
            if (!e.Success)
            {
                this.LogError("Failed to record ban in the local backup database.");
            }
        }

        /// <summary>
        /// Starts the timer to run the failed ban queue.
        /// </summary>
        private void StartQueueTimer()
        {
            if (this.queueTimer == null)
            {
                this.queueTimer = new Timer(this.processQueueTime.AsFloat * 60000);
                this.queueTimer.Elapsed += this.OnQueueTimerElapsed;
                this.queueTimer.Enabled = true;
            }
        }

        /// <summary>
        /// Called when the failed ban queue timer elapses.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">An <see cref="ElapsedEventArgs"/> object containing the event data.</param>
        private void OnQueueTimerElapsed(object sender, ElapsedEventArgs e)
        {
            this.queueTimer.Dispose();
            this.queueTimer = null;
            this.ProcessBanQueue();
        }

        /// <summary>
        /// Starts processing the failed ban queue.
        /// </summary>
        private void ProcessBanQueue()
        {
            this.backupDatabase.TQuery("SELECT auth, ip, name, duration, start_time, reason, admin_auth, admin_ip FROM queue;").QueryCompleted += this.OnQueueQueryCompleted;
        }

        /// <summary>
        /// Called when the failed ban queue query has completed.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">A <see cref="QueryCompletedEventArgs"/> object containing the event data.</param>
        private void OnQueueQueryCompleted(object sender, QueryCompletedEventArgs e)
        {
            if (!e.Success)
            {
                this.StartQueueTimer();
                return;
            }

            foreach (DataRow row in e.Results.Rows)
            {
                var auth = row.ItemArray.GetValue(0).ToString();
                var ip = row.ItemArray.GetValue(1).ToString();
                var name = this.database.Escape(row.ItemArray.GetValue(2).ToString());
                var duration = uint.Parse(row.ItemArray.GetValue(3).ToString());
                var startTime = long.Parse(row.ItemArray.GetValue(4).ToString());
                var reason = this.database.Escape(row.ItemArray.GetValue(5).ToString());
                var adminAuth = row.ItemArray.GetValue(6).ToString();
                var adminIp = row.ItemArray.GetValue(7).ToString();

                this.InsertBan(auth, ip, name, startTime, duration, reason, adminAuth, adminIp);
            }
        }
    }
}
