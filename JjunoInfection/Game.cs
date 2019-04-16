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
    class Game
    {
        const int StartSwappingAfter = 30; // Seconds
        const int ZombieCount = 2;

        public static bool Initialized { get { lock (InitLock) return _init; } set { _init = value; } }
        public static Process UsingProcess;
        private static Random RandomMap = new Random();
        private static OWEvent OverwatchEvent = OWEvent.None;

        private static bool _init = false;
        private static object InitLock = new object();

        public GameState GameState = GameState.InitialSetup;
        public List<int> AISlots;
        public List<string> InviteToGame = new List<string>();
        private CancellationTokenSource CancelSource = new CancellationTokenSource();

        public int CurrentRound { get; private set; }
        // Discord !gameinfo data
        public int       GIWaitingCount  { get; private set; }
        public int       GISurvivorCount { get; private set; }
        public int       GIZombieCount   { get; private set; }
        public Stopwatch GIGameTime      { get; private set; } = new Stopwatch();
        public Map       GICurrentMap    { get; private set; }

        public Game(List<int> aiSlots = null)
        {
            AISlots = aiSlots;
        }

        /// <summary>
        /// Starts the game.
        /// </summary>
        /// <param name="createGame"></param>
        /// <param name="cancelToken"></param>
        /// <exception cref="OperationCanceledException">Game stopped.</exception>
        /// <exception cref="OverwatchStartFailedException">Overwatch failed to start.</exception>
        /// <exception cref="OverwatchClosedException">Overwatch closed.</exception>
        /// <exception cref="InitializedException">This was already initialized.</exception>
        /// <exception cref="MissingOverwatchProcessException">The Overwatch process is missing.</exception>
        public void Play()
        {
            CancellationToken cancelToken = CancelSource.Token;

            if (Initialized)
                throw new InitializedException();
            Initialized = true;

            try
            {
                CustomGame cg = null;

                try
                {
                    #region Game
                    #region Initial Setup

                    UsingProcess = CustomGame.GetOverwatchProcess();
                    bool createdCustomGame = false;
                    if (UsingProcess == null)
                    {
                        createdCustomGame = true;
                        try
                        {
                            UsingProcess = CustomGame.StartOverwatch(new OverwatchInfoAuto()
                            {
                                AutomaticallyCreateCustomGame = true,
                                CloseOverwatchProcessOnFailure = true,
                                BattlenetExecutableFilePath = Program.Config.BattlenetExecutable,
                                OverwatchSettingsFilePath = Program.Config.OverwatchSettingsFile
                            });
                        }
                        catch (System.IO.FileNotFoundException)
                        {
                            throw new OverwatchStartFailedException("Could not find vital file for starting Overwatch.");
                        }
                    }
                    cg = new CustomGame(new CustomGameBuilder() { OverwatchProcess = UsingProcess });

                    // Round variables
                    bool gameOver = false;
                    bool roundOver = false;
                    List<Tuple<int, bool>> vaccinated = new List<Tuple<int, bool>>();
                    Profile[] startingZombies = new Profile[ZombieCount];

                    cancelToken.ThrowIfCancellationRequested();

                    // Set the OnGameOver and OnRoundOver events.
                    cg.OnGameOver += (sender, e) => Cg_OnGameOver(ref gameOver, sender, e);
                    cg.OnRoundOver += (sender, e) => Cg_OnRoundOver(ref roundOver, sender, e);

                    // Setup commands
                    ListenTo ShopCommand = new ListenTo("$SHOP", listen: true, getNameAndProfile: false, checkIfFriend: false, callback: (cd) => Command_Shop(cg, cd));
                    ListenTo BalanceCommand = new ListenTo("$BALANCE", listen: true, getNameAndProfile: true, checkIfFriend: false, callback: (cd) => Command_Balance(cg, cd));
                    ListenTo VaccinateCommand = new ListenTo("$VACCINATE", listen: false, getNameAndProfile: true, checkIfFriend: false, callback: (cd) => Command_Vaccinate(cg, cd, vaccinated));
                    cg.Commands.Listen = true;
                    cg.Commands.ListenTo.Add(ShopCommand);
                    cg.Commands.ListenTo.Add(BalanceCommand);
                    cg.Commands.ListenTo.Add(VaccinateCommand);

                    OverwatchState state = cg.Reset();
                    if (state == OverwatchState.MainMenu)
                    {
                        cg.CreateCustomGame();
                        createdCustomGame = true;
                    }

                    // Join the match channel.
                    cg.Chat.SwapChannel(Channel.Match);

                    // Load the infection preset.
                    cg.Settings.LoadPreset(Program.Config.PresetName);

                    // Set join setting
                    cg.Settings.JoinSetting = Join.FriendsOnly;

                    cancelToken.ThrowIfCancellationRequested();

                    // Add AI and set game name/team names if the custom game was created.
                    if (createdCustomGame)
                    {
                        // Set Game Name
                        try
                        {
                            cg.Settings.SetGameName(Program.Config.GameName);
                        }
                        catch (Exception e) when (e is ArgumentException || e is ArgumentOutOfRangeException)
                        {
                            Console.WriteLine($"Error: {Program.Config.GameName} is an invalid game name.");
                        }
                        // Set Team Names
                        cg.Settings.SetTeamName(Team.Blue, Constants.TEAM_NAME_SURVIVORS);
                        cg.Settings.SetTeamName(Team.Red, Constants.TEAM_NAME_ZOMBIES);
                        // Add AI
                        cg.Interact.Move(0, 12);
                        cg.AI.AddAI(AIHero.Bastion, Difficulty.Easy, Team.BlueAndRed, 12 - Program.Config.PlayerCount);
                        cg.WaitForSlotUpdate();
                        AISlots = cg.GetSlots(SlotFlags.BlueAndRed);
                    }
                    // If the AI slots were not pre-filled, attempt to get them automatically. This can be inaccurate.
                    else if (AISlots == null)
                    {
                        AISlots = cg.GetSlots(SlotFlags.BlueAndRed | SlotFlags.AIOnly);
                        int addAICount = 12 - Program.Config.PlayerCount - AISlots.Count;
                        if (addAICount > 0)
                        {
                            cg.AI.AddAI(AIHero.Bastion, Difficulty.Easy, Team.BlueAndRed, addAICount);
                            cg.WaitForSlotUpdate();
                            AISlots = cg.GetSlots(SlotFlags.BlueAndRed | SlotFlags.AIOnly);
                        }
                    }

                    cancelToken.ThrowIfCancellationRequested();

                    SetupCompleted.Invoke(this, null);

                    // Set up the game
                    SetupGame(cg, ref startingZombies, cancelToken);

                    VaccinateCommand.Listen = true;
                    #endregion

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

                        // Invite players
                        if (InviteToGame.Count > 0)
                            cg.InvitePlayer(InviteToGame[0], Team.BlueAndRed);

                        // Use powerups
                        for (int i = vaccinated.Count - 1; i >= 0; i--)
                            // If the vaccine powerup is activated
                            if (vaccinated[i].Item2)
                            {
                                cg.Interact.SwapToBlue(vaccinated[i].Item1);
                                vaccinated.RemoveAt(i);
                            }

                        // Check if zombies win.
                        bool survivorsWin = true;
                        List<int> survivorSlots = cg.GetSlots(SlotFlags.Blue /*| SlotFlags.PlayersOnly*/).Where(slot => !AISlots.Contains(slot)).ToList();
                        if (survivorSlots.Count == 0 && !gameOver)
                        {
                            // All survivors are dead, zombies win.
                            gameOver = true;
                            survivorsWin = false;
                            cg.Chat.SendChatMessage(Constants.MESSAGE_SURVIVORS_LOSE);
                        }

                        // Update discord info
                        GISurvivorCount = survivorSlots.Count;
                        GIZombieCount = cg.GetSlots(SlotFlags.Red).Where(slot => !AISlots.Contains(slot)).Count();

                        if (gameOver)
                        {
                            GIGameTime.Stop();
                            GameState = GameState.Setup;
                            VaccinateCommand.Listen = false;
                            vaccinated = new List<Tuple<int, bool>>();

                            cg.SendServerToLobby();

                            cancelToken.ThrowIfCancellationRequested();

                            // Award jbucks
                            // Give every initial zombie .5 jbucks
                            foreach (Profile initialZombie in startingZombies)
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
                                switch (CurrentRound)
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
                                    //foreach (int zombieSlot in zombieSlots)
                                    //    Profile.GetProfileFromSlot(cg, zombieSlot)?.Award(cg, $"Won in {CurrentRound + 1} rounds.", roundBonus);
                                    foreach (Profile initialZombie in startingZombies)
                                        initialZombie?.Award(cg, $"Won in {CurrentRound + 1} rounds.", roundBonus);
                            }

                            // Save all player's J-bucks to the system.
                            Profile.Save();

                            cg.Chat.SendChatMessage(Constants.MESSAGE_DISCORD);

                            cancelToken.ThrowIfCancellationRequested();

                            gameOver = false;
                            roundOver = false;
                            CurrentRound = 0;

                            SetupGame(cg, ref startingZombies, cancelToken);

                            VaccinateCommand.Listen = true;
                        }
                        else if (roundOver)
                        {
                            // Indent current round.
                            CurrentRound++;

                            // Activate vaccine powerups
                            for (int i = 0; i < vaccinated.Count; i++)
                                vaccinated[i] = new Tuple<int, bool>(vaccinated[i].Item1, true);

                            cg.Chat.SendChatMessage($"Survivors need to win {Program.Config.RoundCount - CurrentRound} more rounds.");

                            // Wait for the black screen.
                            Thread.Sleep(StartSwappingAfter * 1000);

                            if (CurrentRound == Program.Config.RoundCount)
                                cg.Chat.SendChatMessage(Constants.MESSAGE_LAST_ROUND);

                            roundOver = false;
                        }
                    }
                    #endregion
                }
                catch (OperationCanceledException)
                {
                    CancelSource.Dispose();
                    CancelSource = new CancellationTokenSource();
                }
                finally
                {
                    if (cg != null)
                        cg.Dispose();
                    Profile.Save();
                    Initialized = false;
                    Program.Game = null;
                    Program.GameTask = null;
                }
            }
            catch (Exception) { throw; }
        }

        static void Cg_OnRoundOver(ref bool roundOver, object sender, EventArgs e)
        {
            Thread.Sleep(500);
            roundOver = true;
        }

        static void Cg_OnGameOver(ref bool gameOver, object sender, GameOverArgs e)
        {
            // Survivors won
            gameOver = true;

            CustomGame cg = (CustomGame)sender;
            cg.Chat.SendChatMessage("Survivors win gg");
        }

        void SetupGame(CustomGame cg, ref Profile[] startingZombies, CancellationToken cancelToken)
        {
            // Choose random map
            Map[] maps = Map.GetMapsInGamemode(Gamemode.Elimination, OverwatchEvent);
            Map nextMap = maps[RandomMap.Next(maps.Length - 1)];
            GICurrentMap = nextMap;
            cg.ToggleMap(Gamemode.Elimination, OverwatchEvent, ToggleAction.DisableAll, nextMap);

            GameState = GameState.Waiting;
            WaitForEnoughPlayers(cg, cancelToken);
            GameState = GameState.Setup;

            // Print then next map to the chat.
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
            Profile[] newStartingZombies = new Profile[ZombieCount];

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
                        bool wasLastZombie = startingZombies.Any(lz => lz == zombieProfile);
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
                newStartingZombies[currentZombieCount] = zombieProfile;

                // Remove the starting zombie from the survivor slots.
                survivorSlots.Remove(moveSlot);

                // Update the valid red slots.
                validRedSlots.RemoveAt(0);

                // Wait for the slots to update then get the new zombie count.
                currentZombieCount = cg./*GetCount*/GetSlots(SlotFlags.Red /*| SlotFlags.PlayersOnly*/).Where(slot => !AISlots.Contains(slot)).Count();
            }
            newStartingZombies = startingZombies;

            cancelToken.ThrowIfCancellationRequested();

            // Make everyone queueing for the game queue for red.
            List<int> nonRedQueue = cg.GetSlots(SlotFlags.BlueQueue | SlotFlags.NeutralQueue);
            foreach (int queueSlot in nonRedQueue)
                cg.Interact.SwapToRed(queueSlot);

            cancelToken.ThrowIfCancellationRequested();

            // Start the game.
            cg.StartGame();

            cancelToken.ThrowIfCancellationRequested();

            // Write the rules to the chat.
            cg.Chat.SendChatMessage(Constants.MESSAGE_RULES);

            // Write the names of the starting zombies to the chat.
            string[] startingZombieNames = startingZombies.Select(sz => sz?.Name).ToArray();
            for (int i = 0; i < startingZombieNames.Length; i++)
                if (startingZombieNames[i] == null)
                    startingZombieNames[i] = "<unknown>";

            if (startingZombieNames.Length > 0)
                cg.Chat.SendChatMessage($"{Helpers.CommaSeperate(startingZombieNames)} {(startingZombieNames.Length == 1 ? "is" : "are")} the starting zombie{(startingZombieNames.Length == 1 ? "" : "s")}!");

            GameState = GameState.Ingame;
            GIGameTime.Restart();
            GISurvivorCount = survivorSlots.Count;
            GIZombieCount = currentZombieCount;

            cancelToken.ThrowIfCancellationRequested();
            Thread.Sleep(StartSwappingAfter * 1000);
            cancelToken.ThrowIfCancellationRequested();
        }

        void Command_Shop(CustomGame cg, CommandData cd)
        {
            cg.Chat.SendChatMessage(Helpers.FormatMessage(
                "♦ Shop ♦", 
                "3 J-bucks: $VACCINATE <slot 1-6>"
                ));
        }

        void Command_Balance(CustomGame cg, CommandData cd)
        {
            Profile profile = Profile.GetProfile(cd.PlayerIdentity, cd.PlayerName);
            cg.Chat.SendChatMessage($"{profile.Name}, you have {profile.JBucks} J-bucks.");
        }

        void Command_Vaccinate(CustomGame cg, CommandData cd, List<Tuple<int, bool>> vaccinated)
        {
            if (GameState != GameState.Ingame)
                return;

            Profile profile = Profile.GetProfile(cd.PlayerIdentity, cd.PlayerName);
            int.TryParse(cd.Command.Split(' ').ElementAtOrDefault(1), out int slot);
            if (slot > 0 && CustomGame.IsSlotRed(slot + 5))
            {
                if (profile.Buy(cg, "vaccine", 3))
                    vaccinated.Add(new Tuple<int, bool>(slot + 5, false));
            }
            else
                cg.Chat.SendChatMessage("Syntax: $VACCINATE <slot 1-6> (Press L then choose the red slot number.)");
        }

        void WaitForEnoughPlayers(CustomGame cg, CancellationToken cancelToken)
        {
            int previousPlayerCount = 0;
            for (; ; )
            {
                cancelToken.ThrowIfCancellationRequested();
                Thread.Sleep(10);

                for (int i = InviteToGame.Count - 1; i >= 0; i--)
                {
                    cg.InvitePlayer(InviteToGame[i], Team.BlueAndRed);
                    InviteToGame.RemoveAt(i);
                }

                int playerCount = cg.GetSlots(SlotFlags.BlueAndRed | SlotFlags.IngameOnly).Where(slot => !AISlots.Contains(slot)).Count();

                if (previousPlayerCount < playerCount)
                {
                    int left = Program.Config.MinPlayers - playerCount;
                    if (left > 1)
                        cg.Chat.SendChatMessage($"Welcome! Waiting for {left} more players.");
                    if (left == 1)
                        cg.Chat.SendChatMessage($"Welcome! Waiting for 1 more player.");
                    if (left < 1)
                        cg.Chat.SendChatMessage($"Welcome! The game will be starting shortly...");
                }
                previousPlayerCount = playerCount;
                GIWaitingCount = playerCount;

                if (playerCount >= Program.Config.MinPlayers)
                    break;
            }
        }

        public void Stop()
        {
            CancelSource.Cancel();
        }

        public event EventHandler SetupCompleted;
    }

    enum GameState
    {
        InitialSetup,
        Setup,
        Waiting,
        Ingame,
    }
}
