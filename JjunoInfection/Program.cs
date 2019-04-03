using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Deltin.CustomGameAutomation;

namespace JjunoInfection
{
    class Program
    {
        // Constants
        const int RoundCount = 5;
        const int AICount = 3;
        const int ZombieCount = 2;
        const int TestAICount = 4;

        static Profile[] LastZombies = new Profile[ZombieCount];
        static bool GameOver = false;
        static bool RoundOver = false;
        static List<int> AISlots;

        static void Main(string[] args)
        {
            Game();

            while (true)
            {
                Console.Write(">");
                string input = Console.ReadLine();
                string[] split = input.Split(' ');
                string firstWord = split[0].ToLower();

                switch (firstWord)
                {
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

                    default:
                        Console.WriteLine($"Unknown command \"{firstWord}\"");
                        goto case "help";
                }
            }
        }

        static void Game()
        {
            Task.Run(() =>
            {
                int currentRound = 0;

                CustomGame cg = new CustomGame();

                // Set the OnGameOver and OnRoundOver events.
                cg.OnGameOver += Cg_OnGameOver;
                cg.OnRoundOver += (sender, e) => Cg_OnRoundOver(cg, sender, e);

                // Setup commands
                cg.Commands.Listen = true;
                // $BALANCE command
                cg.Commands.ListenTo.Add(new ListenTo("$BALANCE", true, true, false, (cd) => Command_Balance(cg, cd)));

                // Join the match channel.
                cg.Chat.SwapChannel(Channel.Match);

                // Add filler AI
                AISlots = cg.AI.GetAISlots();
                int addAICount = AICount - AISlots.Count;
                if (addAICount > 0)
                {
                    cg.AI.AddAI(AIHero.Bastion, Difficulty.Easy, Team.BlueAndRed, addAICount);
                    cg.WaitForSlotUpdate();
                    Thread.Sleep(500);
                    AISlots = cg.AI.GetAISlots();
                }

                if (TestAICount > 0)
                {
                    cg.AI.AddAI(AIHero.McCree, Difficulty.Medium, Team.BlueAndRed, TestAICount);
                    cg.WaitForSlotUpdate();
                }

                SetupGame(cg);

                for (; ; )
                {

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

                    bool survivorsWin = true;

                    List<int> survivorSlots = cg.GetSlots(SlotFlags.Blue /*| SlotFlags.PlayersOnly*/).Where(slot => !AISlots.Contains(slot)).ToList();
                    if (survivorSlots.Count == 0 && !GameOver)
                    {
                        // All survivors are dead, zombies win.
                        GameOver = true;
                        survivorsWin = false;
                        cg.Chat.SendChatMessage("Survivors lose gg");
                    }

                    if (GameOver)
                    {

                        cg.SendServerToLobby();

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
                            foreach (int zombieSlot in zombieSlots)
                                Profile.GetProfileFromSlot(cg, zombieSlot)?.Award(cg, $"Won in {currentRound + 1} rounds.", roundBonus);
                        }

                        // Save player's J-bucks
                        Profile.Save();

                        cg.Chat.SendChatMessage("Be notified of the next Zombie game! Join our discord http://discord.gg/xTVeqm");

                        GameOver = false;
                        RoundOver = false;
                        currentRound = 0;

                        SetupGame(cg);
                    }
                    else if (RoundOver)
                    {
                        currentRound++;
                        cg.Chat.SendChatMessage($"Survivors need to win {RoundCount - currentRound} more rounds.");
                    }
                }
            });
        }

        static void SetupGame(CustomGame cg)
        {
            // Make everyone queueing for the game queue for blue
            List<int> nonNeutralQueue = cg.GetSlots(SlotFlags.RedQueue);
            foreach (int queueSlot in nonNeutralQueue)
                cg.Interact.SwapToBlue(queueSlot);

            // Swap AI in blue to red.
            List<int> validRedSlots = new List<int> { 6, 7, 8, 9, 10, 11 }.Where(vs => !AISlots.Contains(vs)).ToList();
            for (int i = 0; i < AISlots.Count && validRedSlots.Count > 0; i++)
                if (CustomGame.IsSlotBlue(AISlots[i]))
                {
                    cg.Interact.Move(AISlots[i], validRedSlots[0]);
                    AISlots[i] = validRedSlots[0];
                    validRedSlots.RemoveAt(0);
                }

            // Swap players in red to blue.
            bool updatedRed = false;
            foreach (int survivorInRed in cg.GetSlots(SlotFlags.Red).Where(slot => validRedSlots.Contains(slot)))
            {
                cg.Interact.SwapToBlue(survivorInRed);
                updatedRed = true;
            }

            if (updatedRed)
                cg.WaitForSlotUpdate();

            // Set starting zombies.
            Profile[] startingZombies = new Profile[ZombieCount];

            int currentZombieCount = cg./*GetCount*/GetSlots(SlotFlags.Red /*| SlotFlags.PlayersOnly*/).Where(slot => !AISlots.Contains(slot)).Count();
            List<int> survivorSlots = cg.GetSlots(SlotFlags.Blue /*| SlotFlags.PlayersOnly */).Where(slot => !AISlots.Contains(slot)).ToList();
            while (currentZombieCount < ZombieCount && validRedSlots.Count > 0 && survivorSlots.Count > 0)
            {
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
                    moveSlot = survivorSlots[0];

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

            cg.StartGame();

            List<string> startingZombiesNames = startingZombies.Select(sz => sz?.Name).ToList();
            startingZombiesNames.RemoveAll(name => name == null);
            if (startingZombiesNames.Count > 0)
                cg.Chat.SendChatMessage($"{Helpers.CommaSeperate(startingZombiesNames)} {(startingZombiesNames.Count == 1 ? "is" : "are")} the starting zombie{(startingZombiesNames.Count == 1 ? "" : "s")}!");
        }

        static void Cg_OnRoundOver(CustomGame cg, object sender, EventArgs e)
        {
            RoundOver = true;
        }

        static void Cg_OnGameOver(object sender, GameOverArgs e)
        {
            // Survivors won
            GameOver = true;

            CustomGame cg = (CustomGame)sender;
            cg.Chat.SendChatMessage("Survivors win gg");
        }

        static void Command_Balance(CustomGame cg, CommandData cd)
        {
            Profile profile = Profile.GetProfile(cd.PlayerIdentity, cd.PlayerName);
            cg.Chat.SendChatMessage($"{profile.Name}, you have {profile.JBucks} J-bucks.");
        }
    }
}
