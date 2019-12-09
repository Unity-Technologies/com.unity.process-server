using System.Threading;
using UnityEngine;
using UnityEditor;
using Unity.Editor.ProcessServer;
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
        var testApp = Path.GetFullPath("Packages/com.unity.process-server/Tests/Helpers~/Helper.CommandLine.exe");

        var expectedResult = "result!";

        var process = new DotNetProcessTask(processServer.TaskManager, processServer.ProcessManager,
                testApp, "-d " + expectedResult.InQuotes());

        process.Finally((success, ex, ret) => {
            if (!success)
                Debug.LogException(ex);
            else
                Debug.Log($"done! got {ret}");
        }).Start();

    }
}
