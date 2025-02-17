﻿#if !EXILED
using NWAPIPermissionSystem;
#else
using Exiled.Permissions.Extensions;
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using CedMod.Addons.QuerySystem;
using CedMod.Addons.QuerySystem.WS;
using CommandSystem;
using Newtonsoft.Json;
using PluginAPI.Core;

namespace CedMod.Commands
{
    [CommandHandler(typeof(RemoteAdminCommandHandler))]
    public class WarnCommand : ICommand, IUsageProvider
    {
        public string Command { get; } = "warn";

        public string[] Aliases { get; } = new string[]
        {
            "addwarning",
            "addwarn",
            "issuewarn",
            "issuewarning"
        };

        public string Description { get; } = "Warns a player, Warn reason can be multiple words.";
        
        public string[] Usage { get; } = new string[]
        {
            "%player%",
            "%reason%"
        };

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            if (arguments.Count <= 1)
            {
                response = "Missing arguments, warn <player> <reason>\nReason can be multiple words";
                return false;
            }
            
            CedModPlayer plr = CedModPlayer.Get(arguments.At(0));
            CedModPlayer send = Player.Get<CedModPlayer>(sender);
            string reason = arguments.Skip(1).Aggregate((current, n) => current + " " + n);

            Task.Run(async () =>
            {
                using (HttpClient client = new HttpClient())
                {
                    await VerificationChallenge.AwaitVerification();
                    var response = await client.PostAsync($"http{(QuerySystem.UseSSL ? "s" : "")}://" + QuerySystem.CurrentMaster + $"/Api/v3/Punishment/IssueWarn/{QuerySystem.QuerySystemKey}?userId={plr.UserId}&issuer={send.UserId}", new StringContent(JsonConvert.SerializeObject(new Dictionary<string, string> { { "Reason", reason } })));
                    var responseString = await response.Content.ReadAsStringAsync();
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        ThreadDispatcher.ThreadDispatchQueue.Enqueue(() =>
                        {
                            sender.Respond(responseString, true);
                        });
                    }
                    else
                    {
                        ThreadDispatcher.ThreadDispatchQueue.Enqueue(() =>
                        {
                            sender.Respond($"{response.StatusCode} - {responseString}");
                        });
                    }
                }
            });

            response = "Attempting to issue warning, please wait...";
            return true;
        }
    }
}