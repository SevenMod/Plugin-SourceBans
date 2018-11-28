// <copyright file="SourceBans.cs" company="Steve Guidetti">
// Copyright (c) Steve Guidetti. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.
// </copyright>

namespace SevenMod.Plugin.SourceBans
{
    using SevenMod.Admin;
    using SevenMod.Console;
    using SevenMod.ConVar;
    using SevenMod.Core;

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
        public override void LoadPlugin()
        {
            base.LoadPlugin();

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
        public override void ConfigsExecuted()
        {
            base.ConfigsExecuted();

            this.RegAdminCmd("rehash", AdminFlags.RCON, "Reload SQL admins").Executed += this.OnRehashCommandExecuted;
            this.RegAdminCmd("ban", AdminFlags.Ban, "sm ban <#userid|name> <minutes|0> [reason]").Executed += this.OnBanCommandExecuted;
            this.RegAdminCmd("banip", AdminFlags.Ban, "sm_banip <ip|#userid|name> <time> [reason]").Executed += this.OnBanipCommandExecuted;
            this.RegAdminCmd("addban", AdminFlags.RCON, "sm_addban <time> <steamid> [reason]").Executed += this.OnAddbanCommandExecuted;
            this.RegAdminCmd("unban", AdminFlags.Unban, "sm_unban <steamid|ip> [reason]").Executed += this.OnUnbanCommandExecuted;
        }

        /// <summary>
        /// Called when the rehash admin command is executed.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">An <see cref="AdminCommandEventArgs"/> object containing the event data.</param>
        private void OnRehashCommandExecuted(object sender, AdminCommandEventArgs e)
        {
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
