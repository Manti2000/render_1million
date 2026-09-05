# CLAUDE.md

## Project

Demo project for the GDD 2026 talk *"How to render 1 million objects in Unity without a hitch"*. One scene, five swappable backends benchmarked against each other, in order of increasing performance and decreasing flexibility:

1. MonoBehaviour per object (naive baseline)
2. Manager pattern (single update loop)
3. DOTS / ECS
4. GPU instanced rendering
5. GPU instanced indirect + compute shaders

