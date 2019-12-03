using UnityEngine;
using UnityEditor;
using Unity.Editor.ProcessServer;
using Unity.Editor.Tasks;

public class RunAProcess : MonoBehaviour
{
    [MenuItem("Process Server/Run Process")]
    public static void Menu_RunProcess()
    {
        var processServer = ProcessServer.Get();
        processServer.Connect();

        var process = new ProcessTask<string>(processServer.TaskManager, processServer.ProcessManager.DefaultProcessEnvironment, "git", "log", outputProcessor: new SimpleOutputProcessor())
            .Configure(processServer.ProcessManager, workingDirectory: "d:/code/unity/unity");

        //process.OnOutput += s => Debug.Log(s);

        process.Start().FinallyInUI((success, ex, ret) => {
            Debug.Log("Done in-process");
        });

        process = new ProcessTask<string>(processServer.TaskManager, processServer.ProcessManager.DefaultProcessEnvironment, "git", "log", outputProcessor: new SimpleOutputProcessor())
            .Configure(processServer, workingDirectory: "d:/code/unity/unity");

        process.OnOutput += s => Debug.Log(s);
        process.Start().FinallyInUI((success, ex, ret) => {
            Debug.Log("Done out-of-process");
        });

    }
}
