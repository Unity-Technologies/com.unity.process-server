using System;
using UnityEditor;
using UnityEditorInternal;
using System.Globalization;
using System.Runtime.Serialization;
using System.IO;
using UnityEngine;

namespace Unity.Editor.ProcessServer
{
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

    [Location("processserver/appsettings.yaml", LocationAttribute.Location.CacheFolder)]
    class ApplicationConfiguration : ScriptObjectSingleton<ApplicationConfiguration>, IProcessServerConfiguration
    {
        [SerializeField] private int port;

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
    }
}
