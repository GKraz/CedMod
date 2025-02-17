﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CedMod.Addons.QuerySystem;
using MEC;
using Newtonsoft.Json;
using PluginAPI.Core;
using VoiceChat;
using VoiceChat.Networking;

namespace CedMod
{
    public class BanSystem
    {
        public static Dictionary<string, Dictionary<string, string>> CachedStates = new Dictionary<string, Dictionary<string, string>>();

        public static readonly object Banlock = new object();
        public static async Task HandleJoin(CedModPlayer player)
        {
            if (CedModMain.Singleton.Config.CedMod.ShowDebug)
                Log.Debug("Join");
            try
            {
                if (player.ReferenceHub.serverRoles.BypassStaff || player.ReferenceHub.isLocalPlayer)
                    return;

                Dictionary<string, string> info = new Dictionary<string, string>();
                bool req = false;
                lock (CachedStates)
                {
                    if (CachedStates.ContainsKey(player.UserId))
                        info = CachedStates[player.UserId];
                    else
                        req = true;
                }
                
                if (req)
                    info = (Dictionary<string, string>) await API.APIRequest("Auth/", $"{player.UserId}&{player.IpAddress}?banLists={string.Join(",", ServerPreferences.Prefs.BanListReadBans.Select(s => s.Id))}&banListMutes={string.Join(",", ServerPreferences.Prefs.BanListReadMutes.Select(s => s.Id))}");

                if (info == null)
                {
                    if (File.Exists(Path.Combine(CedModMain.PluginConfigFolder, "CedMod", "Internal", $"tempb-{player.UserId}")))
                    {
                        info = new Dictionary<string, string>()
                        {
                            {"success", "true"},
                            {"vpn", "false"},
                            {"isbanned", "true"},
                            {"preformattedmessage", "You are banned from this server, please check back later to see the ban reason."},
                            {"iserror", "false"}
                        };
                    }
                    else if (File.Exists(Path.Combine(CedModMain.PluginConfigFolder, "CedMod", "Internal", $"tempd-{player.UserId}")))
                    {
                        info = JsonConvert.DeserializeObject<Dictionary<string, string>>(await File.ReadAllTextAsync(Path.Combine(CedModMain.PluginConfigFolder, "CedMod", "Internal", $"tempd-{player.UserId}")));
                        File.SetLastWriteTimeUtc(Path.Combine(CedModMain.PluginConfigFolder, "CedMod", "Internal", $"tempd-{player.UserId}"), DateTime.UtcNow);
                    }
                    else
                    {
                        info = new Dictionary<string, string>()
                        {
                            {"success", "true"},
                            {"vpn", "false"},
                            {"isbanned", "false"},
                            {"iserror", "false"}
                        };
                    }

                    if (File.Exists(Path.Combine(CedModMain.PluginConfigFolder, "CedMod", "Internal", $"tempm-{player.UserId}")) && !File.Exists(Path.Combine(CedModMain.PluginConfigFolder, "CedMod", "Internal", $"tempum-{player.UserId}")))
                    {
                        info.Add("mute", "Global");
                        info.Add("mutereason", "Temporarily unavailable");
                        info.Add("muteduration", "Until revoked");
                    }
                }
                else
                {
                    if (info["isbanned"] == "true")
                    {
                        await File.WriteAllTextAsync(Path.Combine(CedModMain.PluginConfigFolder, "CedMod", "Internal", $"tempd-{player.UserId}"), JsonConvert.SerializeObject(info));
                    }
                    else
                    {
                        File.Delete(Path.Combine(CedModMain.PluginConfigFolder, "CedMod", "Internal", $"tempd-{player.UserId}"));
                    }
                }

                if (CedModMain.Singleton.Config.CedMod.ShowDebug)
                    Log.Debug(JsonConvert.SerializeObject(info));
                
                string reason;
                if (info["success"] == "true" && info["vpn"] == "true" && info["isbanned"] == "false")
                {
                    reason = info["reason"];
                    Log.Info($"user: {player.UserId} attempted connection with blocked ASN/IP/VPN/Hosting service");
                    int count = 5;
                    while (count >= 0)
                    {
                        await Task.Delay(500);
                        count--;
                        try
                        {
                            player.Disconnect(reason);
                        }
                        catch (Exception e)
                        {
                            continue;
                        }
                            
                        break;
                    }
                }
                else
                {
                    if (info["success"] == "true" && info["vpn"] == "false" && info["isbanned"] == "true")
                    {
                        reason = info["preformattedmessage"];
                        Log.Info($"user: {player.UserId} attempted connection with active ban disconnecting");
                        int count = 5;
                        while (count >= 0)
                        {
                            await Task.Delay(500);
                            count--;
                            try
                            {
                                player.Disconnect(reason + "\n" + CedModMain.Singleton.Config.CedMod.AdditionalBanMessage);
                            }
                            catch (Exception e)
                            {
                                continue;
                            }
                            
                            break;
                        }
                    }
                    else
                    {
                        if (info["success"] == "true" && info["vpn"] == "false" && info["isbanned"] == "false" &&
                            info["iserror"] == "true")
                        {
                            Log.Info($"Message from CedMod server: {info["error"]}");
                        }
                    }
                }

                if (info["success"] == "true" && info.ContainsKey("mute") && info.ContainsKey("mutereason") && info.ContainsKey("muteduration"))
                {
                    Log.Info($"user: {player.UserId} joined while muted, issuing mute...");
                    Enum.TryParse(info["mute"], out MuteType muteType);
                    player.SendConsoleMessage(CedModMain.Singleton.Config.CedMod.MuteMessage.Replace("{type}", muteType.ToString()).Replace("{duration}", info["muteduration"]).Replace("{reason}", info["mutereason"]), "red");
                    Broadcast.Singleton.TargetAddElement(player.Connection, CedModMain.Singleton.Config.CedMod.MuteMessage.Replace("{type}", muteType.ToString()).Replace("{duration}", info["muteduration"]).Replace("{reason}", info["mutereason"]), 5, Broadcast.BroadcastFlags.Normal);
                    // if (muteType == MuteType.Global)
                    //     player.Mute(true);
                    //
                    // if (muteType == MuteType.Intercom)
                    //     player.IntercomMute(true);

                    Timing.CallDelayed(0.1f, () =>
                    {
                        if (muteType == MuteType.Global)
                        {
                            VoiceChatMutes.SetFlags(player.ReferenceHub, VcMuteFlags.LocalRegular);
                        }

                        if (muteType == MuteType.Intercom)
                        {
                            VoiceChatMutes.SetFlags(player.ReferenceHub, VcMuteFlags.LocalIntercom);
                        }
                    });

                    if (!string.IsNullOrEmpty(CedModMain.Singleton.Config.CedMod.MuteCustomInfo))
                        player.CustomInfo = CedModMain.Singleton.Config.CedMod.MuteCustomInfo.Replace("{type}", muteType.ToString());
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex.ToString());
            }
        }
    }
    
    public enum MuteType
    {
        Intercom,
        Global
    }
}
