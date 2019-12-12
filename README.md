# About the Unity Process Server

The Unity process server lets you run arbitrary external processes that can survive domain reloads, streaming process output back to you via IPC with the help of the Unity.Ipc library. It can optionally monitor processes that it runs and restart them if they stop.

# How to build

## Build and Package a release build with a release version

`pack[.sh|cmd] -r -b -p -u`

## Build

`dotnet build -c [Debug|Release]` or `build[.sh|cmd] -[r|d]`

## Build and test a debug build

`dotnet test -c Debug` or `test[.sh|cmd] -b -d`

## Build and test a release build

`dotnet test` or `test[.sh|cmd] -b`

## Test an existing build

`dotnet test --no-build -c [Debug|Release]` or `test[.sh|cmd] -[r|d]`

## Package an existing build (nuget)

`dotnet pack --no-restore --no-build -c [Debug|Release]` or `pack[.sh|cmd] -[r|d]`

## Where are the built artifacts?

Nuget packages are in `build/nuget`. Npm packages are in `upc-ci~/packages`.
Binaries for each project are in `build/bin` for the main projects, `build/Samples/bin` for the samples, and `build/bin/tests` for the tests.

## How to bump the major or minor parts of the version

The `version.json` file in the root of the repo controls the version for all packages.
Set the major and/or minor number in it and **commit the change** so that the next build uses the new version.
The patch part of the version is the height of the commit tree since the last manual change of the `version.json`
file, so once you commit a change to the major or minor parts, the patch will reset back to 0.