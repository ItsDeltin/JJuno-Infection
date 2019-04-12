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

            string botToken = Program.DiscordConfig.Token;

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
            if (!await IsAdmin())
                return;

            if (Game.Initialized)
            {
                await ReplyAsync("Error: Bot already started");
                return;
            }

            IUserMessage botReply = await ReplyAsync("Starting... ");

            Program.GameTask = Task.Run(() =>
            {
                try
                {
                    Program.Game = new Game();
                    Program.Game.SetupCompleted += (sender, e) => Game_SetupCompleted(sender, e, botReply);
                    Program.Game.Play();
                }
                catch (OverwatchStartFailedException ex)
                {
                    botReply.ModifyAsync(msg => msg.Content = $"Starting... Startup failed: {ex.Message}");
                }
                catch (InitializedException)
                {
                    botReply.ModifyAsync(msg => msg.Content = "Starting... Error: Already running!");
                }
                catch (Exception ex)
                {
                    botReply.ModifyAsync(msg => msg.Content = $"Starting... Error: {ex}");
                }
            });
        }

        private void Game_SetupCompleted(object sender, EventArgs e, IUserMessage botReply)
        {
            botReply.ModifyAsync(msg => msg.Content = $"Starting... Startup finished!");
        }

        [Command("stop")]
        public async Task StopAsync()
        {
            if (!await IsAdmin())
                return;

            if (!Game.Initialized)
                await ReplyAsync(Constants.ErrorNotInitialized);
            else
            {
                IUserMessage botReply = await ReplyAsync("Stopping... ");
                
                _ = Task.Run(() =>
                {
                    Program.Game.Stop();
                    Program.GameTask.Wait();
                    botReply.ModifyAsync(msg => msg.Content = "Stopping... bot stopped.");
                });
            }
        }

        [Command("kill")]
        public async Task KillAsync()
        {
            if (!await IsAdmin())
                return;

            if (Game.UsingProcess == null)
                await ReplyAsync("Error: No process to kill.");
            else
            {
                Game.UsingProcess.CloseMainWindow();
                Game.UsingProcess = null;
                IUserMessage botReply = await ReplyAsync("Killed the bot's Overwatch process.");
            }
        }

        [Command("profiles")]
        public async Task ProfilesAsync()
        {
            List<string> lines = new List<string>();
            int whitespaceLength = Profile.ProfileList.Select(p => p.Name).OrderByDescending(n => n.Length).First().Length;
            string whitespace = new string(' ', whitespaceLength);

            foreach (Profile profile in Profile.ProfileList)
                lines.Add($"{profile.Name}{whitespace.Substring(profile.Name.Length)} ${profile.JBucks}");

            await ReplyAsync($"```{string.Join("\n", lines)}```");
        }

        [Command("join")]
        public async Task JoinAsync(string battletag)
        {
            if (!Game.Initialized)
            {
                await ReplyAsync(Constants.ErrorNotInitialized);
                return;
            }

            _ = Task.Run(() =>
            {
                if (CustomGame.PlayerExists(battletag))
                {
                    Program.Game.InviteToGame.Add(battletag);
                    ReplyAsync("Inviting to game...");
                }
                else
                    ReplyAsync($"Could not find the player {battletag}.");
            });
        }

        [Command("gameinfo")]
        public async Task GameInfoAsync()
        {
            if (!Game.Initialized)
            {
                await ReplyAsync(Constants.ErrorNotInitialized);
                return;
            }

            string[] send = null;

            switch (Program.Game.GameState)
            {
                case GameState.InitialSetup:
                    send = new string[] {
                        "**Setting up bot**"
                    };
                    break;

                case GameState.Setup:
                    send = new string[] {
                        $"**Setting up next game**",
                        $"Map: {Program.Game.GICurrentMap.ShortName}"
                    };
                    break;

                case GameState.Waiting:
                    send = new string[] {
                        $"**Waiting for players**",
                        $"Map: `{Program.Game.GICurrentMap.ShortName}`",
                        $"Player count: `{Program.Game.GIWaitingCount}/{Program.Config.PlayerCount}` *(Minimum: {Program.Config.MinPlayers})*"
                    };
                    break;

                case GameState.Ingame:
                    send = new string[] {
                        $"**Ingame**",
                        $"Map: `{Program.Game.GICurrentMap.ShortName}`, Round: `{Program.Game.CurrentRound}`, Time: `{Program.Game.GIGameTime.Elapsed.ToString("mm\\:ss")}`",
                        $"**{Program.Game.GISurvivorCount}** Survivors vs **{Program.Game.GIZombieCount}** Zombies"
                    };
                    break;
            }

            await ReplyAsync($"{string.Join("\n", send)}");
        }

        [Command("help")]
        public async Task HelpAsync()
        {
            string[] help = new string[]
            {
                "Commands:",
                "    !join <battletag>  Join the game. Battletag is case sensitive.",
                "    !profiles          List all profiles and their worth.",
                "    !gameinfo          Get the info of the current match.",
                "  Admins:",
                "    !start             Start the bot.",
                "    !stop              Stop the bot.",
                "    !kill              Kill the Overwatch process the bot is using.",
            };
            await ReplyAsync($"```{string.Join("\n", help)}```");
        }

        private async Task<bool> IsAdmin()
        {
            string fullname = Context.User.Username.ToLower() + "#" + Context.User.Discriminator;
            bool isAdmin = Program.DiscordConfig?.Admins?.Contains(fullname) ?? false;

            if (!isAdmin)
                await ReplyAsync("Error: Not authorized.");

            return isAdmin;
        }
    }
}
