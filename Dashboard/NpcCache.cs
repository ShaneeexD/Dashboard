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
                            hpMax = (citizen is Actor a2) ? a2.maximumHealth : 0f
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


        private static string GetPhotoBase64(Citizen citizen)
        {
            try
            {
                if (citizen == null || citizen.evidenceEntry == null) return null;

                var keys = new Il2CppSystem.Collections.Generic.List<Evidence.DataKey>();
                keys.Add(Evidence.DataKey.photo);
                Texture2D tex = citizen.evidenceEntry.GetPhoto(keys);

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
                ModLogger.Warn($"GetPhotoBase64 for {citizen.humanID} failed: {ex.Message}");
            }
            return null;
        }
    }
}
