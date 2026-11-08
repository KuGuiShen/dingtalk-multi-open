using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace DingTalkMultiCore
{
    internal static class Native
    {
        internal const int SystemHandleInformation = 16;
        internal const int ObjectNameInformation = 1;
        internal const int ObjectTypeInformation = 2;

        internal const uint STATUS_INFO_LENGTH_MISMATCH = 0xC0000004;
        internal const uint DUPLICATE_CLOSE_SOURCE = 0x00000001;
        internal const uint DUPLICATE_SAME_ACCESS = 0x00000002;

        internal const uint PROCESS_DUP_HANDLE = 0x0040;
        internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        internal const uint TOKEN_QUERY = 0x0008;
        internal const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        internal const uint SE_PRIVILEGE_ENABLED = 0x00000002;

        internal const uint FILE_READ_ATTRIBUTES = 0x0080;
        internal const uint FILE_SHARE_READ = 0x00000001;
        internal const uint FILE_SHARE_WRITE = 0x00000002;
        internal const uint FILE_SHARE_DELETE = 0x00000004;
        internal const uint OPEN_EXISTING = 3;
        internal const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

        [DllImport("ntdll.dll")]
        internal static extern int NtQuerySystemInformation(
            int infoClass, IntPtr buffer, int length, out int returnLength);

        [DllImport("ntdll.dll")]
        internal static extern int NtQueryObject(
            IntPtr handle, int infoClass, IntPtr buffer, int length, out int returnLength);

        [DllImport("ntdll.dll")]
        internal static extern int NtDuplicateObject(
            IntPtr sourceProcessHandle,
            IntPtr sourceHandle,
            IntPtr targetProcessHandle,
            out IntPtr targetHandle,
            uint desiredAccess,
            uint handleAttributes,
            uint options);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr OpenProcess(
            uint desiredAccess, bool inheritHandle, uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool CloseHandle(IntPtr handle);

        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool OpenProcessToken(
            IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool LookupPrivilegeValue(
            string systemName, string name, out long luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool AdjustTokenPrivileges(
            IntPtr tokenHandle,
            bool disableAllPrivileges,
            ref TokenPrivileges newState,
            int bufferLength,
            IntPtr previousState,
            IntPtr returnLength);

        [StructLayout(LayoutKind.Sequential)]
        internal struct TokenPrivileges
        {
            public uint PrivilegeCount;
            public long Luid;
            public uint Attributes;
        }
    }

    internal sealed class MutexHandleInfo
    {
        public uint ProcessId;
        public ushort HandleValue;
        public string Name;
    }

    public enum LaunchResult
    {
        Ok,
        AlreadyRunning,
        LauncherNotFound,
        PermissionDenied,
        Timeout,
        DingTalkNotStarted
    }

    public sealed class ProfileInfo
    {
        public string Name;
        public string DataPath;
        public bool Running;

        public ProfileInfo()
        {
        }

        public ProfileInfo(string name, string dataPath)
        {
            Name = name;
            DataPath = dataPath;
        }
    }

    public static class DingTalkEngine
    {
        private const string MutexGuidPart = "239B7D43-86D5-4E5C-ADE6-CEC42155B475";
        private const string DefaultLauncherPath = @"D:\DingDing\DingtalkLauncher.exe";
        private const string ProcessName = "DingTalk";
        private const int LaunchTimeoutSeconds = 180;

        private static readonly object ConfigLock = new object();

        public static string GetDefaultDataPath()
        {
            string appData = Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "DingTalk");
        }

        public static string GetConfigDirectory()
        {
            string appData = Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "DingTalkMultiOpen");
        }

        public static string GetConfigFilePath()
        {
            return Path.Combine(GetConfigDirectory(), "accounts.txt");
        }

        public static string GetRedirectFilePath()
        {
            return Path.Combine(GetDefaultDataPath(), "redirectAppData.dat");
        }

        public static List<ProfileInfo> LoadProfiles()
        {
            string configFile = GetConfigFilePath();
            lock (ConfigLock)
            {
                if (!File.Exists(configFile))
                {
                    List<ProfileInfo> seeds = CreateSeedProfiles();
                    SaveProfiles(seeds);
                    return seeds;
                }

                List<ProfileInfo> result = new List<ProfileInfo>();
                string[] lines = File.ReadAllLines(configFile, Encoding.UTF8);
                foreach (string rawLine in lines)
                {
                    string line = (rawLine ?? string.Empty).Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    int tab = line.IndexOf('\t');
                    if (tab <= 0 || tab >= line.Length - 1)
                    {
                        continue;
                    }

                    ProfileInfo profile = new ProfileInfo();
                    profile.Name = line.Substring(0, tab).Trim();
                    profile.DataPath = line.Substring(tab + 1).Trim();
                    if (profile.Name.Length > 0 && profile.DataPath.Length > 0)
                    {
                        result.Add(profile);
                    }
                }

                return result;
            }
        }

        private static List<ProfileInfo> CreateSeedProfiles()
        {
            List<ProfileInfo> seeds = new List<ProfileInfo>();
            seeds.Add(new ProfileInfo("主账号", GetDefaultDataPath()));
            seeds.Add(new ProfileInfo("账号2", GetDefaultSecondDataPath()));
            return seeds;
        }

        public static string GetDefaultSecondDataPath()
        {
            string appData = Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "DingTalk2");
        }

        public static void SaveProfiles(List<ProfileInfo> profiles)
        {
            string configFile = GetConfigFilePath();
            lock (ConfigLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(configFile));
                StringBuilder builder = new StringBuilder();
                builder.AppendLine("# 钉钉多开账号配置：每行 = 账号名称<TAB>数据目录");
                foreach (ProfileInfo profile in profiles)
                {
                    builder.Append(profile.Name);
                    builder.Append('\t');
                    builder.AppendLine(profile.DataPath);
                }
                File.WriteAllText(configFile, builder.ToString(), new UTF8Encoding(false));
            }
        }

        public static string BuildNewProfilePath(List<ProfileInfo> profiles)
        {
            string appData = Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData);
            int index = 3;
            while (true)
            {
                string candidate = Path.Combine(appData, "DingTalk" + index);
                bool used = Directory.Exists(candidate);
                if (!used)
                {
                    foreach (ProfileInfo profile in profiles)
                    {
                        if (SamePath(candidate, profile.DataPath))
                        {
                            used = true;
                            break;
                        }
                    }
                }
                if (!used)
                {
                    return candidate;
                }
                index++;
            }
        }

        public static bool SamePath(string left, string right)
        {
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            {
                return false;
            }
            try
            {
                return string.Equals(
                    Path.GetFullPath(left).TrimEnd('\\'),
                    Path.GetFullPath(right).TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static string FindLauncherPath()
        {
            string fromRegistry = null;
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\App Paths\DingTalk.exe"))
                {
                    if (key != null)
                    {
                        object value = key.GetValue(null);
                        if (value == null)
                        {
                            value = key.GetValue("");
                        }
                        fromRegistry = value as string;
                    }
                }
            }
            catch
            {
                fromRegistry = null;
            }

            string resolved = ResolveLauncher(fromRegistry);
            if (resolved != null)
            {
                return resolved;
            }
            return ResolveLauncher(DefaultLauncherPath);
        }

        private static string ResolveLauncher(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            if (File.Exists(path))
            {
                return path;
            }
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                string mainExe = Path.Combine(dir, "main", "current", "DingTalk.exe");
                if (File.Exists(mainExe))
                {
                    return mainExe;
                }
            }
            return null;
        }

        private static void StartDingTalk(string launcherPath)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = launcherPath;
            psi.WorkingDirectory = Path.GetDirectoryName(launcherPath);
            psi.UseShellExecute = false;
            Process.Start(psi);
        }

        public static void EnableDebugPrivilege()
        {
            IntPtr token;
            if (!Native.OpenProcessToken(
                    Process.GetCurrentProcess().Handle,
                    Native.TOKEN_ADJUST_PRIVILEGES | Native.TOKEN_QUERY,
                    out token))
            {
                return;
            }
            try
            {
                long luid;
                if (!Native.LookupPrivilegeValue(null, "SeDebugPrivilege", out luid))
                {
                    return;
                }
                Native.TokenPrivileges tp = new Native.TokenPrivileges();
                tp.PrivilegeCount = 1;
                tp.Luid = luid;
                tp.Attributes = Native.SE_PRIVILEGE_ENABLED;
                Native.AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            }
            finally
            {
                Native.CloseHandle(token);
            }
        }

        public static LaunchResult LaunchProfile(
            ProfileInfo profile,
            List<ProfileInfo> allProfiles)
        {
            string launcher = FindLauncherPath();
            if (launcher == null)
            {
                return LaunchResult.LauncherNotFound;
            }
            if (profile == null || string.IsNullOrEmpty(profile.DataPath))
            {
                return LaunchResult.LauncherNotFound;
            }

            try
            {
                Directory.CreateDirectory(profile.DataPath);
            }
            catch
            {
                return LaunchResult.PermissionDenied;
            }

            if (allProfiles == null)
            {
                allProfiles = new List<ProfileInfo>();
            }
            bool contains = false;
            foreach (ProfileInfo existing in allProfiles)
            {
                if (object.ReferenceEquals(existing, profile) ||
                    SamePath(existing.DataPath, profile.DataPath))
                {
                    contains = true;
                    break;
                }
            }
            if (!contains)
            {
                allProfiles.Add(profile);
            }

            RefreshRunningStatus(allProfiles);
            if (profile.Running)
            {
                return LaunchResult.AlreadyRunning;
            }

            int runningBefore = 0;
            foreach (ProfileInfo p in allProfiles)
            {
                if (p.Running)
                {
                    runningBefore++;
                }
            }

            // 先关闭当前持有的单实例互斥量（无实例运行时此处不会有命中），
            // 否则新实例会认为钉钉已在运行而直接退出。
            List<MutexHandleInfo> hits = FindKeyMutexHandles();
            int closed = CloseKeyMutexHandles(hits);
            if (hits.Count > 0 && closed == 0)
            {
                return LaunchResult.PermissionDenied;
            }

            string redirectFile = GetRedirectFilePath();
            bool isDefaultProfile = SamePath(profile.DataPath, GetDefaultDataPath());

            try
            {
                if (isDefaultProfile)
                {
                    ClearRedirectFile(redirectFile);
                }
                else
                {
                    WriteRedirectFile(redirectFile, profile.DataPath);
                }

                StartDingTalk(launcher);

                if (!WaitForTargetProfileStable(
                        profile, allProfiles, runningBefore))
                {
                    return LaunchResult.Timeout;
                }
                return LaunchResult.Ok;
            }
            finally
            {
                ClearRedirectFile(redirectFile);
            }
        }

        private static bool WaitForTargetProfileStable(
            ProfileInfo target,
            List<ProfileInfo> allProfiles,
            int runningBefore)
        {
            DateTime deadline = DateTime.Now.AddSeconds(LaunchTimeoutSeconds);
            DateTime stableSince = DateTime.MinValue;
            bool everSeen = false;
            int targetCount = runningBefore + 1;

            while (DateTime.Now < deadline)
            {
                RefreshRunningStatus(allProfiles);

                int runningNow = 0;
                foreach (ProfileInfo profile in allProfiles)
                {
                    if (profile.Running)
                    {
                        runningNow++;
                    }
                }

                if (target.Running && runningNow >= targetCount)
                {
                    everSeen = true;
                    if (stableSince == DateTime.MinValue)
                    {
                        stableSince = DateTime.Now;
                    }
                    if ((DateTime.Now - stableSince).TotalSeconds >= 10)
                    {
                        return true;
                    }
                }
                else
                {
                    stableSince = DateTime.MinValue;
                }

                Thread.Sleep(2000);
            }

            return everSeen;
        }

        private static void WriteRedirectFile(string redirectFile, string targetPath)
        {
            string dir = Path.GetDirectoryName(redirectFile);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(redirectFile, targetPath, Encoding.ASCII);
        }

        private static void ClearRedirectFile(string redirectFile)
        {
            try
            {
                if (File.Exists(redirectFile))
                {
                    File.Delete(redirectFile);
                }
            }
            catch
            {
            }
        }

        public static int CountMainWindows()
        {
            int count = 0;
            Process[] procs = Process.GetProcessesByName(ProcessName);
            if (procs == null)
            {
                return 0;
            }
            foreach (Process p in procs)
            {
                try
                {
                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        count++;
                    }
                }
                catch
                {
                }
            }
            return count;
        }

        public static void RefreshRunningStatus(List<ProfileInfo> profiles)
        {
            if (profiles == null || profiles.Count == 0)
            {
                return;
            }

            Dictionary<string, string> ntPathCache = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (ProfileInfo profile in profiles)
            {
                profile.Running = false;
                if (string.IsNullOrEmpty(profile.DataPath))
                {
                    continue;
                }
                try
                {
                    if (!Directory.Exists(profile.DataPath))
                    {
                        Directory.CreateDirectory(profile.DataPath);
                    }
                    string nt = GetNtPath(profile.DataPath);
                    if (!string.IsNullOrEmpty(nt))
                    {
                        ntPathCache[profile.DataPath] = nt;
                    }
                }
                catch
                {
                }
            }

            HashSet<string> ntSet = new HashSet<string>(
                ntPathCache.Values, StringComparer.OrdinalIgnoreCase);
            if (ntSet.Count == 0)
            {
                return;
            }

            byte[] data = GetSystemHandleBuffer();
            if (data == null)
            {
                return;
            }

            HashSet<uint> pids = new HashSet<uint>();
            Process[] procs = Process.GetProcessesByName(ProcessName);
            if (procs != null)
            {
                foreach (Process p in procs)
                {
                    try
                    {
                        pids.Add((uint)p.Id);
                    }
                    catch
                    {
                    }
                }
            }

            int handleCount = BitConverter.ToInt32(data, 0);
            int entrySize = Marshal.SizeOf(typeof(HandleEntry));
            Dictionary<uint, HashSet<string>> pidPaths =
                new Dictionary<uint, HashSet<string>>();

            for (int i = 0; i < handleCount; i++)
            {
                int offset = IntPtr.Size + i * entrySize;
                if (offset + entrySize > data.Length)
                {
                    break;
                }

                HandleEntry entry = ReadHandleEntry(data, offset);
                if (!pids.Contains(entry.ProcessId))
                {
                    continue;
                }

                IntPtr process = Native.OpenProcess(
                    Native.PROCESS_DUP_HANDLE | Native.PROCESS_QUERY_LIMITED_INFORMATION,
                    false,
                    entry.ProcessId);
                if (process == IntPtr.Zero)
                {
                    continue;
                }
                try
                {
                    IntPtr duplicated;
                    int status = Native.NtDuplicateObject(
                        process,
                        (IntPtr)entry.HandleValue,
                        Process.GetCurrentProcess().Handle,
                        out duplicated,
                        0,
                        0,
                        Native.DUPLICATE_SAME_ACCESS);
                    if (status != 0 || duplicated == IntPtr.Zero)
                    {
                        continue;
                    }
                    try
                    {
                        string typeName = QueryObjectType(duplicated);
                        if (!string.Equals(typeName, "File", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        string objectName = QueryObjectName(duplicated);
                        if (string.IsNullOrEmpty(objectName))
                        {
                            continue;
                        }
                        foreach (ProfileInfo profile in profiles)
                        {
                            string ntPath;
                            if (!ntPathCache.TryGetValue(profile.DataPath, out ntPath) ||
                                string.IsNullOrEmpty(ntPath))
                            {
                                continue;
                            }
                            if (PathMatchesHandle(ntPath, objectName))
                            {
                                HashSet<string> paths;
                                if (!pidPaths.TryGetValue(entry.ProcessId, out paths))
                                {
                                    paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                    pidPaths.Add(entry.ProcessId, paths);
                                }
                                paths.Add(profile.DataPath);
                            }
                        }
                    }
                    finally
                    {
                        Native.CloseHandle(duplicated);
                    }
                }
                finally
                {
                    Native.CloseHandle(process);
                }
            }

            foreach (ProfileInfo profile in profiles)
            {
                foreach (HashSet<string> paths in pidPaths.Values)
                {
                    if (paths.Contains(profile.DataPath))
                    {
                        profile.Running = true;
                        break;
                    }
                }
            }
        }

        private static bool IsSingleProfileRunning(string dataPath)
        {
            List<ProfileInfo> one = new List<ProfileInfo>();
            ProfileInfo profile = new ProfileInfo("", dataPath);
            one.Add(profile);
            RefreshRunningStatus(one);
            return profile.Running;
        }

        private static bool PathMatchesHandle(string ntPath, string objectName)
        {
            string path = ntPath.TrimEnd('\\');
            string name = objectName.TrimEnd('\\');
            if (string.Equals(path, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return name.StartsWith(
                path + "\\",
                StringComparison.OrdinalIgnoreCase);
        }

        private static string GetNtPath(string directoryPath)
        {
            IntPtr handle = Native.CreateFile(
                directoryPath,
                Native.FILE_READ_ATTRIBUTES,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE | Native.FILE_SHARE_DELETE,
                IntPtr.Zero,
                Native.OPEN_EXISTING,
                Native.FILE_FLAG_BACKUP_SEMANTICS,
                IntPtr.Zero);
            if (handle == new IntPtr(-1))
            {
                return null;
            }
            try
            {
                int size = 1024;
                IntPtr buffer = Marshal.AllocHGlobal(size);
                try
                {
                    int needed = 0;
                    int status = Native.NtQueryObject(
                        handle, Native.ObjectNameInformation, buffer, size, out needed);
                    if (status != 0)
                    {
                        return null;
                    }
                    return ReadUnicodeStringFromBuffer(buffer);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                Native.CloseHandle(handle);
            }
        }

        private static List<MutexHandleInfo> FindKeyMutexHandles()
        {
            List<MutexHandleInfo> result = new List<MutexHandleInfo>();
            HashSet<uint> pids = new HashSet<uint>();
            Process[] procs = Process.GetProcessesByName(ProcessName);
            if (procs == null || procs.Length == 0)
            {
                return result;
            }
            foreach (Process p in procs)
            {
                try
                {
                    pids.Add((uint)p.Id);
                }
                catch
                {
                }
            }

            byte[] data = GetSystemHandleBuffer();
            if (data == null)
            {
                return result;
            }

            int count = BitConverter.ToInt32(data, 0);
            int entrySize = Marshal.SizeOf(typeof(HandleEntry));
            for (int i = 0; i < count; i++)
            {
                int offset = IntPtr.Size + i * entrySize;
                if (offset + entrySize > data.Length)
                {
                    break;
                }
                HandleEntry entry = ReadHandleEntry(data, offset);
                if (!pids.Contains(entry.ProcessId))
                {
                    continue;
                }

                IntPtr process = Native.OpenProcess(
                    Native.PROCESS_DUP_HANDLE | Native.PROCESS_QUERY_LIMITED_INFORMATION,
                    false,
                    entry.ProcessId);
                if (process == IntPtr.Zero)
                {
                    continue;
                }
                try
                {
                    IntPtr duplicated;
                    int status = Native.NtDuplicateObject(
                        process,
                        (IntPtr)entry.HandleValue,
                        Process.GetCurrentProcess().Handle,
                        out duplicated,
                        0,
                        0,
                        Native.DUPLICATE_SAME_ACCESS);
                    if (status != 0 || duplicated == IntPtr.Zero)
                    {
                        continue;
                    }
                    try
                    {
                        string typeName = QueryObjectType(duplicated);
                        if (!string.Equals(typeName, "Mutant", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        string objectName = QueryObjectName(duplicated);
                        if (objectName != null &&
                            objectName.IndexOf(MutexGuidPart, StringComparison.OrdinalIgnoreCase) >= 0 &&
                            objectName.IndexOf("DingTalk", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            MutexHandleInfo hit = new MutexHandleInfo();
                            hit.ProcessId = entry.ProcessId;
                            hit.HandleValue = entry.HandleValue;
                            hit.Name = objectName;
                            result.Add(hit);
                        }
                    }
                    finally
                    {
                        Native.CloseHandle(duplicated);
                    }
                }
                finally
                {
                    Native.CloseHandle(process);
                }
            }
            return result;
        }

        private static int CloseKeyMutexHandles(List<MutexHandleInfo> hits)
        {
            if (hits == null || hits.Count == 0)
            {
                return 0;
            }
            int closed = 0;
            Dictionary<uint, IntPtr> processCache = new Dictionary<uint, IntPtr>();
            try
            {
                foreach (MutexHandleInfo hit in hits)
                {
                    IntPtr process;
                    if (!processCache.TryGetValue(hit.ProcessId, out process))
                    {
                        process = Native.OpenProcess(
                            Native.PROCESS_DUP_HANDLE | Native.PROCESS_QUERY_LIMITED_INFORMATION,
                            false,
                            hit.ProcessId);
                        if (process == IntPtr.Zero)
                        {
                            continue;
                        }
                        processCache.Add(hit.ProcessId, process);
                    }

                    IntPtr duplicated;
                    int status = Native.NtDuplicateObject(
                        process,
                        (IntPtr)hit.HandleValue,
                        Process.GetCurrentProcess().Handle,
                        out duplicated,
                        0,
                        0,
                        Native.DUPLICATE_SAME_ACCESS | Native.DUPLICATE_CLOSE_SOURCE);
                    if (status == 0 && duplicated != IntPtr.Zero)
                    {
                        Native.CloseHandle(duplicated);
                        closed++;
                    }
                }
            }
            finally
            {
                foreach (IntPtr process in processCache.Values)
                {
                    Native.CloseHandle(process);
                }
            }
            return closed;
        }

        private static byte[] GetSystemHandleBuffer()
        {
            int size = 8 * 1024 * 1024;
            while (true)
            {
                IntPtr buffer = Marshal.AllocHGlobal(size);
                try
                {
                    int needed = 0;
                    int status = Native.NtQuerySystemInformation(
                        Native.SystemHandleInformation, buffer, size, out needed);
                    if (status == 0)
                    {
                        int length = needed > 0 ? needed : size;
                        byte[] data = new byte[length];
                        Marshal.Copy(buffer, data, 0, length);
                        return data;
                    }
                    if (status == unchecked((int)Native.STATUS_INFO_LENGTH_MISMATCH) &&
                        needed > size)
                    {
                        size = needed + 4096;
                        continue;
                    }
                    return null;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }

        private static string QueryObjectType(IntPtr handle)
        {
            int size = 512;
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                int needed = 0;
                int status = Native.NtQueryObject(
                    handle, Native.ObjectTypeInformation, buffer, size, out needed);
                if (status != 0)
                {
                    return null;
                }
                return ReadUnicodeStringFromBuffer(buffer);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static string QueryObjectName(IntPtr handle)
        {
            int size = 4096;
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                int needed = 0;
                int status = Native.NtQueryObject(
                    handle, Native.ObjectNameInformation, buffer, size, out needed);
                if (status == unchecked((int)Native.STATUS_INFO_LENGTH_MISMATCH) &&
                    needed > size)
                {
                    Marshal.FreeHGlobal(buffer);
                    size = needed + 64;
                    buffer = Marshal.AllocHGlobal(size);
                    status = Native.NtQueryObject(
                        handle, Native.ObjectNameInformation, buffer, size, out needed);
                }
                if (status != 0)
                {
                    return null;
                }
                return ReadUnicodeStringFromBuffer(buffer);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static string ReadUnicodeStringFromBuffer(IntPtr buffer)
        {
            ushort length = unchecked((ushort)Marshal.ReadInt16(buffer, 0));
            IntPtr namePointer = Marshal.ReadIntPtr(buffer, 8);
            if (length == 0 || namePointer == IntPtr.Zero)
            {
                return string.Empty;
            }
            return Marshal.PtrToStringUni(namePointer, length / 2);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HandleEntry
        {
            public ushort ProcessId;
            public ushort CreatorBackTraceIndex;
            public byte ObjectTypeIndex;
            public byte HandleAttributes;
            public ushort HandleValue;
            public IntPtr Object;
            public uint GrantedAccess;
        }

        private static HandleEntry ReadHandleEntry(byte[] data, int offset)
        {
            HandleEntry entry = new HandleEntry();
            entry.ProcessId = BitConverter.ToUInt16(data, offset);
            entry.CreatorBackTraceIndex = BitConverter.ToUInt16(data, offset + 2);
            entry.ObjectTypeIndex = data[offset + 4];
            entry.HandleAttributes = data[offset + 5];
            entry.HandleValue = BitConverter.ToUInt16(data, offset + 6);
            entry.Object = (IntPtr)BitConverter.ToInt64(data, offset + 8);
            entry.GrantedAccess = BitConverter.ToUInt32(data, offset + 16);
            return entry;
        }
    }
}
