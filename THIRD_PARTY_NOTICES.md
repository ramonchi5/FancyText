# Third-Party Notices

## LiveSplit

FancyText is a custom component for LiveSplit and references LiveSplit APIs.

Some implementation patterns were developed with reference to LiveSplit and existing LiveSplit components, including component structure, layout settings integration, rendering behavior, and component factory registration.

LiveSplit is licensed under the MIT License.

Copyright (c) 2013 Christopher Serr and Sergey Papushin.

Official repository: https://github.com/LiveSplit/LiveSplit  
License: https://github.com/LiveSplit/LiveSplit/blob/master/LICENSE

## LiveSplit.Core.dll

This project may reference `LiveSplit.Core.dll` for building against LiveSplit.

If `LiveSplit.Core.dll` is included in this repository or distributed with this project, it is covered by LiveSplit's MIT License notice above.

## UpdateManager.dll

This project may reference `UpdateManager.dll` during development/building.

Before publicly redistributing `UpdateManager.dll`, verify the exact source and license of the DLL included in this repository. If it is the GPL-licensed UpdateManager library, additional GPL obligations may apply.

If the DLL is not required to build or distribute FancyText, prefer removing it from the public repository and referencing the copy included with LiveSplit or your local build environment instead.

## Microsoft .NET Framework Reference Assemblies

This project may use Microsoft .NET Framework Reference Assemblies for building against .NET Framework 4.8.

These should ideally be restored through NuGet/build tooling rather than manually redistributed in this repository.
