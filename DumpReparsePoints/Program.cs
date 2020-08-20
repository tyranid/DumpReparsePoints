//    This file is part of DumpReparsePoints.
//    Copyright (C) James Forshaw 2020
//
//    DumpReparsePoints is free software: you can redistribute it and/or modify
//    it under the terms of the GNU General Public License as published by
//    the Free Software Foundation, either version 3 of the License, or
//    (at your option) any later version.
//
//    DumpReparsePoints is distributed in the hope that it will be useful,
//    but WITHOUT ANY WARRANTY; without even the implied warranty of
//    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//    GNU General Public License for more details.
//
//    You should have received a copy of the GNU General Public License
//    along with DumpReparsePoints.  If not, see <http://www.gnu.org/licenses/>.

//    Quick and dirty tool to dump all (unless filtered) Reparse Tags on an NTFS volume.
using NtApiDotNet;
using NtApiDotNet.Utilities.Text;
using System;
using System.Runtime.InteropServices;

namespace DumpReparsePoints
{
    [StructLayout(LayoutKind.Sequential)]
    struct FileReparseTagInformation
    {
        public long FileReferenceNumber;
        public ReparseTag Tag;
    }

    struct FileData
    {
        public string FileName;
        public ReparseBuffer Reparse;
    }

    class Program
    {
        static NtFile OpenReparseDirectory(string volume)
        {
            return NtFile.Open($@"\??\{volume}\$Extend\$Reparse:$R:$INDEX_ALLOCATION", null, FileAccessRights.GenericRead | FileAccessRights.Synchronize, 
                FileShareMode.Read, FileOpenOptions.OpenForBackupIntent | FileOpenOptions.SynchronousIoNonAlert);
        }

        static void EnablePrivileges()
        {
            using (var token = NtToken.OpenProcessToken())
            {
                token.SetPrivilege(TokenPrivilegeValue.SeBackupPrivilege, PrivilegeAttributes.Enabled);
                token.SetPrivilege(TokenPrivilegeValue.SeRestorePrivilege, PrivilegeAttributes.Enabled);
            }
        }

        static FileData GetFileData(NtFile volume, ReparseTag tag, long fileid)
        {
            using (var file = NtFile.OpenFileById(volume, fileid, FileAccessRights.ReadAttributes | FileAccessRights.Synchronize,
                FileShareMode.None, FileOpenOptions.OpenReparsePoint | FileOpenOptions.SynchronousIoNonAlert | FileOpenOptions.OpenForBackupIntent))
            {
                var filename = file.GetWin32PathName(NtApiDotNet.Win32.Win32PathNameFlags.None, false).GetResultOrDefault(fileid.ToString());
                ReparseBuffer reparse = new OpaqueReparseBuffer(tag, new byte[0]);
                try
                {
                    reparse = file.GetReparsePoint();
                }
                catch (NtException)
                {
                }
                return new FileData()
                {
                    FileName = filename,
                    Reparse = reparse
                };
            }
        }

        static void Main(string[] args)
        {
            try
            {
                if (args.Length < 1)
                {
                    return;
                }
                EnablePrivileges();
                using (var dir = OpenReparseDirectory(args[0]))
                {
                    using (var buffer = new SafeStructureInOutBuffer<FileReparseTagInformation>())
                    {
                        using (var io_status = new SafeIoStatusBuffer())
                        {
                            while (true)
                            {
                                NtStatus status = NtSystemCalls.NtQueryDirectoryFile(dir.Handle, SafeKernelObjectHandle.Null, IntPtr.Zero,
                                    IntPtr.Zero, io_status, buffer, buffer.Length, FileInformationClass.FileReparsePointInformation,
                                    true, null, false);
                                if (status == NtStatus.STATUS_NO_MORE_FILES)
                                {
                                    break;
                                }
                                if (status != NtStatus.STATUS_SUCCESS)
                                {
                                    throw new NtException(status);
                                }
                                var result = buffer.Result;
                                var filedata = GetFileData(dir, result.Tag, result.FileReferenceNumber);
                                Console.WriteLine("{0} {1}", filedata.FileName, filedata.Reparse.Tag);
                                if (filedata.Reparse is MountPointReparseBuffer mount_point)
                                {
                                    Console.WriteLine("Target: {0}", mount_point.SubstitutionName);
                                    Console.WriteLine("Print: {0}", mount_point.PrintName);
                                }
                                else if (filedata.Reparse is SymlinkReparseBuffer symlink)
                                {
                                    Console.WriteLine("Target: {0}", symlink.SubstitutionName);
                                    Console.WriteLine("Print: {0}", symlink.PrintName);
                                    Console.WriteLine("Flags: {0}", symlink.Flags);
                                }
                                else if (filedata.Reparse is ExecutionAliasReparseBuffer alias)
                                {
                                    Console.WriteLine("Target: {0}", alias.Target);
                                    Console.WriteLine("Package: {0}", alias.PackageName);
                                    Console.WriteLine("Entry Point: {0}", alias.EntryPoint);
                                    Console.WriteLine("AppType: {0}", alias.AppType);
                                }
                                else if (filedata.Reparse is OpaqueReparseBuffer opaque)
                                {
                                    HexDumpBuilder builder = new HexDumpBuilder(true, true, true, false, 0);
                                    builder.Append(opaque.Data);
                                    Console.WriteLine(builder);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
    }
}
