using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;
using Deltin.CustomGameAutomation;

namespace JjunoInfection
{
    [Serializable]
    class Profile
    {
        public readonly string Name;
        public PlayerIdentity Identifier { get; private set; }
        public decimal JBucks { get; private set; }

        // Determines if a player's profile was updated by getting/selling j-bucks or their identifier being updated.
        // If it was updated, their saved file on the system gets updated when Save() is called.
        [NonSerialized]
        private bool Updated = false;
        // The path to the profile file.
        [NonSerialized]
        private string ProfilePath = null;

        private Profile(PlayerIdentity identifier, string name)
        {
            // Create a profile.
            Identifier = identifier;
            Name = Helpers.FirstLetterToUpperCase(name);
            // Get the filepath this profile will be saved to.
            ProfilePath = GetNewProfileFile();
            // Mark the profile as updated so it gets saved when Save() is called. 
            Updated = true;
            // Add the profile to the list of profiles.
            lock (ProfileListAccessLock)
                _profileList.Add(this);
        }

        public void UpdateIdentifier(PlayerIdentity newIdentifier)
        {
            // Dispose the memory of the old identier then replace it with the new identifier.
            Identifier.Dispose();
            Identifier = newIdentifier;
            // Mark the profile as updated so it gets saved when Save() is called. 
            Updated = true;
        }

        public bool Buy(CustomGame cg, string product, decimal price)
        {
            if (JBucks >= price)
            {
                JBucks -= price;
                cg.Chat.SendChatMessage($"{Name} bought {product} for {price}! Remaining funds: {JBucks} J-bucks.");
                Updated = true;
                return true;
            }
            return false;
        }

        public void Award(CustomGame cg, string reason, decimal amount)
        {
            cg.Chat.SendChatMessage($"{Name} +{amount} J-bucks: {reason}");
            JBucks += amount;
            Updated = true;
        }

        // Static
        private static readonly string ProfilesDirectory = AppDomain.CurrentDomain.BaseDirectory + "/Profiles/"; // The folder on the file system storing the profiles.
        private static List<Profile> _profileList = Load(); // The list of profiles.
        private static object ProfileListAccessLock = new object(); // The lock for the profile list for thread safety.
        public static IReadOnlyList<Profile> ProfileList { get { lock (ProfileListAccessLock) return _profileList.AsReadOnly(); } } // Read-only list of the profiles for public usage.

        public static Profile GetProfileFromSlot(CustomGame cg, int slot)
        {
            cg.GetPlayerIdentityAndName(slot, out PlayerIdentity identifier, out string name);

            if (identifier == null)
                return null;

            return GetProfile(identifier, name);
        }

        public static Profile GetProfile(PlayerIdentity identifier, string backupName)
        {
            // Gets a profile from an identifier and creates one if it is not found.

            lock (ProfileListAccessLock)
                foreach (Profile profile in _profileList)
                    if (Identity.Compare(identifier, profile.Identifier))
                    {
                        profile.UpdateIdentifier(identifier);
                        return profile;
                    }
            return new Profile(identifier, backupName);
        }

        public static void Save()
        {
            // Saves every profile that was updated.

            BinaryFormatter formatter = new BinaryFormatter();

            lock (ProfileListAccessLock)
                foreach (Profile profile in _profileList)
                    if (profile.Updated)
                    {
                        using (FileStream fs = File.Create(profile.ProfilePath))
                            formatter.Serialize(fs, profile);
                        profile.Updated = false;
                    }
        }

        private static List<Profile> Load()
        {
            // Loads all profiles.

            if (!Directory.Exists(ProfilesDirectory))
                Directory.CreateDirectory(ProfilesDirectory);

            List<Profile> profiles = new List<Profile>();

            string[] files = Directory.GetFiles(ProfilesDirectory);
            foreach(string file in files)
                using (FileStream fs = new FileStream(file, FileMode.Open))
                {
                    BinaryFormatter formatter = new BinaryFormatter();

                    try
                    {
                        Profile loadedProfile = (Profile)formatter.Deserialize(fs);

                        loadedProfile.ProfilePath = file;

                        profiles.Add(loadedProfile);
                    }
                    catch (SerializationException) { }
                }

            return profiles;
        }

        private static string GetNewProfileFile()
        {
            int profileIndex = 0;
            string newProfileFile;
            while (File.Exists(newProfileFile = ProfilesDirectory + profileIndex + ".jjprofile")) profileIndex++;
            return newProfileFile;
        }
    }
}
