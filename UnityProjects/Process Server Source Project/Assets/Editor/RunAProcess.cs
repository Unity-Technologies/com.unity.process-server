using System.Threading;
using UnityEngine;
using UnityEditor;
using Unity.ProcessServer;
using Unity.Editor.Tasks;
using System.IO;
using Unity.Editor.Tasks.Extensions;

public class RunAProcess : MonoBehaviour
{
    [MenuItem("Process Server/Stop process server")]
    public static void Menu_Stop()
    {
        ProcessServer.Stop();
    }

    [MenuItem("Process Server/Run Process")]
    public static void Menu_RunProcess()
    {
        var processServer = ProcessServer.Get();


        Debug.Log("Running out-of-process");

        var expectedResult = "result!";

        var testApp = Path.GetFullPath("Packages/com.unity.process-server/Tests/Helpers~/Helper.CommandLine.exe");

        processServer.NewDotNetProcess(testApp, "-d " + expectedResult.InQuotes(),
                         onEnd: (_, result, success, exception) => {

                             Debug.Log(result);
                         },
                         outputProcessor: new FirstNonNullLineOutputProcessor<string>())
                     .Start();
    }
}
