# (IMPORTANT) 

If building using CPP_ALLOCATION preprocessor command:

    Project requires ECSDLL.dll to be built and copied into the same folder as ECSCORE.dll (or Game_Example.dll if running it directly) !!

If not, the project will build as normal.

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
