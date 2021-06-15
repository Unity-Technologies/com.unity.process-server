# About the Unity Process Server

Process Server package lets you run external processes that can survive Unity domain reload. It can optionally monitor processes that it runs and restart them if they stop.

This package was created to allow version control packages to perform long-running operations without losing state due to Unity's C# domain being reloaded whenever game code is compiled. Having an RPC server per Unity instance that can run processes on behalf of editor extensions means that when the Unity editor reloads the managed domain, the RPC server is unaffected and the client can reconnect to it and continue operations, and the process server can ensure that processes are kept alive, if they need to be, while the domain is reloading.

## API

The process server doesn't know anything about the processes it's running, it just takes the executable name, command line arguments and working directory data and runs the process with that information. When you ask the server to prepare a process for execution, it returns an `RpcProcess` object that contains two process identifiers (the id assigned by the system, and the id assigned by the process server, the start information for the process (a serializable version of the `System.Diagnostics.ProcessStartInfo` object, and an options object with process server-specific options).

The `ProcessOptions` object includes additional options specific to the process server. Right now, it contains two options:

- `MonitorOptions`: an enum that controls whether the process server should restart a process if it exits without user intervention. If KeepAlive is set, the process will be kept alive until the user calls Stop on it or the process server shuts down.
- `useProjectPath`: the process server automatically adds the `-projectPath [path to unity project]` command line argument to the process.

Besides the numeric id that the OS assigns to the process, the process server also assigns a unique string identifier. This identifer can be one of the following:

- A GUID, for processes that are one-shots, i.e. not meant to be kept alive or be unique
- A string identifer hashed from the StartInfo values (process name, arguments, working dir, environment variables, etc), for processes that are to be kept alive and are therefore unique (one per Unity instance).

When the process server receives a request to run a KeepAlive process with a certain set of StartInfo values, it will only run the process once, and any subsequent request for the same set of StartInfo values will instead receive a replay of all the process events of the running process up to the point where `Detach` was called. From the client's perspective, there's no difference - the process ran and produced output.


### High level API


To run processes, you'll need to obtain a `IProcessServer` instance first.

```
using Unity.ProcessServer;
var processServer = ProcessServer.Get();
```

The `IProcessServer` interface provides several helper methods to easily run dotnet processes (executed natively on Windows and with Unity's Mono on other systems), mono processes (dotnet processes that will always be executed with Unity's mono), and native processes.

```
var process = processServer.NewDotNetProcess(
  executable: "path/to/myapp.exe", // the executable name or path+name. Back and forward slashes will work regardless of OS
  arguments: "argument".InQuotes(), // the arguments to the process (can be null). The InQuotes() extension method will make sure an argument is surrounded by quotes, if you need to.
  options: new ProcessOptions(MonitorOptions.KeepAlive), // optional process options. If you want the process server to keep your process alive if it exits, pass KeepAlive to it
  workingDir: "optional/path/to/working/directory", // the working directory. By default, it's the Unity project path.
  onStart: task => {}, // this is called when the process starts
  onOutput: (task, output) => {}, // you can handle the process output here, it gets called once per line
  onEnd: (task, result, success, exception) => {}, // when the process ends
  outputProcessor: new FirstNonNullOutputProcessor<string>(), //an output processor that can process the string output and filter it, as well as raise events for it
  affinity: TaskAffinity.None, // which scheduler will this task run. None is for the thread pool, Exclusive/Concurrent is for the writer/reader schedulers, UI is for the main thread. See the com.unity.editor.tasks package for more details on the threading model.
  token: CancellationToken.None // optionally pass a cancellation token in if you want to be able to cancel the task/process later on
);

process.Start(); // start the process. This is non-blocking, it will schedule the task in the scheduler set above.
```

If you want to run your process in the background once it's up and running, call `Detach` on the `IProcessTask` object once it's started or at some later point. `Detach` causes `IProcessTask` objects to complete as if the process had ended (though it hasn't), and tells the Process Server to stop processing the raw output directly (since there's no one to receive the output anymore). This saves memory on the server side, and allows process event replays (when reconnecting to running processes) to only replay the relevant events.

```
processServer.NewDotNetProcess("myprocess.exe", onStart: task => task.Detach()).Start();
```

The `NewDotNetProcess/NewMonoProcess/NewNativeProcess` APIs return `IProcessTask<string>` objects. `IProcessTask` object are a specialization of the `com.unity.editor.tasks` package `ITask` object that represent a process running on a background thread. For examples of what these objects are and can do, check out the `com.unity.editor.tasks` package [process tests](https://github.com/Unity-Technologies/com.unity.editor.tasks/blob/master/src/com.unity.editor.tasks/Tests/Editor/Unity.ProcessTests/ProcessTaskTests.cs)

If you need more flexibility than what `NewDotNetProcess/NewMonoProcess/NewNativeProcess` can provide - for instance, if you'd like your `IProcessTask` to return something other than the (optionally filtered) raw output, you can create and run `IProcessTask` objects directly. For instance, if you want to run a native process (non-dotnet, for instance), read its output until you receive a line with a number, wait until the process is finished, and do something with that number on the UI thread, you can do something like this:

```
new NativeProcessTask<int>(processServer.TaskManager, processServer.ProcessManager, "myprocess", "arg1 arg2",
        new FirstResultOutputProcessor<int>((string input, out int output, bool foundIt) => foundIt = int.TryParse(input, out output)))
    .ThenInUI(result => {
         // do something with the result on the UI thread
    })
    .Start();
```

If you want to use async/await instead

```
var result = await new NativeProcessTask<int>(processServer.TaskManager, processServer.ProcessManager, "myprocess", "arg1 arg2",
        new FirstResultOutputProcessor<int>((string input, out int output, bool foundIt) => foundIt = int.TryParse(input, out output)))
    .StartAsAsync();

// do something with the result on the UI thread
```

If you don't want to wait until the process is done, just until you get that number:

```
var processTask = new NativeProcessTask<int>(processServer.TaskManager, processServer.ProcessManager, "myprocess", "arg1 arg2",
        new FirstResultOutputProcessor<int>((string input, out int output, bool foundIt) => foundIt = int.TryParse(input, out output)));

// this gets called when the output process returns a value. since we know that it will only return one value, we can detach the process and let it finish by itself. We could also `Stop()` the process to stop it completely.
processTask.OnOutput += _ => processTask.Detach();

processTask..ThenInUI(result => {
     // do something with the result on the UI thread
  })
  .Start();
```

 If you're already using `IProcessTask` objects to run processes, you can just pass the `processServer.ProcessManager` to them instead of the ProcessManager that you would usually use, and they'll execute on the process server automatically without any other changes.

### Using the process server to run an rpc server

A useful common workflow is to have a server rpc process that handles a specific set of functionality - like running compilation jobs or git commands. Running an rpc server normally involves the following steps:

- Run the rpc server process
- Wait for some output that signals that the server is ready to receive requests. The server might dynamically allocate a port and print it on the console, for instance.
- Connect an rpc client
- Save the port information for later reconnection

The `RpcServerTask` encapsulates this logic to make it easier. To use it, implement an RPC server and client using the [com.unity.rpc](https://github.com/Unity-Technologies/com.unity.rpc) package, and then do the following:

```
var task = new RpcServerTask(processServer.TaskManager, processServer.ProcessManager, new ServerConfiguration(ServerDirectory))
            { Affinity = TaskAffinity.None };

task.RegisterRemoteTarget<IRPCInterfaceThatMyServerExposes>()
    .RegisterLocalTarget(instanceThatImplementsMyRpcClientInterfaces);

task.Then(rpcClient => {

      // we're connected, get the rpc proxy instance and call it on a background thread
      var remoteRpcInstance = rpcClient.GetRemoteTarget<IRPCInterfaceThatMyServerExposes>();
      remoteRpcInstance.DoSomething();

      // also probably save the rpcClient instance somewhere for later

  })
  .FinallyInUI((success, result) => {
      // regardless of failure or success of earlier tasks, this will always get called

  });


task.Start();
```

The default implementation of `RpcServerTask` waits for an output with the format `^Port:(?<port>\d+)`, as specified in the [PortOutputProcessor class](https://github.com/Unity-Technologies/com.unity.process-server/blob/master/src/com.unity.process-server/Editor/Client/PortOutputProcessor.cs).

Output processor instances take raw process output and optionally filter and convert it to other types, so you can pass your own regex to this class to process a different output into an it, or make your own output processor from scratch. You can find more output processors in the [com.unity.editor.tasks OutputProcessor folder](https://github.com/Unity-Technologies/com.unity.editor.tasks/tree/master/src/com.unity.editor.tasks/Editor/OutputProcessor) and in the [Git for Unity OutputProcessors folder](https://github.com/Unity-Technologies/Git-for-Unity/tree/master/src/com.unity.git.api/Api/OutputProcessors).


### Low level API

The `IProcessServer` object exposes two RPC interfaces, `IServer Server { get; }` for controlling the server, and `IProcessRunner ProcessRunner { get; }` for running processes.

#### Running a process

```
var processServer = ProcessServer.Get();
var rpcProcess = await processServer.ProcessRunner.Prepare("path/to/executable", "arguments here", "working directory or null for default", new ProcessOptions());
rpcProcess = await processServer.ProcessRunner.Run(rpcProcess);
Debug.Log($"Process {rpcProcess.Id} is running!");
```

If you already have a `System.Diagnostics.ProcessStartInfo` object and you just want to have it running via the process server, you can convert it to the serializable `ProcessInfo` object with

```
var processInfo = ProcessInfo.FromStartInfo(ProcessStartInfo startInfo);
```

and then prepare and run it on the process server with:

```
var processServer = ProcessServer.Get();
var rpcProcess = await processServer.ProcessRunner.Prepare(processInfo, new ProcessOptions());
rpcProcess = await processServer.ProcessRunner.Run(rpcProcess);
Debug.Log($"Process {rpcProcess.Id} is running!");
```

If you can't use the async/await C# keywords (maybe you can't change your method signature?), encapsulate these calls with the `TaskManager`:

```
var processServer = ProcessServer.Get();
processServer.TaskManager
    .WithAsync(async () => {
        var rpcProcess = await processServer.ProcessRunner.Prepare(processInfo, new ProcessOptions());
        rpcProcess = await processServer.ProcessRunner.Run(rpcProcess);
        return rpcProcess;
    }, TaskAffinity.None)
    .ThenInUI(rpcProcess => Debug.Log($"Process {rpcProcess.Id} is running!"))
    .Start();
```

## Security

There are no built-in authentication or authorization mechanisms. User must provide one if needed.

## How to build

Check [How to Build](https://raw.githubusercontent.com/Unity-Technologies/Git-for-Unity/master/BUILD.md) for all the build, packaging and versioning details.

### Release build

`build[.sh|cmd] -r`

### Release build and package

`pack[.sh|cmd] -r -b`

### Release build and test

`test[.sh|cmd] -r -b`


### Where are the build artifacts?

Packages sources are in `build/packages`.

Nuget packages are in `build/nuget`.

Packman (npm) packages are in `upm-ci~/packages`.

Binaries for each project are in `build/bin` for the main projects and `build/tests` for the tests.

### How to bump the major or minor parts of the version

The `version.json` file in the root of the repo controls the version for all packages.
Set the major and/or minor number in it and **commit the change** so that the next build uses the new version.
The patch part of the version is the height of the commit tree since the last manual change of the `version.json`
file, so once you commit a change to the major or minor parts, the patch will reset back to 0.
