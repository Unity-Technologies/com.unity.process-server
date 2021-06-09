# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/) and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [VERSION] - DATE

### Added
- Added a way of stopping remote processes manually.
- Added optional output processor argument to RpcServerTask.
- Started monitoring process that spawned the server (i.e. Unity). Shutdown if it's gone.

### Changed
- Updated com.unity.editor.tasks and com.unity.rpc dependencies to newer versions.

### Fixed
- Fixed server shutdown when there are no running processes.
- Fixed RPC process so it doesn't block concurrent or exclusive affinities.

## [0.1.28-preview] - 2019-12-18

### Added
- Added ProcessOptions to RpcServerTask.

## [0.1.27-preview] - 2019-12-17

### Changed
- Merged StreamRPC assemblies into one bundle.

## [0.1.0-preview] - 2019-12-12

### This is the first release of *Unity Package Process Server*.

Process Server package lets you run external processes that can survive Unity domain reload. It can optionally monitor processes that it runs and restart them if they stop.