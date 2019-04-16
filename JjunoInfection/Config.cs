using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.IO;
using Deltin.CustomGameAutomation;

namespace JjunoInfection
{
    class Config
    {
        public static GeneralConfig ParseConfig()
        {
            string configLocation = AppDomain.CurrentDomain.BaseDirectory + "/config.xml";

            if (!File.Exists(configLocation))
                return new GeneralConfig();

            XDocument document = XDocument.Load(configLocation);

            var config = new GeneralConfig();

            ParseString(ref config.GameName, document, "gameName");
            ParseString(ref config.PresetName, document, "presetName");
            ParseString(ref config.BattlenetExecutable, document, "battlenetExecutable");
            ParseString(ref config.OverwatchSettingsFile, document, "overwatchSettingsFile");
            config.OverwatchSettingsFile = config.OverwatchSettingsFile?.Replace("{username}", Environment.UserName);
            ParseInt(ref config.PlayerCount, document, "playerCount", 3, 12);
            ParseInt(ref config.RoundCount, document, "roundCount", 1, 9);
            ParseInt(ref config.MinPlayers, document, "minPlayers", 3, 12);

            return config;
        }

        public static DiscordConfig ParseDiscordConfig()
        {
            string configLocation = AppDomain.CurrentDomain.BaseDirectory + "/discord.xml";

            if (!File.Exists(configLocation))
                return new DiscordConfig();

            XDocument document = XDocument.Load(configLocation);

            var config = new DiscordConfig()
            {
                Token = GetElement(document, "token")?.Attribute("value")?.Value
            };

            ParseStrings(ref config.Admins, document, name: "admins");

            Console.WriteLine($"Bot admins: {string.Join(", ", config.Admins)}");

            return config;
        }

        private static XElement GetElement(XDocument document, string name)
        {
            return document.Element("config")?.Element(name);
        }
        private static string GetValue(XDocument document, string name)
        {
            return GetElement(document, name)?.Value;
        }

        private static void ParseString(ref string value, XDocument document, string name)
        {
            string elementValue = GetValue(document, name);
            if (elementValue != null)
                value = elementValue;
        }

        private static void ParseStrings(ref string[] value, XDocument document, string name)
        {
            value = GetValue(document, name)?.Split(',').Select(n => n.ToLower().Trim()).ToArray() ?? value;
        }

        private static void ParseInt(ref int value, XDocument document, string name, int min, int max)
        {
            if (int.TryParse(GetValue(document, name), out int newvalue) && newvalue >= min && newvalue <= max)
                value = newvalue;
        }
    }

    class GeneralConfig
    {
        public string GameName = "JJuno Infection";

        public string PresetName = "Jjuno Infection";
        public int PlayerCount = 8;
        public int MinPlayers = 4;
        public int RoundCount = 5;

        public string BattlenetExecutable = @"C:\Program Files (x86)\Blizzard App\Battle.net.exe";
        public string OverwatchSettingsFile = @"C:\Users\{username}\Documents\Overwatch\Settings\Settings_v0.ini".Replace("{username}", Environment.UserName);
    }

    class DiscordConfig
    {
        public string Token = null;
        public string[] Admins;
    }
}
