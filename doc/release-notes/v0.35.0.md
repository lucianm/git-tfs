# git-tfs-lfs - Initial Community Fork with Full LFS Support

Community-maintained fork of git-tfs with full Git LFS awareness and updated dependencies.

## Full Git LFS support

Implemented complete Git LFS awareness using the Git filter-process protocol.

LFS files are now handled correctly directly from the initial TFVC clone, including:

* automatic smudge/clean operations,
* transparent pointer-file handling,
* compatibility with standard git-lfs,
* support during fetch and checkout operations.

## Improvements

* Updated LibGit2Sharp to v0.31
* Updated StructureMap to v4.7.1
* Updated xUnit tooling and test infrastructure
* Refreshed various dependencies for security and compatibility
* Added support for relative paths in .git redirection files

## Breaking Changes

* **Dropped Visual Studio 2015 support** - VS2015 project removed from solution
* **Dropped Visual Studio 2017 support** - VS2017 project removed from solution
* Minimum supported Visual Studio versions are now VS2019 and VS2022

## Bug Fixes (from upstream)

* fix: read/write of `description` in bare repos ( #1487 by bramborman )
* Fix handling of renamed branches for clone/fetch ( #1493 by @dh2i-sam )

## Notes

This fork maintains full backward compatibility with git-tfs repositories while adding comprehensive LFS support. The LFS filter implementation is based on the Git pkt-line protocol and integrates directly with the local git-lfs installation for all filter operations.
