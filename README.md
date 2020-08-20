# DumpReparsePoints
This is a simple tool to dump all the reparse points on an NTFS volume.

It uses the \$Extend\$Reparse directory which can then be queried using
NtQueryDirectoryFile and the FileReparsePointInformation info class to
enumerate all reparse points on the volume without actually recursively
interating through all files and directories.

Some filter drivers will actively remove their reparse tags so it's possible
that not everything will be visible but it'll identity standard tags such as
MOUNT_POINT and SYMLINK.