using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Deltin.CustomGameAutomation;

namespace JjunoInfection
{
    class Program
    {
        public static GeneralConfig Config = JjunoInfection.Config.ParseConfig();
        public static DiscordConfig DiscordConfig = JjunoInfection.Config.ParseDiscordConfig();
        public static Game Game;
        public static Task GameTask;

        static void Main(string[] args)
        {
            if (DiscordConfig.Enabled)
                Task.Run(() =>
                {
                    new Discord().RunBotAsync().GetAwaiter().GetResult();
                });

            while (true)
            {
                Console.Write(">");
                string input = Console.ReadLine();
                string[] split = input.Split(' ');
                string firstWord = split[0].ToLower();

                switch (firstWord)
                {
                    case "start":

                        List<int> aiSlots = null;
                        if (firstWord == "start")
                        {
                            Console.Write("AI Slots: ");
                            aiSlots = new List<int>();
                            string[] aiSlotsString = Console.ReadLine().Split(' ');
                            for (int i = 0; i < aiSlotsString.Length; i++)
                                if (int.TryParse(aiSlotsString[i], out int aislot))
                                    aiSlots.Add(aislot);
                            if (aiSlots.Count == 0)
                                aiSlots = null;
                        }

                        GameTask = Task.Run(() =>
                        {
                            try
                            {
                                Game = new Game(aiSlots);
                                Game.Play();
                            }
                            catch (OperationCanceledException)
                            {
                                Console.WriteLine("Bot stopped.");
                            }
                            catch (OverwatchClosedException)
                            {
                                Console.WriteLine("Overwatch closed, bot stopped.");
                            }
                            catch (InitializedException)
                            {
                                Console.WriteLine("Error: Already initialized!");
                            }
                            catch (OverwatchStartFailedException)
                            {
                                Console.WriteLine("Error: Failed to start Overwatch!");
                            }
                            catch (MissingOverwatchProcessException)
                            {
                                Console.WriteLine("Error: No Overwatch Process!");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("Error: Unhandled exception: " + ex);
                            }
                        });

                        break;

                    case "stop":
                        if (Game == null)
                            Console.WriteLine(Constants.ERROR_NOT_INITIALIZED);
                        else
                            Game.Stop();
                        break;

                    case "save":
                        Console.Write("Saving... ");
                        Profile.Save();
                        Console.WriteLine("Done.");
                        break;

                    case "filler-slots":
                        if (Game?.AISlots != null)
                            Console.WriteLine($"Filler slots: {string.Join(", ", Game.AISlots.OrderBy(slot => slot))}");
                        else
                            Console.WriteLine(Constants.ERROR_NOT_INITIALIZED);
                        break;

                    case "profiles":
                        var profiles = Profile.ProfileList;

                        if (profiles.Count == 0)
                            Console.WriteLine("Error: No profiles!");
                        else
                            for (int i = 0; i < profiles.Count; i++)
                                Console.WriteLine($"  {profiles[i].Name} ${profiles[i].JBucks}");

                        break;

                    case "help":
                        Console.WriteLine("Commands:");
                        Console.WriteLine("  start         Starts the bot.");
                        Console.WriteLine("  stop          Stops the bot.");
                        Console.WriteLine("  save          Saves every player's profile.");
                        Console.WriteLine("  profiles      Lists every profile.");
                        Console.WriteLine("  filler-slots  Gets the filler AI slots.");
                        break;

                    case "":
                        break;

                    default:
                        Console.WriteLine($"Unknown command \"{firstWord}\"");
                        goto case "help";
                }
            }
        }
    }
}
