using System;
using System.Collections.Generic;
using System.Linq;

namespace Dashboard
{
    public static class AddressCache
    {
        public sealed class AddressInfo
        {
            public int id;
            public string name;
            public string buildingName;
            public string floor;
            public int floorNumber;
            public string addressPreset;
            public bool isResidence;
            public List<ResidentInfo> residents;
            public string designStyle;
            public int roomCount;
        }

        public sealed class ResidentInfo
        {
            public int id;
            public string name;
            public string surname;
            public string photoBase64;
        }

        private static readonly object _lock = new object();
        private static List<AddressInfo> _addresses = new List<AddressInfo>();
        public static bool Ready { get; private set; }

        public static void RebuildFromGame()
        {
            try
            {
                var list = new List<AddressInfo>();
                
                if (CityData.Instance == null)
                {
                    ModLogger.Warn("AddressCache: CityData.Instance is null");
                    return;
                }

                int totalAddresses = 0;
                int residences = 0;
                int residencesWithInhabitants = 0;
                int totalResidents = 0;

                // Iterate through all addresses
                foreach (var addr in CityData.Instance.addressDirectory)
                {
                    if (addr == null) continue;
                    totalAddresses++;

                    var info = new AddressInfo
                    {
                        id = addr.id,
                        name = addr.name ?? string.Empty,
                        buildingName = SafeToString(() => addr.building?.name),
                        floor = SafeToString(() => addr.floor?.floorName),
                        floorNumber = addr.floor?.floor ?? -1,
                        addressPreset = SafeToString(() => addr.addressPreset?.presetName),
                        isResidence = addr.residence != null,
                        residents = new List<ResidentInfo>(),
                        designStyle = SafeToString(() => addr.designStyle?.name),
                        roomCount = addr.rooms?.Count ?? 0
                    };

                    if (info.isResidence) residences++;

                    // Get residents from address.inhabitants
                    try
                    {
                        if (addr.inhabitants != null && addr.inhabitants.Count > 0)
                        {
                            residencesWithInhabitants++;
                            int humanCount = 0;
                            int citizenCount = 0;
                            int nonCitizenCount = 0;
                            
                            foreach (var human in addr.inhabitants)
                            {
                                if (human == null) continue;
                                humanCount++;
                                
                                // Try direct cast to Citizen
                                Citizen citizen = human as Citizen;
                                if (citizen != null)
                                {
                                    citizenCount++;
                                    var resident = new ResidentInfo
                                    {
                                        id = citizen.humanID,
                                        name = citizen.GetCitizenName() ?? string.Empty,
                                        surname = citizen.GetSurName() ?? string.Empty,
                                        photoBase64 = NpcCache.GetPhotoBase64(citizen)
                                    };
                                    info.residents.Add(resident);
                                    totalResidents++;
                                }
                                else
                                {
                                    nonCitizenCount++;
                                    // Log what type it actually is
                                    string typeName = human?.GetType()?.Name ?? "null";
                                    if (nonCitizenCount <= 3) // Only log first 3 to avoid spam
                                    {
                                        ModLogger.Info($"Address {addr.name} ({addr.id}): Non-Citizen inhabitant type: {typeName}");
                                    }
                                }
                            }
                            
                            if (humanCount > 0 && citizenCount == 0)
                            {
                                ModLogger.Warn($"Address {addr.name} ({addr.id}): {humanCount} inhabitants but 0 Citizens (types logged above)");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Warn($"Failed to get residents for address {addr.id}: {ex.Message}");
                    }

                    list.Add(info);
                }

                ModLogger.Info($"AddressCache rebuilt: {totalAddresses} addresses, {residences} residences, {residencesWithInhabitants} with inhabitants, {totalResidents} total residents");

                lock (_lock)
                {
                    _addresses = list;
                    Ready = true;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"AddressCache rebuild error: {ex.Message}");
            }
        }

        public static List<AddressInfo> Snapshot()
        {
            lock (_lock)
            {
                return new List<AddressInfo>(_addresses);
            }
        }

        public static AddressInfo GetById(int id)
        {
            lock (_lock)
            {
                return _addresses.FirstOrDefault(a => a.id == id);
            }
        }

        // Helpers
        private static string SafeToString(Func<string> getter)
        {
            try { var s = getter(); return s ?? string.Empty; } catch { return string.Empty; }
        }

        private static T SafeGet<T>(Func<T> getter, T defaultValue)
        {
            try { return getter(); } catch { return defaultValue; }
        }
    }
}
