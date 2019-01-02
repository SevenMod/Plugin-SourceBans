// <copyright file="SBCache.cs" company="Steve Guidetti">
// Copyright (c) Steve Guidetti. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.
// </copyright>

namespace SevenMod.Plugin.SourceBans
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Represents the memory cache for SourceBans.
    /// </summary>
    internal class SBCache
    {
        /// <summary>
        /// The list of player banned statuses.
        /// </summary>
        private Dictionary<string, Entry<bool>> playerStatus = new Dictionary<string, Entry<bool>>();

        /// <summary>
        /// The cached list of admin users.
        /// </summary>
        private Entry<List<Admin>> admins = new Entry<List<Admin>>
        {
            Expires = DateTime.Now,
            Value = null,
        };

        /// <summary>
        /// Gets the cached banned status of a player.
        /// </summary>
        /// <param name="playerId">The identifier of the player to look up.</param>
        /// <param name="banned">Will be set to a value indicating whether the player is banned if the player is found; otherwise will be set to <c>false</c>.</param>
        /// <param name="expired">Will be set to a value indicating whether the cached data is expired if the player is found; otherwise will be set to <c>false</c>.</param>
        /// <returns>A value indicating whether the player was found in the cache.</returns>
        public bool GetPlayerStatus(string playerId, out bool banned, out bool expired)
        {
            if (this.playerStatus.TryGetValue(playerId, out var entry))
            {
                banned = entry.Value;
                expired = DateTime.Now > entry.Expires;
                return true;
            }

            banned = expired = false;
            return false;
        }

        /// <summary>
        /// Sets the banned status of a player in the cache.
        /// </summary>
        /// <param name="playerId">The identifier of the player.</param>
        /// <param name="banned">A value indicating whether the player is banned.</param>
        public void SetPlayerStatus(string playerId, bool banned)
        {
            this.playerStatus[playerId] = new Entry<bool>
            {
                Expires = DateTime.Now.AddMinutes(5),
                Value = banned,
            };
        }

        /// <summary>
        /// Gets the cached list of admin users.
        /// </summary>
        /// <param name="list">Will be set to the cached list of admin users if it exists; otherwise will be set to <c>null</c>.</param>
        /// <param name="expired">Will be set to a value indicating whether the cached data is expired if it exists; otherwise will be set to <c>false</c>.</param>
        /// <returns>A value indicating whether the data exists in the cache.</returns>
        public bool GetAdmins(out List<Admin> list, out bool expired)
        {
            if (this.admins.Value != null)
            {
                list = this.admins.Value;
                expired = DateTime.Now > this.admins.Expires;
                return true;
            }

            list = null;
            expired = false;
            return false;
        }

        /// <summary>
        /// Sets the list of admin users in the cache.
        /// </summary>
        /// <param name="list">The list of admin users.</param>
        /// <param name="ttl">The time in seconds before the cache entry is considered expired.</param>
        public void SetAdminList(List<Admin> list, uint ttl)
        {
            this.admins.Expires = DateTime.Now.AddSeconds(ttl);
            this.admins.Value = list;
        }

        /// <summary>
        /// Represents an admin user.
        /// </summary>
        public struct Admin
        {
            /// <summary>
            /// The identifier of the user.
            /// </summary>
            public readonly string Identity;

            /// <summary>
            /// The access flag string.
            /// </summary>
            public readonly string Flags;

            /// <summary>
            /// The immunity level.
            /// </summary>
            public readonly int Immunity;

            /// <summary>
            /// Initializes a new instance of the <see cref="Admin"/> struct.
            /// </summary>
            /// <param name="identity">The identifier of the user.</param>
            /// <param name="flags">The access flag string.</param>
            /// <param name="immunity">The immunity level.</param>
            public Admin(string identity, string flags, int immunity)
            {
                this.Identity = identity;
                this.Flags = flags;
                this.Immunity = immunity;
            }
        }

        /// <summary>
        /// Represents an entry in the cache.
        /// </summary>
        /// <typeparam name="T">The type of data being stored in this entry.</typeparam>
        private struct Entry<T>
        {
            /// <summary>
            /// The time after which this entry is to be considered expired.
            /// </summary>
            public DateTime Expires;

            /// <summary>
            /// The value stored in this entry.
            /// </summary>
            public T Value;
        }
    }
}
