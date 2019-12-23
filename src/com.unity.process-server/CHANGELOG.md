# Changelog

## [VERSION] - DATE

- Bump com.unity.editor.tasks to 1.2.22 and com.unity.rpc to 1.0.21-preview
- Don't try to stop the server if there's no server
- Add a way of stopping remote processes manually
- Add an optional output processor argument to RpcServerTask
- Make sure the rpc process doesn't block the concurrent or exclusive affinities
- Monitor the process that spawned the server (i.e. Unity), and shutdown if it's gone

## [0.1.28-preview] - 2019-12-18

- Add ProcessOptions to RpcServerTask

## [0.1.27-preview] - 2019-12-17

- ILRepack StreamRPC DLLs into one bundle
- Fixes

## [0.1.0-preview] - 2019-12-12

- com.unity.process-server