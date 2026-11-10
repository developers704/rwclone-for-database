Valliani Data Sync — clean source

Solution: VallianiDataSync.sln
Projects: src\ (App, Web, Api, Core, Infrastructure, Sync)
Docs: docs\
Scripts: scripts\ , BUILD-DISTRIBUTE.bat, START-*.bat
Tests: tests\

Build desktop package:
  BUILD-DISTRIBUTE.bat

Build web package:
  scripts\pack-web-server.bat

Requires: .NET 8 SDK
