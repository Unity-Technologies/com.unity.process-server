using System;

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
#endif

namespace Unity.Editor.ProcessServer
{
    using Internal.IO;
#if !UNITY_EDITOR

    using EditorStubs;
    namespace EditorStubs
    {
        public class SerializeFieldAttribute : Attribute
        {

        }

        public class ScriptableSingleton<T>
            where T : class, new()
        {
            private static T _instance;
            public static T instance => _instance ?? (_instance = new T());
            public static T Instance => instance;

            protected void Save(bool flush)
            { }
        }

        public static class Application
        {
            public static string productName { get; } = "DefaultApplication";
            public static string unityVersion { get; set; } = "2019.2.1f1";
            public static string projectPath { get; set; }
        }

        public static class EditorApplication
        {
            public static string applicationPath { get; set; }
            public static string applicationContentsPath { get; set; }
        }
    }
#endif

    sealed class ApplicationCache : ScriptableSingleton<ApplicationCache>
    {
        [SerializeField] private bool firstRun = true;
        [SerializeField] public string instanceIdString;
        [SerializeField] private bool initialized = false;
        [NonSerialized] private Guid? instanceId;
        [NonSerialized] private bool? firstRunValue;

        public bool FirstRun
        {
            get
            {
                EnsureFirstRun();
                return firstRunValue.Value;
            }
        }

        private void EnsureFirstRun()
        {
            if (!firstRunValue.HasValue)
            {
                firstRunValue = firstRun;
            }
        }

        public Guid InstanceId
        {
            get
            {
                EnsureInstanceId();
                return instanceId.Value;
            }
        }

        private void EnsureInstanceId()
        {
            if (instanceId.HasValue)
            {
                return;
            }

            if (string.IsNullOrEmpty(instanceIdString))
            {
                instanceId = Guid.NewGuid();
                instanceIdString = instanceId.ToString();
            }
            else
            {
                instanceId = new Guid(instanceIdString);
            }
        }

        public bool Initialized
        {
            get { return initialized; }
            set
            {
                initialized = value;
                if (initialized && firstRun)
                {
                    firstRun = false;
                }
                Save(true);
            }
        }
    }

#if UNITY_EDITOR
    [Location("processserver/appsettings.yaml", LocationAttribute.Location.TempFolder)]
#endif
    class ApplicationConfiguration :
#if UNITY_EDITOR
        ScriptObjectSingleton<ApplicationConfiguration>
#else
        ScriptableSingleton<ApplicationConfiguration>
#endif
        , IProcessServerConfiguration
    {
        [SerializeField] private int port;
        [SerializeField] private string executablePath;

        private const string ProcessExecutable = "Packages/com.unity.process-server/Server~/processserver.exe";

        public int Port
        {
            get => port;
            set
            {
                if (port != value)
                {
                    port = value;
                    Save(true);
                }
            }
        }

        public string ExecutablePath
        {
            get
            {
                if (string.IsNullOrEmpty(executablePath))
                {
                    ExecutablePath = System.IO.Path.GetFullPath(ProcessExecutable);
                }
                return executablePath;
            }
            set
            {
                if (executablePath != value)
                {
                    executablePath = value;
                    Save(true);
                }
            }
        }
    }
}
