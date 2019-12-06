using System.Threading;
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

        var process = new ProcessTask<string>(processServer.TaskManager, processServer.ProcessManager.DefaultProcessEnvironment,
                "git", "log", outputProcessor: new SimpleOutputProcessor())
            .Configure(processServer.ProcessManager);

        process.OnOutput += s => Debug.Log(s);
        process.FinallyInUI((success, ex, ret) => {
            Debug.Log("Done out-of-process");
            Debug.Log(ret);
        }).Start();

        new ActionTask(processServer.TaskManager, () => {
            new ManualResetEventSlim().Wait(200); 
            process.Stop();
        }).Start();
    }
}
