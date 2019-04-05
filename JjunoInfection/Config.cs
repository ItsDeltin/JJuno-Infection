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
    public class Config
    {
        public static Config ParseConfig()
        {
            string configLocation = AppDomain.CurrentDomain.BaseDirectory + "/config.xml";

            if (!File.Exists(configLocation))
            {
                Log("Could not find config file, using defaults.");
                return new Config();
            }

            XDocument document = XDocument.Load(configLocation);

            return new Config()
            {
                PresetName = ParseString(document, "presetName", "Jjuno Infection"),
                PlayerCount = ParseInt(document, "playerCount", 3, 12, 8),
                RoundCount = ParseInt(document, "roundCount", 1, 9, 5),

                BattlenetExecutable = ParseString(document, "battlenetExecutable", @"C:\Program Files (x86)\Blizzard App\Battle.net.exe"),
                OverwatchSettingsFile = ParseString(document, "overwatchSettingsFile", @"C:\Users\{username}\Documents\Overwatch\Settings\Settings_v0.ini"),
            };
        }

        private static void Log(string text)
        {
            Console.WriteLine("[Config] " + text);
        }

        private static string ParseString(XDocument document, string name, params string[] validValues)
        {
            string elementValue = document.Element("config")?.Element(name)?.Value?.ToLower();
            if (elementValue == null || !validValues.Contains(elementValue))
            {
                Log($"{name} is not {string.Join(", ", validValues)}. Using {validValues[0]} by default.");
                return validValues[0];
            }
            return elementValue;
        }

        private static string ParseString(XDocument document, string name, string @default)
        {
            string elementValue = document.Element("config")?.Element(name)?.Value?.ToLower();
            if (elementValue == null)
            {
                Log($"Could not get {name}. Using {@default} by default.");
                return @default;
            }
            return elementValue;
        }

        private static T ParseString<T>(XDocument document, string name, T @default) where T : struct
        {
            T value;
            if (!Enum.TryParse(document.Element("config")?.Element(name)?.Value, true, out value))
            {
                Log($"Could not get {name}. Using {@default} by default.");
                value = @default;
            }
            return value;
        }

        private static int ParseInt(XDocument document, string name, int min, int max, int @default)
        {
            int value;
            if (int.TryParse(document.Element("config")?.Element(name)?.Value, out value))
            {
                if (value < min || value > max)
                {
                    Log($"{name} ({value}) is less than {min} or greater than {max}. Using {@default} by default.");
                    value = @default;
                }
            }
            else
            {
                Log($"Could not get {name}. Using {@default} by default.");
                value = @default;
            }
            return value;
        }

        private static bool ParseBool(XDocument document, string name)
        {
            return ParseString(document, name, "false", "true") == "true";
        }

        private static bool Exists(XDocument document, string name)
        {
            return document.Element("config")?.Element(name) != null;
        }

        public string PresetName = "Jjuno Infection";
        public int PlayerCount = 8;
        public int RoundCount = 5;

        public string BattlenetExecutable = @"C:\Program Files (x86)\Blizzard App\Battle.net.exe";
        public string OverwatchSettingsFile = @"C:\Users\{username}\Documents\Overwatch\Settings\Settings_v0.ini";
    }
}
