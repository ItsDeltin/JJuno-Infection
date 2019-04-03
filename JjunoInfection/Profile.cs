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

        [NonSerialized]
        private bool Updated = false;
        [NonSerialized]
        private string ProfilePath = null;

        private Profile(PlayerIdentity identifier, string name)
        {
            Identifier = identifier;
            Name = Helpers.FirstLetterToUpperCase(name);
            Updated = true;
            ProfilePath = GetNewProfileFile();
            _profileList.Add(this);
        }

        public void UpdateIdentifier(PlayerIdentity newIdentifier)
        {
            Identifier.Dispose();
            Identifier = newIdentifier;
            Updated = true;
        }

        public bool Buy(decimal price)
        {
            if (JBucks >= price)
            {
                JBucks -= price;
                Updated = true;
                return true;
            }
            return false;
        }

        // Static
        private static readonly string ProfilesDirectory = AppDomain.CurrentDomain.BaseDirectory + "/Profiles/";
        private static List<Profile> _profileList = Load();
        public static IReadOnlyList<Profile> ProfileList { get { return _profileList.AsReadOnly(); } }

        public static Profile GetProfileFromSlot(CustomGame cg, int slot)
        {
            cg.GetPlayerIdentityAndName(slot, out PlayerIdentity identifier, out string name);

            if (identifier == null)
                return null;

            return GetProfileFromIdentity(identifier) ?? new Profile(identifier, name);
        }

        private static Profile GetProfileFromIdentity(PlayerIdentity identifier)
        {
            foreach (Profile profile in _profileList)
                if (Identity.Compare(identifier, profile.Identifier))
                {
                    profile.UpdateIdentifier(identifier);
                    return profile;
                }
            return null;
        }

        public static void Save()
        {
            BinaryFormatter formatter = new BinaryFormatter();

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
            List<Profile> profiles = new List<Profile>();

            string[] files = Directory.GetFiles(ProfilesDirectory);
            foreach(string file in files)
                using (FileStream fs = new FileStream(file, FileMode.Open))
                {
                    BinaryFormatter formatter = new BinaryFormatter();

                    try
                    {
                        Profile loadedProfile = (Profile)formatter.Deserialize(fs);

#warning remove this after testing.
                        if (loadedProfile.Updated)
                            Console.WriteLine("Error: loaded profile has updated to true.");

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

        public void Award(CustomGame cg, string reason, decimal amount)
        {
            cg.Chat.SendChatMessage($"{Name} +{amount} J-Bucks: {reason}");
            AddJBucks(amount);
        }

        private void AddJBucks(decimal amount)
        {
            JBucks += amount;
            Updated = true;
        }
    }
}
