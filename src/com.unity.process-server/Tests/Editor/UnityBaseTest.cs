using System.Runtime.CompilerServices;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace BaseTests
{
    using System;
    using Unity.Editor.ProcessServer.Internal.IO;

    // Unity does not support async/await tests, but it does
    // have a special type of test with a [CustomUnityTest] attribute
    // which mimicks a coroutine in EditMode. This attribute is
    // defined here so the tests can be compiled without
    // referencing Unity, and nunit on the command line
    // outside of Unity can execute the tests. Basically I don't
    // want to keep two copies of all the tests.
    public class CustomUnityTestAttribute : UnityTestAttribute
    { }


    public partial class BaseTest
    {
        internal SPath ServerDirectory => "Packages/com.unity.process-server/Server~".ToSPath().Resolve();

        internal SPath? testApp;

        internal SPath TestApp
        {
            get
            {
                if (!testApp.HasValue)
                {
                    testApp = "Packages/com.unity.process-server/Tests/Helpers~/Helper.CommandLine.exe".ToSPath().Resolve();
                    if (!testApp.Value.FileExists())
                    {
                        testApp = "Packages/com.unity.process-server.tests/Helpers~/Helper.CommandLine.exe".ToSPath().Resolve();
                        if (!testApp.Value.FileExists())
                        {
                            testApp = "Packages/com.unity.process-server.tests/Tests/Helpers~/Helper.CommandLine.exe".ToSPath().Resolve();
                            if (!testApp.Value.FileExists())
                            {
                                Debug.LogException(new InvalidOperationException(
                                    "Test helper binaries are missing. Build the UnityTools.sln solution once with `dotnet build` in order to set up the tests."));
                                testApp = SPath.Default;
                            }
                        }
                    }
                }
                return testApp.Value;
            }
        }
    }
}
