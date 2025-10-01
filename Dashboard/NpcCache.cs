using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Dashboard
{
    public static class NpcCache
    {
        public sealed class NpcInfo
        {
            public int id;
            public string name;
            public string surname;
            public string photoBase64;
            public float hpCurrent;
            public float hpMax;
            public bool isDead;
            public bool isKo;
            public float koRemainingSeconds;
            public float koTotalSeconds;
            public string employer;
            public string jobTitle;
            public string salary;
            public int workAddressId;
            public string homeAddress;
            public int homeAddressId;
            // Additional profile fields
            public int ageYears;
            public string ageGroup;
            public string gender;
            public int heightCm;
            public string heightCategory;
            public string build;
            public string hairType;
            public string hairColor;
            public string eyes;
            public int shoeSize;
            public bool glasses;
            public bool facialHair;
            public string dateOfBirth;
            public string telephoneNumber;
            public string livesInBuilding;
            public string livesOnFloor;
            public string worksInBuilding;
            public string workHours;
            public string handwriting;
        }

        // Helpers
        private static string SafeToString(Func<string> getter)
        {
            try { var s = getter(); return s ?? string.Empty; } catch { return string.Empty; }
        }

        private static int SafeRound(Func<float> getter)
        {
            try { return Mathf.RoundToInt(getter()); } catch { return 0; }
        }

        private static T SafeGet<T>(Func<T> getter)
        {
            try { return getter(); } catch { return default; }
        }

        private static int SafeGetAge(Citizen c)
        {
            try { return Convert.ToInt32(c.GetAge()); } catch { return 0; }
        }

        private static bool HasTrait(Citizen c, string traitName)
        {
            try
            {
                if (c == null || c.characterTraits == null) return false;
                for (int i = 0; i < c.characterTraits.Count; i++)
                {
                    var t = c.characterTraits[i];
                    if (t != null && string.Equals(t.name, traitName, StringComparison.Ordinal)) return true;
                }
            }
            catch { }
            return false;
        }

        private static string GetHomeTelephone(Citizen c)
        {
            try
            {
                var home = c?.home;
                if (home != null && home.telephones != null && home.telephones.Count > 0)
                {
                    var tel = home.telephones[0];
                    if (tel == null) return string.Empty;
                    // Try precomputed string
                    try { if (!string.IsNullOrEmpty(tel.numberString)) return tel.numberString; } catch { }
                    // Try to load/compute the number string
                    try { tel.LoadTelephoneNumber(); if (!string.IsNullOrEmpty(tel.numberString)) return tel.numberString; } catch { }
                    // Fallback to formatting raw number if available
                    try { if (tel.number > 0) return Toolbox.Instance.GetTelephoneNumberString(tel.number); } catch { }
                    return string.Empty;
                }
            }
            catch { }
            return string.Empty;
        }

        private static string GetBuildingDisplayName(Citizen c)
        {
            try
            {
                var home = c?.home;
                if (home == null) return string.Empty;
                // Prefer specific address name first
                try { var n = home.thisAsAddress?.name; if (!string.IsNullOrEmpty(n)) return n; } catch { }
                // Then address preset name
                try { var n = home.thisAsAddress?.addressPreset?.presetName; if (!string.IsNullOrEmpty(n)) return n; } catch { }
                // Then building object name
                try { var n = home.building?.name; if (!string.IsNullOrEmpty(n)) return n; } catch { }
                // Finally building preset name
                try { var n = home.building?.preset?.name; if (!string.IsNullOrEmpty(n)) return n; } catch { }
            }
            catch { }
            return string.Empty;
        }

        private static string GetFloorDisplayName(Citizen c)
        {
            try
            {
                var f = c?.home?.floor;
                if (f == null) return string.Empty;
                try { var n = f.floorName; if (!string.IsNullOrEmpty(n)) return n; } catch { }
                try { return $"Floor {f.floor}"; } catch { }
            }
            catch { }
            return string.Empty;
        }

        private static readonly object _lock = new object();
        private static List<NpcInfo> _npcs = new List<NpcInfo>();
        private static string _photoCacheDir;
        public static bool Ready { get; private set; }
        
        static NpcCache()
        {
            try
            {
                // Create a unique temp directory for this session
                string baseDir = Path.Combine(Path.GetTempPath(), "SOD_Dashboard");
                if (!Directory.Exists(baseDir)) Directory.CreateDirectory(baseDir);
                _photoCacheDir = Path.Combine(baseDir, "photos_" + DateTime.Now.Ticks);
                if (!Directory.Exists(_photoCacheDir)) Directory.CreateDirectory(_photoCacheDir);
                ModLogger.Info($"NPC photo cache directory: {_photoCacheDir}");
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Failed to create photo cache directory: {ex.Message}");
                _photoCacheDir = null;
            }
        }

        public static IReadOnlyList<NpcInfo> Snapshot()
        {
            lock (_lock)
            {
                return _npcs.ToArray();
            }
        }

        public static void ClearPhotoCache()
        {
            // This is now a no-op since photos are part of NpcInfo, but kept for API compatibility
            // The actual clearing happens when _npcs list is rebuilt.
        }

        public static void RebuildFromGame()
        {
            try
            {
                var list = new List<NpcInfo>(1024);
                if (CityData.Instance != null && CityData.Instance.citizenDirectory != null)
                {
                    foreach (Citizen citizen in CityData.Instance.citizenDirectory)
                    {
                        if (citizen == null) continue;
                        var info = new NpcInfo
                        {
                            id = citizen.humanID,
                            name = citizen.GetCitizenName(),
                            surname = citizen.GetSurName(),
                            photoBase64 = GetPhotoBase64(citizen),
                            hpCurrent = (citizen is Actor a1) ? a1.currentHealth : 0f,
                            hpMax = (citizen is Actor a2) ? a2.maximumHealth : 0f,
                            isDead = (citizen is Actor a3) ? a3.isDead : false,
                            isKo = false,
                            koRemainingSeconds = 0f,
                            koTotalSeconds = 0f,
                            employer = citizen.job?.employer?.name?.ToString() ?? "",
                            jobTitle = citizen.job?.name?.ToString() ?? "",
                            salary = citizen.job?.salaryString?.ToString() ?? "",
                            workAddressId = citizen.job?.employer?.address?.id ?? -1,
                            homeAddress = citizen.home?.thisAsAddress?.name?.ToString() ?? "",
                            homeAddressId = citizen.home?.id ?? -1,
                            // Profile fields (best-effort null-safe)
                            ageYears = SafeGetAge(citizen),
                            ageGroup = SafeToString(() => citizen.GetAgeGroup().ToString()),
                            gender = SafeToString(() => citizen.gender.ToString()),
                            heightCm = SafeRound(() => citizen.descriptors.heightCM),
                            heightCategory = SafeToString(() => citizen.descriptors.height.ToString()),
                            build = SafeToString(() => citizen.descriptors.build.ToString()),
                            hairType = SafeToString(() => citizen.descriptors.hairType.ToString()),
                            hairColor = SafeToString(() => citizen.descriptors.hairColourCategory.ToString()),
                            eyes = SafeToString(() => citizen.descriptors.eyeColour.ToString()),
                            shoeSize = SafeGet(() => citizen.descriptors.shoeSize),
                            glasses = HasTrait(citizen, "Affliction-ShortSighted") || HasTrait(citizen, "Affliction-FarSighted"),
                            facialHair = HasTrait(citizen, "Quirk-FacialHair"),
                            dateOfBirth = SafeToString(() => citizen.birthday.ToString()),
                            telephoneNumber = GetHomeTelephone(citizen),
                            livesInBuilding = GetBuildingDisplayName(citizen),
                            livesOnFloor = GetFloorDisplayName(citizen),
                            worksInBuilding = string.Empty,
                            workHours = SafeToString(() => citizen.job?.GetWorkingHoursString()),
                            handwriting = SafeToString(() => citizen.handwriting?.fontAsset?.name)
                        };
                        list.Add(info);
                    }
                }

                lock (_lock)
                {
                    _npcs = list;
                    Ready = true;
                }

                ModLogger.Info($"NPC cache built: {_npcs.Count} citizens");
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Failed to build NPC cache: {ex}");
            }
        }

        public static void UpdateHealth(int id, float current, float max)
        {
            lock (_lock)
            {
                for (int i = 0; i < _npcs.Count; i++)
                {
                    if (_npcs[i].id == id)
                    {
                        _npcs[i].hpCurrent = current;
                        _npcs[i].hpMax = max;
                        // Do not infer death from health; KO can be 0 HP without being dead.
                        break;
                    }
                }
            }
        }

        public static void UpdateDeath(int id, bool dead)
        {
            lock (_lock)
            {
                for (int i = 0; i < _npcs.Count; i++)
                {
                    if (_npcs[i].id == id)
                    {
                        _npcs[i].isDead = dead;
                        if (dead)
                        {
                            _npcs[i].isKo = false;
                            _npcs[i].koRemainingSeconds = 0f;
                            _npcs[i].koTotalSeconds = 0f;
                        }
                        break;
                    }
                }
            }
        }

        public static void UpdateKo(int id, bool isKo, float totalSeconds, float remainingSeconds)
        {
            lock (_lock)
            {
                for (int i = 0; i < _npcs.Count; i++)
                {
                    if (_npcs[i].id == id)
                    {
                        _npcs[i].isKo = isKo;
                        _npcs[i].koTotalSeconds = Math.Max(0f, totalSeconds);
                        _npcs[i].koRemainingSeconds = Math.Max(0f, remainingSeconds);
                        break;
                    }
                }
            }
        }

        public static void UpdateKoTick(int id, float remainingSeconds)
        {
            lock (_lock)
            {
                for (int i = 0; i < _npcs.Count; i++)
                {
                    if (_npcs[i].id == id)
                    {
                        _npcs[i].koRemainingSeconds = Math.Max(0f, remainingSeconds);
                        break;
                    }
                }
            }
        }

        public static bool TryGetCitizen(int id, out Citizen citizen)
        {
            citizen = null;
            try
            {
                Human h;
                if (CityData.Instance != null && CityData.Instance.GetHuman(id, out h, includePlayer: false) && h is Citizen c)
                {
                    citizen = c;
                    return true;
                }
                // Fallback: iterate directory (some IDs may not resolve via GetHuman early on)
                if (CityData.Instance != null && CityData.Instance.citizenDirectory != null)
                {
                    foreach (Citizen cz in CityData.Instance.citizenDirectory)
                    {
                        if (cz != null && cz.humanID == id)
                        {
                            citizen = cz;
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"TryGetCitizen failed for {id}: {ex.Message}");
            }
            return false;
        }


        public static string GetPhotoBase64(Human human)
        {
            try
            {
                if (human == null || human.evidenceEntry == null) return null;

                var keys = new Il2CppSystem.Collections.Generic.List<Evidence.DataKey>();
                keys.Add(Evidence.DataKey.photo);
                Texture2D tex = human.evidenceEntry.GetPhoto(keys);

                if (tex != null)
                {
                    // Force texture to be readable
                    var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
                    Graphics.Blit(tex, rt);
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    var readableTex = new Texture2D(tex.width, tex.height);
                    readableTex.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                    readableTex.Apply();
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(rt);

                    byte[] pngBytes = ImageConversion.EncodeToPNG(readableTex);
                    UnityEngine.Object.Destroy(readableTex);

                    if (pngBytes != null && pngBytes.Length > 0)
                    {
                        return $"data:image/png;base64,{Convert.ToBase64String(pngBytes)}";
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"GetPhotoBase64 for {human.humanID} failed: {ex.Message}");
            }
            return null;
        }
    }
}
