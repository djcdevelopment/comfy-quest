# Interim local packages

This directory holds the connected Creator/DM 0.9.10-local NuGet packages so the
repository can restore without reaching into another checkout. Historical
0.9.9-local packages remain byte-for-byte unchanged. Both versions are immutable.

It is deleted when the public Comfy.Quest.Contracts and Comfy.Quest.Studio 0.9.3
packages are published and every consumer is repinned to exact `[0.9.3]`.
