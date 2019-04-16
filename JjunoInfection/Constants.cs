using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JjunoInfection
{
    static class Constants
    {
        public const string ERROR_NOT_INITIALIZED = "Error: Bot is not running, type !start to start the bot.";

        public const string TEAM_NAME_SURVIVORS = "Survivors";
        public const string TEAM_NAME_ZOMBIES = "Zombies";

        public const string MESSAGE_SURVIVORS_LOSE = "Survivors lose gg";
        public const string MESSAGE_DISCORD = "Be notified of the next Zombie game! Join our discord http://discord.gg/xTVeqm";
        public const string MESSAGE_LAST_ROUND = "Last round, good luck!";

        public const string MESSAGE_RULES = "You start as a Survivor. The infection starts with 2 Zombies. Zombies infect via melee/abilities. Survivors win if at least one remains uninfected for 5 rounds. GL (Dev. by JJuno & Friends).";    }
}
