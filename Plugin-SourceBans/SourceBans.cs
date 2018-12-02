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
    using SevenMod.Admin;
    using SevenMod.Console;
    using SevenMod.ConVar;
    using SevenMod.Core;
    using SevenMod.Database;

    /// <summary>
    /// Plugin that periodically shows messages in chat.
    /// </summary>
    public sealed class SourceBans : PluginAbstract
    {
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

        /// <summary>
        /// Represents the database connection.
        /// </summary>
        private Database database;

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

            PluginManager.Unload("BaseBans");

            this.RegAdminCmd("rehash", AdminFlags.RCON, "Reload SQL admins").Executed += this.OnRehashCommandExecuted;
            this.RegAdminCmd("ban", AdminFlags.Ban, "sm ban <#userid|name> <minutes|0> [reason]").Executed += this.OnBanCommandExecuted;
            this.RegAdminCmd("banip", AdminFlags.Ban, "sm banip <ip|#userid|name> <time> [reason]").Executed += this.OnBanipCommandExecuted;
            this.RegAdminCmd("addban", AdminFlags.RCON, "sm addban <time> <steamid> [reason]").Executed += this.OnAddbanCommandExecuted;
            this.RegAdminCmd("unban", AdminFlags.Unban, "sm unban <steamid|ip> [reason]").Executed += this.OnUnbanCommandExecuted;

            this.enableAdmins.ConVar.ValueChanged += this.OnEnableAdminsChanged;
            this.requireSiteLogin.ConVar.ValueChanged += this.OnRequireSiteLoginChanged;
            this.serverId.ConVar.ValueChanged += this.OnServerIdChanged;
        }

        /// <inheritdoc/>
        public override void OnReloadAdmins()
        {
            if (!this.enableAdmins.AsBool)
            {
                return;
            }

            var prefix = this.databasePrefix.AsString;

            var groups = new Dictionary<string, GroupInfo>();
            var results = this.database.TQuery($"SELECT name, flags, immunity FROM {prefix}_srvgroups ORDER BY id");
            foreach (DataRow row in results.Rows)
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

            var queryLastLogin = this.requireSiteLogin.AsBool ? "lastvisit IS NOT NULL AND lastvisit != '' AND " : string.Empty;
            results = this.database.TQuery($"SELECT authid, (SELECT name FROM {prefix}_srvgroups WHERE name = srv_group AND flags != '') AS srv_group, srv_flags, immunity FROM {prefix}_admins_servers_groups AS asg LEFT JOIN {prefix}_admins AS a ON a.aid = asg.admin_id WHERE {queryLastLogin}server_id = {this.serverId.AsInt} OR srv_group_id = ANY(SELECT group_id FROM {prefix}_servers_groups WHERE server_id = {this.serverId.AsInt}) GROUP BY aid, authid, srv_password, srv_group, srv_flags, user");
            foreach (DataRow row in results.Rows)
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

                AdminManager.AddAdmin(identity, immunity, flags);
            }
        }

        /// <inheritdoc/>
        public override bool OnPlayerLogin(ClientInfo client, StringBuilder rejectReason)
        {
            var prefix = this.databasePrefix.AsString;
            var auth = this.GetAuth(client.playerId).Substring(8);
            var ip = this.database.Escape(client.ip);
            var results = this.database.TQuery($"SELECT bid, ip FROM {prefix}_bans WHERE ((type = 0 AND authid REGEXP '^STEAM_[0-9]:{auth}$') OR (type = 1 AND ip = '{ip}')) AND (length = '0' OR ends > UNIX_TIMESTAMP()) AND RemoveType IS NULL");

            if (results.Rows.Count > 0)
            {
                var bid = results.Rows[0].ItemArray.GetValue(0).ToString();

                if (string.IsNullOrEmpty(results.Rows[0].ItemArray.GetValue(1).ToString()))
                {
                    this.database.FastQuery($"UPDATE {prefix}_bans SET `ip` = '{ip}' WHERE `bid` = '{bid}'");
                }

                var name = this.database.Escape(client.playerName);
                this.database.FastQuery($"INSERT INTO {prefix}_banlog (sid, time, name, bid) VALUES ({this.serverId.AsInt}, UNIX_TIMESTAMP(), '{name}', (SELECT bid FROM {prefix}_bans WHERE ((type = 0 AND authid REGEXP '^STEAM_[0-9]:{auth}$') OR(type = 1 AND ip = '{client.ip}')) AND RemoveType IS NULL LIMIT 0, 1))");

                rejectReason.AppendLine($"You have been banned by this server, check {this.website.AsString} for more info");
                return false;
            }

            return base.OnPlayerLogin(client, rejectReason);
        }

        /// <summary>
        /// Converts a SteamID64 to a SteamID.
        /// </summary>
        /// <param name="playerId">The SteamID64.</param>
        /// <returns>The SteamID.</returns>
        private string GetAuth(string playerId)
        {
            long.TryParse(playerId, out var id);
            id -= 76561197960265728L;
            var p1 = id % 2;
            var p2 = (id - p1) / 2;

            return $"STEAM_0:{p1}:{p2}";
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
        }

        /// <summary>
        /// Called when the banip admin command is executed.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">An <see cref="AdminCommandEventArgs"/> object containing the event data.</param>
        private void OnBanipCommandExecuted(object sender, AdminCommandEventArgs e)
        {
        }

        /// <summary>
        /// Called when the addban admin command is executed.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">An <see cref="AdminCommandEventArgs"/> object containing the event data.</param>
        private void OnAddbanCommandExecuted(object sender, AdminCommandEventArgs e)
        {
        }

        /// <summary>
        /// Called when the unban admin command is executed.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">An <see cref="AdminCommandEventArgs"/> object containing the event data.</param>
        private void OnUnbanCommandExecuted(object sender, AdminCommandEventArgs e)
        {
        }
    }
}
