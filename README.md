## (IMPORTANT) 

The host machine running the engine must either:

    1. Have the DotNet 3.1 runtime natively installed at (C:/Program Files/dotnet)
    or
    2. Include the hostfxr.dll and "shared" folder (filled with all dotNet 3.1 libraries) in the main engine's directory (same folder as .exe)
    
    DotNet (installer or binaries): https://dotnet.microsoft.com/en-us/download/dotnet/3.1

## SFML.Net - Simple and Fast Multimedia Library for .Net

[![Build Status](https://travis-ci.org/SFML/SFML.Net.svg?branch=master)](https://travis-ci.org/SFML/SFML.Net)

[SFML](https://www.sfml-dev.org) is a simple, fast, cross-platform and object-oriented multimedia API. It provides access to windowing,
graphics, audio and network.

It is originally written in C++, and this project is its official binding for .Net languages (C#, VB, ...).

## Authors

* Laurent Gomila - main developer (laurent@sfml-dev.org)
* Zachariah Brown - active maintainer (contact@zbrown.net)

## Download

You can get the latest official release on [NuGet](https://www.nuget.org/packages/SFML.Net/) or on [the
SFML website](https://www.sfml-dev.org/download/sfml.net).
You can also get the current development version from the [git repository](https://github.com/SFML/SFML.Net).

## Dependencies

The NuGet package of SFML.Net comes with all dependencies, including native CSFML
and SFML libraries for most platforms.

For unsupported platforms or non-NuGet sources, you must have a copy of CSFML. CSFML can be compiled [from
source](https://github.com/SFML/CSFML/) or downloaded from [the official release
page](https://www.sfml-dev.org/download/csfml/). Also note that since CSFML depends on
the main SFML project you also need all SFML runtime dependencies.
