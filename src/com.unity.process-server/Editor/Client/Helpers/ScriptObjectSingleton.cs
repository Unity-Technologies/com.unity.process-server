using System.Linq;
using UnityEngine;
using UnityEditorInternal;
using System.IO;
using System;

namespace Unity.Editor.ProcessServer
{

    [AttributeUsage(AttributeTargets.Class)]
    sealed class LocationAttribute : Attribute
    {
        public enum Location { PreferencesFolder, ProjectFolder, CacheFolder, TempFolder }

        private string relativePath;
        private Location location;

        private string filePath;
        public string FilePath
        {
            get
            {
                if (filePath != null) return filePath;

                if (relativePath[0] == '/')
                    relativePath = relativePath.Substring(1);

                if (location == Location.PreferencesFolder)
                    filePath = InternalEditorUtility.unityPreferencesFolder + "/" + relativePath;
                else if (location == Location.CacheFolder)
                    filePath = Path.GetFullPath("Cache/" + relativePath);
                else if (location == Location.TempFolder)
                    filePath = Path.GetFullPath("Temp/" + relativePath);
                return filePath;
            }
        }

        public LocationAttribute(string relativePath, Location location)
        {
            Guard.ArgumentNotNullOrWhiteSpace(relativePath, "relativePath");
            this.relativePath = relativePath;
            this.location = location;
        }
    }

    class ScriptObjectSingleton<T> : ScriptableObject where T : ScriptableObject
    {
        private static T instance;
        public static T Instance
        {
            get
            {
                if (instance == null)
                    CreateAndLoad();
                return instance;
            }
        }

        protected ScriptObjectSingleton()
        {
            if (instance != null)
            {
                Debug.LogError("Singleton already exists!");
            }
            else
            {
                instance = this as T;
                System.Diagnostics.Debug.Assert(instance != null);
            }
        }

        private static void CreateAndLoad()
        {
            System.Diagnostics.Debug.Assert(instance == null);

            string filePath = GetFilePath();
            if (!string.IsNullOrEmpty(filePath))
            {
                InternalEditorUtility.LoadSerializedFileAndForget(filePath);
            }

            if (instance == null)
            {
                var inst = CreateInstance<T>() as ScriptObjectSingleton<T>;
                inst.hideFlags = HideFlags.HideAndDontSave;
            }

            System.Diagnostics.Debug.Assert(instance != null);
        }

        protected virtual void Save(bool saveAsText)
        {
            if (instance == null)
            {
                Debug.LogError("Cannot save singleton, no instance!");
                return;
            }

            string locationFilePath = GetFilePath();
            if (locationFilePath != null)
            {
                string folderPath = Path.GetDirectoryName(locationFilePath);
                if (!Directory.Exists(folderPath))
                    Directory.CreateDirectory(folderPath);

                InternalEditorUtility.SaveToSerializedFileAndForget(new[] { instance }, locationFilePath, saveAsText);
            }
        }

        private static string GetFilePath()
        {
            var attr = typeof(T).GetCustomAttributes(true)
                                .Select(t => t as LocationAttribute)
                                .FirstOrDefault(t => t != null);

            if (attr == null)
                return null;
            return attr.FilePath;
        }
    }
}
