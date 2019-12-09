using System.Threading;
using UnityEngine;
using UnityEditor;
using Unity.Editor.ProcessServer;
using Unity.Editor.Tasks;
using System.IO;
using Unity.Editor.Tasks.Extensions;

public class RunAProcess : MonoBehaviour
{
    [MenuItem("Process Server/Run Process")]
    public static void Menu_RunProcess()
    {
        var processServer = ProcessServer.Get();

        var testApp = Path.GetFullPath("Packages/com.unity.process-server.tests/Helpers~/Helper.CommandLine.exe");

        var expectedResult = "result!";

        var process = new DotNetProcessTask(processServer.TaskManager, processServer.ProcessManager,
                testApp, "-d " + expectedResult.InQuotes());

        process.OnOutput += s => Debug.Log(s);

        process.FinallyInUI((success, ex, ret) => {
            Debug.Log("Done out-of-process");
            Debug.Log(ret);
        }).Start();

    }
}
