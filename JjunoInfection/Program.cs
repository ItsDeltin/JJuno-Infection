﻿using System;
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
        // Constants
        const int RoundCount = 5;
        const int AICount = 4;
        const int ZombieCount = 2;
        const int TestAICount = 0;
        const int StartSwappingAfter = 30; // Seconds
        static OWEvent OverwatchEvent = OWEvent.None;

        public static Config Config = Config.ParseConfig();

        static GameState GameState = GameState.Setup;
        static Profile[] LastZombies = new Profile[ZombieCount];
        static List<int> AISlots;

        static Random RandomMap = new Random();

        public static bool Initialized
        {
            get
            {
                lock (InitLock) return _init;
            }
            set
            {
                _init = value;
            }
        }
        static bool _init = false;
        static object InitLock = new object();
        public static Process UsingProcess = null;
        public static CancellationTokenSource CancelSource = new CancellationTokenSource();

        static void Main(string[] args)
        {
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
                        try
                        {
                            Game(false);
                        }
                        catch (MissingOverwatchProcessException)
                        {
                            Console.WriteLine("Error: No Overwatch Process!");
                        }
                        catch (InitializedException)
                        {
                            Console.WriteLine("Error: Already initialized!");
                        }
                        break;

                    case "save":
                        Console.Write("Saving... ");
                        Profile.Save();
                        Console.WriteLine("Done.");
                        break;

                    case "filler-slots":
                        Console.WriteLine($"Filler slots: {string.Join(", ", AISlots.OrderBy(slot => slot))}");
                        break;

                    case "profiles":
                        var profiles = Profile.ProfileList;
                        for (int i = 0; i < profiles.Count; i++)
                            Console.WriteLine($"  {profiles[i].Name} ${profiles[i].JBucks}");
                        break;

                    case "help":
                        Console.WriteLine("Commands:");
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

        public static void Game(bool createGame)
        {
            CancellationToken cancelToken = CancelSource.Token;

            if (Initialized)
                throw new InitializedException();

            CustomGame cg;
            lock (InitLock)
            {
                if (!createGame)
                {
                    Console.Write("AI Slots: ");
                    AISlots = new List<int>();
                    string[] input = Console.ReadLine().Split(' ');
                    for (int i = 0; i < input.Length; i++)
                        if (int.TryParse(input[i], out int aislot))
                            AISlots.Add(aislot);
                }
                
                if (cancelToken.IsCancellationRequested)
                {
                    AISlots = null;
                    return;
                }

                UsingProcess = CustomGame.GetOverwatchProcess();
                if (createGame)
                    UsingProcess = CustomGame.StartOverwatch(new OverwatchInfoAuto()
                    {
                        AutomaticallyCreateCustomGame = true,
                        CloseOverwatchProcessOnFailure = true,
                        BattlenetExecutableFilePath = Config.BattlenetExecutable,
                        OverwatchSettingsFilePath = Config.OverwatchSettingsFile
                    });
                cg = new CustomGame(new CustomGameBuilder() { OverwatchProcess = UsingProcess });

                Initialized = true;
            }

            Task.Run(() =>
            {
                // Round variables
                PlayerTracker playerTracker = new PlayerTracker();
                bool gameOver = false;
                bool roundOver = false;
                int currentRound = 0;
                List<Profile> vaccinated = new List<Profile>();

                try
                {
                    // Set the OnGameOver and OnRoundOver events.
                    cg.OnGameOver += (sender, e) => Cg_OnGameOver(ref gameOver, sender, e);
                    cg.OnRoundOver += (sender, e) => Cg_OnRoundOver(ref roundOver, sender, e);

                    // Setup commands
                    ListenTo BalanceCommand   = new ListenTo("$BALANCE",   listen: true,  getNameAndProfile: true, checkIfFriend: false, callback: (cd) => Command_Balance(cg, cd));
                    ListenTo VaccinateCommand = new ListenTo("$VACCINATE", listen: false, getNameAndProfile: true, checkIfFriend: false, callback: (cd) => Command_Vaccinate(cg, cd, playerTracker, vaccinated));
                    cg.Commands.Listen = true;
                    cg.Commands.ListenTo.Add(BalanceCommand);
                    cg.Commands.ListenTo.Add(VaccinateCommand);

                    // Join the match channel.
                    cg.Chat.SwapChannel(Channel.Match);

                    // Load the infection preset.
                    cg.Settings.LoadPreset(Config.PresetName);

                    // Add AI if the custom game was created.
                    if (createGame)
                    {
                        Thread.Sleep(1000);
                        cg.Interact.Move(0, 12);
                        cg.AI.AddAI(AIHero.Bastion, Difficulty.Easy, Team.BlueAndRed, AICount);
                        AISlots = cg.GetSlots(SlotFlags.All | SlotFlags.AIOnly);
                    }

                    cancelToken.ThrowIfCancellationRequested();

                    // Add filler AI
                    if (TestAICount > 0)
                    {
                        cg.AI.AddAI(AIHero.McCree, Difficulty.Medium, Team.BlueAndRed, TestAICount);
                        cg.WaitForSlotUpdate();
                    }

                    // Set up the game
                    SetupGame(cg, playerTracker, cancelToken);

                    GameState = GameState.Ingame;
                    VaccinateCommand.Listen = true;

                    for (; ; )
                    {
                        cancelToken.ThrowIfCancellationRequested();
                        Thread.Sleep(10);

                        var deadSlots = cg.GetSlots(SlotFlags.Blue | SlotFlags.DeadOnly).Where(slot => !AISlots.Contains(slot));

                        foreach (var deadSlot in deadSlots)
                        {
                            var redAISlots = AISlots.Where(slot => CustomGame.IsSlotRed(slot));
                            // If there are any AI slots in red, swap with it.
                            if (redAISlots.Count() > 0)
                            {
                                int swapWith = redAISlots.First();
                                cg.Interact.Move(deadSlot, swapWith);
                                AISlots[AISlots.IndexOf(swapWith)] = deadSlot; // Update the AI slot
                            }
                            // If not, just swap to red.
                            else
                            {
                                cg.Interact.SwapToRed(deadSlot);
                            }
                        }

                        cg.TrackPlayers(playerTracker, SlotFlags.Red | SlotFlags.PlayersOnly);
                        for (int i = vaccinated.Count - 1; i >= 0; i--)
                        {
                            int vaccinatedSlot = playerTracker.SlotFromPlayerIdentity(vaccinated[i].Identifier);
                            cg.Interact.SwapToBlue(vaccinatedSlot);
                            vaccinated.RemoveAt(i);
                        }

                        bool survivorsWin = true;

                        List<int> survivorSlots = cg.GetSlots(SlotFlags.Blue /*| SlotFlags.PlayersOnly*/).Where(slot => !AISlots.Contains(slot)).ToList();
                        if (survivorSlots.Count == 0 && !gameOver)
                        {
                            // All survivors are dead, zombies win.
                            gameOver = true;
                            survivorsWin = false;
                            cg.Chat.SendChatMessage("Survivors lose gg");
                        }

                        if (gameOver)
                        {
                            GameState = GameState.Setup;
                            VaccinateCommand.Listen = false;

                            cg.SendServerToLobby();

                            cancelToken.ThrowIfCancellationRequested();

                            // Award jbucks
                            // Give every initial zombie .5 jbucks
                            foreach (Profile initialZombie in LastZombies)
                                initialZombie?.Award(cg, "Initial zombie.", .5m);

                            if (survivorsWin)
                            {
                                // Survivors win.
                                decimal roundBonus = 2; // Every winning survivor gets 2 jbucks by default.
                                switch (survivorSlots.Count)
                                {
                                    // If there is 2 survivors left, award 5 jbucks to the remaining survivors.
                                    case 2:
                                        roundBonus = 5;
                                        break;
                                    // If there is 1 survivor left, award 10 jbucks to the last survivor.
                                    case 1:
                                        roundBonus = 10;
                                        break;
                                }

                                // Add jbucks to profile
                                foreach (int survivorSlot in survivorSlots)
                                    Profile.GetProfileFromSlot(cg, survivorSlot)?.Award(cg, $"Survived with {survivorSlots.Count} survivors left.", roundBonus);
                            }
                            else
                            {
                                // Zombies win.
                                decimal roundBonus = 0;
                                switch (currentRound)
                                {
                                    // If the zombies win in 1 round, award 5 jbucks to every zombie.
                                    case 0:
                                        roundBonus = 5;
                                        break;
                                    // If the zombies win in 2 rounds, award 3 jbucks to every zombie.
                                    case 1:
                                        roundBonus = 3;
                                        break;
                                    // If the zombies win in 3 rounds, award 1 jbuck to every zombie.
                                    case 2:
                                        roundBonus = 1;
                                        break;
                                }

                                // Add jbucks to profile
                                var zombieSlots = cg.GetSlots(SlotFlags.Red /*| SlotFlags.PlayersOnly*/).Where(slot => !AISlots.Contains(slot)).ToList();
                                if (roundBonus > 0)
                                    foreach (int zombieSlot in zombieSlots)
                                        Profile.GetProfileFromSlot(cg, zombieSlot)?.Award(cg, $"Won in {currentRound + 1} rounds.", roundBonus);
                            }

                            // Save all player's J-bucks to the system.
                            Profile.Save();

                            cg.Chat.SendChatMessage("Be notified of the next Zombie game! Join our discord http://discord.gg/xTVeqm");

                            cancelToken.ThrowIfCancellationRequested();

                            gameOver = false;
                            roundOver = false;
                            currentRound = 0;

                            SetupGame(cg, playerTracker, cancelToken);

                            GameState = GameState.Ingame;
                            VaccinateCommand.Listen = true;
                        }
                        else if (roundOver)
                        {
                            currentRound++;
                            cg.Chat.SendChatMessage($"Survivors need to win {RoundCount - currentRound} more rounds.");
                            Thread.Sleep(StartSwappingAfter * 1000);
                            roundOver = false;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("Bot stopped.");
                }
                catch (OverwatchClosedException)
                {
                    Console.WriteLine("Stopping bot, Overwatch closed.");
                }
                finally
                {
                    cg.Dispose();
                    playerTracker.Dispose();
                    Initialized = false;
                    Profile.Save();
                }
            });
        }

        static void Cg_OnRoundOver(ref bool roundOver, object sender, EventArgs e)
        {
            roundOver = true;
        }

        static void Cg_OnGameOver(ref bool gameOver, object sender, GameOverArgs e)
        {
            // Survivors won
            gameOver = true;

            CustomGame cg = (CustomGame)sender;
            cg.Chat.SendChatMessage("Survivors win gg");
        }

        static void SetupGame(CustomGame cg, PlayerTracker playerTracker, CancellationToken cancelToken)
        {
            // Choose random map
            Map[] maps = Map.GetMapsInGamemode(Gamemode.Elimination, OverwatchEvent);
            Map nextMap = maps[RandomMap.Next(maps.Length - 1)];
            cg.ToggleMap(Gamemode.Elimination, OverwatchEvent, ToggleAction.DisableAll, nextMap);
            cg.Chat.SendChatMessage($"Next map: {nextMap.ShortName}");

            cancelToken.ThrowIfCancellationRequested();

            // Swap AI in blue to red.
            List<int> validRedSlots = new List<int> { 6, 7, 8, 9, 10, 11 }.Where(vs => !AISlots.Contains(vs)).ToList();
            for (int i = 0; i < AISlots.Count && validRedSlots.Count > 0; i++)
                if (CustomGame.IsSlotBlue(AISlots[i]))
                {
                    cg.Interact.Move(AISlots[i], validRedSlots[0]);
                    AISlots[i] = validRedSlots[0];
                    validRedSlots.RemoveAt(0);
                }

            cancelToken.ThrowIfCancellationRequested();

            cg.Chat.ClosedChatIsDefault();

            // Swap players in red to blue.
            while (true) // Players in queue can drop down into the red team, use a while loop instead of a for loop.
            {
                cancelToken.ThrowIfCancellationRequested();

                var playersInRedSlots = cg.GetSlots(SlotFlags.Red).Where(slot => validRedSlots.Contains(slot)).ToList();
                if (playersInRedSlots.Count == 0)
                    break;

                SlotInfo preswap = new SlotInfo();
                cg.GetUpdatedSlots(preswap, SlotFlags.Red);

                cg.Interact.SwapToBlue(playersInRedSlots[0]);

                cg.WaitForSlotUpdate(preswap);
            }

            cg.Chat.OpenChatIsDefault();

            // Make everyone queueing for the game queue for blue.
            List<int> nonBlueQueue = cg.GetSlots(SlotFlags.RedQueue | SlotFlags.NeutralQueue);
            foreach (int queueSlot in nonBlueQueue)
                cg.Interact.SwapToBlue(queueSlot);

            if (nonBlueQueue.Count > 0)
                cg.WaitForSlotUpdate();

            cancelToken.ThrowIfCancellationRequested();

            // Set starting zombies.
            Profile[] startingZombies = new Profile[ZombieCount];

            int currentZombieCount = cg./*GetCount*/GetSlots(SlotFlags.Red /*| SlotFlags.PlayersOnly*/).Where(slot => !AISlots.Contains(slot)).Count();
            List<int> survivorSlots = cg.GetSlots(SlotFlags.Blue /*| SlotFlags.PlayersOnly */).Where(slot => !AISlots.Contains(slot)).ToList();
            while (currentZombieCount < ZombieCount && validRedSlots.Count > 0 && survivorSlots.Count > 0)
            {
                cancelToken.ThrowIfCancellationRequested();

                Profile zombieProfile = null;

                int moveSlot = -1;
                for (int i = 0; i < survivorSlots.Count && moveSlot == -1; i++)
                {
                    zombieProfile = Profile.GetProfileFromSlot(cg, survivorSlots[i]);
                    if (zombieProfile != null)
                    {
                        bool wasLastZombie = LastZombies.Any(lz => lz == zombieProfile);
                        if (!wasLastZombie)
                            moveSlot = survivorSlots[i];
                    }
                    else moveSlot = survivorSlots[i];
                }
                if (moveSlot == -1)
                {
                    zombieProfile = Profile.GetProfileFromSlot(cg, 0);
                    moveSlot = survivorSlots[0];
                }

                // Swap the starting zombie to red.
                cg.Interact.Move(moveSlot, validRedSlots[0]);

                // Set them as the last zombie
                startingZombies[currentZombieCount] = zombieProfile;

                // Remove the starting zombie from the survivor slots.
                survivorSlots.Remove(moveSlot);

                // Update the valid red slots.
                validRedSlots.RemoveAt(0);

                // Wait for the slots to update then get the new zombie count.
                currentZombieCount = cg./*GetCount*/GetSlots(SlotFlags.Red /*| SlotFlags.PlayersOnly*/).Where(slot => !AISlots.Contains(slot)).Count();
            }
            LastZombies = startingZombies;

            cancelToken.ThrowIfCancellationRequested();

            // Make everyone queueing for the game queue for red.
            List<int> nonRedQueue = cg.GetSlots(SlotFlags.BlueQueue | SlotFlags.NeutralQueue);
            foreach (int queueSlot in nonRedQueue)
                cg.Interact.SwapToRed(queueSlot);

            cancelToken.ThrowIfCancellationRequested();

            // Update the tracker.
            cg.TrackPlayers(playerTracker, SlotFlags.Red | SlotFlags.PlayersOnly);

            // Start the game.
            cg.StartGame();

            cancelToken.ThrowIfCancellationRequested();

            // Write the names of the starting zombies to the chat.
            string[] startingZombieNames = startingZombies.Select(sz => sz?.Name).ToArray();
            for (int i = 0; i < startingZombieNames.Length; i++)
                if (startingZombieNames[i] == null)
                    startingZombieNames[i] = "<unknown>";

            if (startingZombieNames.Length > 0)
                cg.Chat.SendChatMessage($"{Helpers.CommaSeperate(startingZombieNames)} {(startingZombieNames.Length == 1 ? "is" : "are")} the starting zombie{(startingZombieNames.Length == 1 ? "" : "s")}!");

            cancelToken.ThrowIfCancellationRequested();
            Thread.Sleep(StartSwappingAfter * 1000);
            cancelToken.ThrowIfCancellationRequested();
        }

        static void Command_Balance(CustomGame cg, CommandData cd)
        {
            Profile profile = Profile.GetProfile(cd.PlayerIdentity, cd.PlayerName);
            cg.Chat.SendChatMessage($"{profile.Name}, you have {profile.JBucks} J-bucks.");
        }

        static void Command_Vaccinate(CustomGame cg, CommandData cd, PlayerTracker playerTracker, List<Profile> vaccinated)
        {
            if (GameState != GameState.Ingame)
                return;

            Profile profile = Profile.GetProfile(cd.PlayerIdentity, cd.PlayerName);
            if (CustomGame.IsSlotRed(playerTracker.SlotFromPlayerIdentity(cd.PlayerIdentity)))
            {
                if (profile.Buy(cg, "vaccine", 3))
                    vaccinated.Add(profile);
            }
        }
    }

    enum GameState
    {
        Ingame,
        Setup
    }
}
