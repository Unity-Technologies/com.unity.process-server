using System;

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
#else
using Unity.ProcessServer.EditorStubs;
#endif

namespace Unity.ProcessServer
{
    using Internal.IO;

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

    class ApplicationConfiguration : ScriptableSingleton<ApplicationConfiguration>, IProcessServerConfiguration
    {
        [SerializeField] private int port;
        [SerializeField] private string executablePath;

        private const string ProcessExecutable = "Packages/com.unity.process-server/Server~/Unity.ProcessServer.exe";

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
