# CLAUDE.md

## Project

Demo project for the GDD 2026 talk *"How to render 1 million objects in Unity without a hitch"*. Five steps, each a self-contained scene plus scripts under `Assets/Steps/`, sharing a common bootstrap scene and infrastructure under `Assets/Common/`. Benchmarked against each other, in order of increasing performance. Steps 1–3 trade ergonomics and ecosystem while keeping per-object features cheap; from step 4 on every per-object feature costs real infrastructure and behaviour moves into shaders:

1. MonoBehaviour per object (naive baseline)
2. Manager pattern (single update loop)
3. DOTS / ECS
4. GPU instanced rendering
5. GPU instanced indirect + compute shaders
