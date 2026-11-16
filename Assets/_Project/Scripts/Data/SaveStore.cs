using System;
using System.IO;
using UnityEngine;

namespace HopOn.Data
{
    /// <summary>
    /// Keeps the <see cref="SavedGame"/> in one JSON file. A save is written to a temporary file that then
    /// takes the old one's place in a single step (a rename), so quitting or a crash in the middle of a write
    /// leaves the last whole save, never half of one. It never throws: a save that cannot be read starts the
    /// game fresh, and one that cannot be written is logged while play goes on.
    /// </summary>
    public sealed class SaveStore
    {
        private const string FileName = "save.json";

        private readonly string _path;
        private readonly string _tempPath;

        public SaveStore(string path)
        {
            _path = path;
            _tempPath = path + ".tmp";
        }

        /// <summary>The save in the app's data folder, which the OS keeps across runs and updates.</summary>
        public static SaveStore InDataFolder()
        {
            return new SaveStore(Path.Combine(Application.persistentDataPath, FileName));
        }

        /// <summary>The last save, or null when there is none or it cannot be used.</summary>
        public SavedGame Load()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return null;
                }
                var saved = JsonUtility.FromJson<SavedGame>(File.ReadAllText(_path));
                if (saved != null && saved.IsUsable)
                {
                    return saved;
                }
                Debug.LogWarning($"The save at {_path} is not one this version can use; starting fresh.");
            }
            catch (Exception e)     // unreadable file or broken JSON: starting fresh beats not starting
            {
                Debug.LogWarning($"Could not read the save at {_path}; starting fresh. {e.Message}");
            }
            return null;
        }

        public void Write(SavedGame saved)
        {
            try
            {
                File.WriteAllText(_tempPath, JsonUtility.ToJson(saved));
                if (File.Exists(_path))
                {
                    File.Replace(_tempPath, _path, null);
                }
                else
                {
                    File.Move(_tempPath, _path);
                }
            }
            catch (Exception e)     // a full disk must never stop the game: the last whole save stays
            {
                Debug.LogWarning($"Could not save to {_path}. {e.Message}");
            }
        }

#if UNITY_EDITOR
        // Play in the editor resumes from the save like the phone does; this starts over from level 1.
        [UnityEditor.MenuItem("Hop On/Delete Save (start from level 1)")]
        private static void DeleteSaveInEditor()
        {
            string path = Path.Combine(Application.persistentDataPath, FileName);
            File.Delete(path);
            File.Delete(path + ".tmp");
            Debug.Log("Save deleted: the game starts from level 1.");
        }
#endif
    }
}
