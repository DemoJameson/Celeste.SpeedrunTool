# DeepCloner (vendored)

This directory contains a vendored copy of the DeepCloner library.

- Upstream: https://github.com/DemoJameson/DeepCloner (fork of https://github.com/force-net/DeepCloner)
- Branch/commit that was checked out as the git submodule: `LozensBranch` @ `860d733` ("migrate to .net 7")
- License: MIT, see [LICENSE](./LICENSE)

## Why vendored instead of a git submodule

The mod previously referenced DeepCloner through a git submodule. That made builds
depend on a second repository, required an extra `git submodule update` step, and
tied the mod to a branch tip that was not published on the upstream fork. The
sources are now vendored so that:

- a plain `git clone` + `dotnet build` works;
- patches to the cloner are reviewable in the same PR as the code using them;
- the compiler can inline/optimize the cloner together with the mod.

Only the library sources (`DeepCloner.cs`, `DeepClonerExtensions.cs`, `Helpers/*`)
are vendored. The upstream test project and packaging files are intentionally left
out. The `Properties` folder (AssemblyInfo) is omitted because the mod produces its
own assembly attributes.

## Local modifications

The vendored snapshot includes the modifications that were uncommitted in the
original submodule working tree (a performance-oriented pass over the clone
generators). They are part of the baseline commit
`refactor: vendor DeepCloner sources, drop the git submodule` and are kept under
source control from that point on.
