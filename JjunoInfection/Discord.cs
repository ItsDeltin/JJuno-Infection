using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Deltin.CustomGameAutomation;

namespace JjunoInfection
{
    public class Discord
    {
        private DiscordSocketClient Client;
        private CommandService Commands;
        private IServiceProvider Services;

        public async Task RunBotAsync()
        {
            Client = new DiscordSocketClient();
            Commands = new CommandService();

            Services = new ServiceCollection()
                .AddSingleton(Client)
                .AddSingleton(Commands)
                .BuildServiceProvider();

            string botToken = "MzIyMDMwMzQwMTI4NTA1ODY2.XKZ-mQ.TJR3ymgaemFE-1EKheW3Knx1fo8";

            Client.Log += Client_Log;

            await RegisterCommandsAsync();

            await Client.LoginAsync(TokenType.Bot, botToken);

            await Client.StartAsync();

            await Task.Delay(-1);
        }

        private Task Client_Log(LogMessage arg)
        {
            Console.WriteLine(arg);

            return Task.CompletedTask;
        }

        public async Task RegisterCommandsAsync()
        {
            Client.MessageReceived += HandleCommandAsync;

            await Commands.AddModulesAsync(Assembly.GetEntryAssembly(), Services);
        }

        private async Task HandleCommandAsync(SocketMessage arg)
        {
            var message = arg as SocketUserMessage;

            if (message is null || message.Author.IsBot) return;

            int argPos = 0;

            if (message.HasStringPrefix("!", ref argPos) || message.HasMentionPrefix(Client.CurrentUser, ref argPos))
            {
                var context = new SocketCommandContext(Client, message);

                var result = await Commands.ExecuteAsync(context, argPos, Services);

                if (!result.IsSuccess)
                    Console.WriteLine(result.ErrorReason);
            }
        }
    }

    public class Commands : ModuleBase<SocketCommandContext>
    {
        [Command("start")]
        public async Task StartAsync()
        {
            if (Program.Initialized)
            {
                await ReplyAsync("Bot already started");
                return;
            }

            await ReplyAsync("Starting...");
            try
            {
                Program.Game(true);
            }
            catch (OverwatchStartFailedException ex)
            {
                await ReplyAsync($"Startup failed: {ex}");
            }
        }

        [Command("kill")]
        public async Task KillAsync()
        {
            await ReplyAsync("Stopped.");
        }

        [Command("profiles")]
        public async Task ProfilesAsync()
        {
            List<string> lines = new List<string>();
            int whitespaceLength = Profile.ProfileList.Select(p => p.Name).OrderByDescending(n => n.Length).First().Length;
            string whitespace = new string(' ', whitespaceLength);

            bool bold = false;
            foreach (Profile profile in Profile.ProfileList)
                lines.Add($"{profile.Name}{whitespace.Substring(profile.Name.Length)} ${profile.JBucks}");

            await ReplyAsync($"```{string.Join("\n", lines)}```");
        }
    }
}
