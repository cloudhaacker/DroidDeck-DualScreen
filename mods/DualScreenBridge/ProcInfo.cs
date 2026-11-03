using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DualScreenBridge
{
    internal sealed class ThreadSample
    {
        public int Id;
        public string Name;
        public char State;
        public long CpuTicks;
    }

    // Wine shows the host's process files under Z:, which say what the game's threads and the device are doing.
    internal static class ProcInfo
    {
        private static readonly List<string> tempFiles = new List<string>();
        private static readonly List<string> tempNames = new List<string>();
        private static readonly List<string> freqFiles = new List<string>();
        private const string GpuBusy = "/sys/class/kgsl/kgsl-3d0/gpu_busy_percentage";
        private const string GpuClock = "/sys/class/kgsl/kgsl-3d0/gpuclk";

        public static string Native(string unixPath)
        {
            return System.IO.Path.DirectorySeparatorChar == '\\' ? "Z:" + unixPath.Replace('/', '\\') : unixPath;
        }

        public static string Read(string unixPath)
        {
            try
            {
                return File.ReadAllText(Native(unixPath)).Trim();
            }
            catch (Exception)
            {
                return null;
            }
        }

        // "tid (comm) S ppid ... utime stime ...": the name may hold spaces and brackets, so split after the last bracket.
        public static bool ParseStat(string text, out string name, out char state, out long cpuTicks)
        {
            name = "";
            state = '?';
            cpuTicks = 0;
            if (text == null) return false;
            int open = text.IndexOf('(');
            int close = text.LastIndexOf(')');
            if (open < 0 || close < open) return false;
            name = text.Substring(open + 1, close - open - 1);
            string[] rest = text.Substring(close + 1).Trim().Split(' ');
            if (rest.Length < 13) return false;
            state = rest[0].Length > 0 ? rest[0][0] : '?';
            long utime;
            long stime;
            if (!long.TryParse(rest[11], out utime) || !long.TryParse(rest[12], out stime)) return false;
            cpuTicks = utime + stime;
            return true;
        }

        public static int MainThreadId()
        {
            string text = Read("/proc/self/stat");
            if (text == null) return 0;
            int space = text.IndexOf(' ');
            int id;
            return space > 0 && int.TryParse(text.Substring(0, space), out id) ? id : 0;
        }

        public static List<ThreadSample> Threads()
        {
            List<ThreadSample> list = new List<ThreadSample>();
            try
            {
                foreach (string dir in Directory.GetDirectories(Native("/proc/self/task")))
                {
                    string leaf = System.IO.Path.GetFileName(dir);
                    int id;
                    if (!int.TryParse(leaf, out id)) continue;
                    string name;
                    char state;
                    long ticks;
                    if (!ParseStat(Read("/proc/self/task/" + leaf + "/stat"), out name, out state, out ticks)) continue;
                    ThreadSample sample = new ThreadSample();
                    sample.Id = id;
                    sample.Name = name;
                    sample.State = state;
                    sample.CpuTicks = ticks;
                    list.Add(sample);
                }
            }
            catch (Exception)
            {
            }
            return list;
        }

        // State, the kernel function the thread sleeps in, and the system call number it is inside.
        public static string ThreadDetail(int tid)
        {
            string dir = "/proc/self/task/" + tid + "/";
            string name;
            char state;
            long ticks;
            if (!ParseStat(Read(dir + "stat"), out name, out state, out ticks)) return "unreadable";
            string wchan = Read(dir + "wchan");
            string call = Read(dir + "syscall");
            if (call != null)
            {
                int space = call.IndexOf(' ');
                if (space > 0) call = call.Substring(0, space);
            }
            return state + " wchan=" + (wchan ?? "?") + " syscall=" + (call ?? "?");
        }

        public static string Memory()
        {
            string text = Read("/proc/self/status");
            if (text == null) return "mem unreadable";
            return "rss " + Megabytes(text, "VmRSS:") + "MB swap " + Megabytes(text, "VmSwap:") + "MB";
        }

        private static long Megabytes(string text, string key)
        {
            int at = text.IndexOf(key, StringComparison.Ordinal);
            if (at < 0) return -1;
            int start = at + key.Length;
            int end = text.IndexOf('\n', start);
            if (end < 0) end = text.Length;
            string[] parts = text.Substring(start, end - start).Trim().Split(' ');
            long kb;
            return long.TryParse(parts[0], out kb) ? kb / 1024 : -1;
        }

        // Share of the last 10 seconds that something waited for the cpu, memory or disk.
        public static string Pressure(string kind)
        {
            string text = Read("/proc/pressure/" + kind);
            if (text == null) return "-";
            int at = text.IndexOf("avg10=", StringComparison.Ordinal);
            if (at < 0) return "-";
            int start = at + 6;
            int end = text.IndexOf(' ', start);
            return text.Substring(start, (end < 0 ? text.Length : end) - start);
        }

        public static void FindSensors()
        {
            try
            {
                foreach (string dir in Directory.GetDirectories(Native("/sys/class/thermal"), "thermal_zone*"))
                {
                    string leaf = System.IO.Path.GetFileName(dir);
                    string file = "/sys/class/thermal/" + leaf + "/temp";
                    if (Read(file) == null) continue;
                    tempFiles.Add(file);
                    tempNames.Add(Read("/sys/class/thermal/" + leaf + "/type") ?? leaf);
                }
            }
            catch (Exception)
            {
            }
            for (int i = 0; i < 32; i++)
            {
                string file = "/sys/devices/system/cpu/cpu" + i + "/cpufreq/scaling_cur_freq";
                if (Read(file) != null) freqFiles.Add(file);
            }
        }

        // Hottest zones, cpu clock range and, where the host shows it, the gpu load.
        public static string SensorLine()
        {
            StringBuilder sb = new StringBuilder();
            List<KeyValuePair<long, string>> temps = new List<KeyValuePair<long, string>>();
            for (int i = 0; i < tempFiles.Count; i++)
            {
                long value;
                if (!long.TryParse(Read(tempFiles[i]), out value)) continue;
                if (value > 1000) value /= 1000;
                temps.Add(new KeyValuePair<long, string>(value, tempNames[i]));
            }
            temps.Sort(delegate (KeyValuePair<long, string> a, KeyValuePair<long, string> b) { return b.Key.CompareTo(a.Key); });
            for (int i = 0; i < temps.Count && i < 3; i++)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(temps[i].Value).Append(' ').Append(temps[i].Key).Append('C');
            }
            long low = long.MaxValue;
            long high = 0;
            foreach (string file in freqFiles)
            {
                long khz;
                if (!long.TryParse(Read(file), out khz)) continue;
                if (khz < low) low = khz;
                if (khz > high) high = khz;
            }
            if (high > 0)
            {
                if (sb.Length > 0) sb.Append("; ");
                sb.Append("cpu ").Append(low / 1000).Append('-').Append(high / 1000).Append("MHz");
            }
            string busy = Read(GpuBusy);
            if (busy != null)
            {
                if (sb.Length > 0) sb.Append("; ");
                sb.Append("gpu busy ").Append(busy);
                long hz;
                if (long.TryParse(Read(GpuClock), out hz)) sb.Append(' ').Append(hz / 1000000).Append("MHz");
            }
            return sb.ToString();
        }
    }
}
