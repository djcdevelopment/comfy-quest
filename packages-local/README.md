# Interim local packages

This directory holds the Phase-1 0.1.0-local NuGet rehearsal packages so the
extracted repository can restore without reaching into the Baseline checkout.

It is deleted when the public Comfy.Quest.Contracts and Comfy.Quest.Studio 0.1.0
packages are published and every consumer is repinned to exact `[0.1.0]`.
