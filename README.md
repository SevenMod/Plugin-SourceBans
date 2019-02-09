# SourceBans

Plugin for SevenMod that integrates with SourceBans. This plugin is in early development and is not ready for use in a live environment. **Use at your own risk.**

## Configuration

File: **SourceBans.xml**

| Property             | Default | Description                                                                                       |
| -------------------- | ------- | ------------------------------------------------------------------------------------------------- |
| `SBAddban`           | `True`  | Allow or disallow admins access to addban command                                                 |
| `SBBackupConfigs`    | `True`  | Enable backing up config files after getting admins from database (not implemented)               |
| `SBDatabasePrefix`   | `"sb"`  | The Table prefix you set while installing the webpanel                                            |
| `SBEnableAdmins`     | `True`  | Enable admin part of the plugin                                                                   |
| `SBProcessQueueTime` | `5`     | How often should we process the failed ban queue in minutes                                       |
| `SBRequireSiteLogin` | `False` | Require the admin to login once into website                                                      |
| `SBRetryTime`        | `45.0`  | How many seconds to wait before retrying when a players ban fails to be checked                   |
| `SBServerId`         | `0`     | This is the ID of this server (Check in the admin panel -> servers to find the ID of this server) |
| `SBUnban`            | `True`  | Allow or disallow admins access to unban command                                                  |
| `SBWebsite`          | `""`    | Website address to tell the player where to go for unban, etc                                     |

## Admin Commands

| Command     | Arguments (_\<required\> [optional]_)  | Access | Description                                       |
| ----------- | -------------------------------------- | ------ | ------------------------------------------------- |
| `sm addban` | \<minutes\|0\> \<playerId\> [reason]   | RCON   | Adds a player to the SourceBans ban list          |
| `sm ban`    | \<target\> \<minutes\|0\> [reason]     | Ban    | Bans a player from the server                     |
| `sm banip`  | \<ip\|target\> \<minutes\|0\> [reason] | Ban    | Bans a player from the server by their IP address |
| `sm rehash` |                                        | RCON   | Reloads SourceBans admins                         |
| `sm unban`  | \<playerId\|ip\> [reason]              | Unban  | Unbans a player from the server                   |

## License

The source code for SevenMod is available under the terms of the [MIT License](https://github.com/SevenMod/Plugin-SourceBans/blob/master/LICENSE.txt).
See the LICENSE.txt in the project root for details.