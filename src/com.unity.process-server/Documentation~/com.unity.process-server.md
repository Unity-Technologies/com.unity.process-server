# About the Unity Process Server

The Unity process server lets you run arbitrary external processes that can survive domain reloads, streaming process output back to you via IPC with the help of the Unity.Ipc library. It can optionally monitor processes that it runs and restart them if they stop.