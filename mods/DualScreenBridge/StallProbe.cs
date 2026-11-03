using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace DualScreenBridge
{
    // Watches the main thread's heartbeat from another thread and, while the game is stalled, records what the
    // process's threads are doing: whether the main thread waits or computes, and which thread uses the cpu.
    internal static class StallProbe
    {
        public static Action<string> Log;
        public static volatile bool Enabled;
        public static double StartMs = 1000;
        public static int MaxReports = 40;
        public static int MaxShortReports = 15;
        private static long heartbeat;
        private static int reports;
        private static int shortReports;

        public static void Beat()
        {
            Interlocked.Exchange(ref heartbeat, Stopwatch.GetTimestamp());
        }

        public static void Start()
        {
            Beat();
            Thread thread = new Thread(Run);
            thread.IsBackground = true;
            thread.Name = "dualscreen-probe";
            thread.Start();
        }

        private static double Ms(long from, long to)
        {
            return (to - from) * 1000.0 / Stopwatch.Frequency;
        }

        private static void Run()
        {
            int mainId = ProcInfo.MainThreadId();
            List<ThreadSample> baseline = null;
            List<string> details = new List<string>();
            long baselineAt = 0;
            long detailAt = 0;
            long stalledSince = 0;
            while (true)
            {
                Thread.Sleep(200);
                try
                {
                    long now = Stopwatch.GetTimestamp();
                    if (!Enabled || reports >= MaxReports)
                    {
                        baseline = null;
                        continue;
                    }
                    long beat = Interlocked.Read(ref heartbeat);
                    if (Ms(beat, now) >= StartMs)
                    {
                        if (baseline == null)
                        {
                            baseline = ProcInfo.Threads();
                            baselineAt = now;
                            detailAt = now;
                            stalledSince = beat;
                            details.Clear();
                            details.Add(ProcInfo.ThreadDetail(mainId));
                        }
                        else
                        {
                            if (Ms(detailAt, now) >= 1500 && details.Count < 8)
                            {
                                detailAt = now;
                                details.Add(ProcInfo.ThreadDetail(mainId));
                            }
                            if (Ms(baselineAt, now) >= 8000)
                            {
                                Report(baseline, baselineAt, now, mainId, details, Ms(stalledSince, now), true);
                                baseline = ProcInfo.Threads();
                                baselineAt = now;
                                detailAt = now;
                                details.Clear();
                                details.Add(ProcInfo.ThreadDetail(mainId));
                            }
                        }
                    }
                    else if (baseline != null)
                    {
                        if (Ms(baselineAt, now) >= 400) Report(baseline, baselineAt, now, mainId, details, Ms(stalledSince, now), false);
                        baseline = null;
                    }
                }
                catch (Exception)
                {
                }
            }
        }

        private static void Report(List<ThreadSample> before, long beforeAt, long now, int mainId, List<string> details, double stallMs, bool ongoing)
        {
            if (stallMs < 4000)
            {
                if (shortReports >= MaxShortReports) return;
                shortReports++;
            }
            reports++;
            Dictionary<int, long> start = new Dictionary<int, long>();
            foreach (ThreadSample s in before) start[s.Id] = s.CpuTicks;
            List<ThreadSample> after = ProcInfo.Threads();
            List<ThreadSample> grown = new List<ThreadSample>();
            long mainTicks = 0;
            foreach (ThreadSample s in after)
            {
                long was;
                if (!start.TryGetValue(s.Id, out was)) was = 0;
                long delta = s.CpuTicks - was;
                if (s.Id == mainId) mainTicks = delta;
                if (delta < 2) continue;
                ThreadSample g = new ThreadSample();
                g.Id = s.Id;
                g.Name = s.Name;
                g.CpuTicks = delta;
                grown.Add(g);
            }
            grown.Sort(delegate (ThreadSample a, ThreadSample b) { return b.CpuTicks.CompareTo(a.CpuTicks); });
            StringBuilder sb = new StringBuilder();
            sb.Append("stall probe: ").Append((stallMs / 1000).ToString("0.0")).Append(ongoing ? "s so far" : "s");
            sb.Append(", sampled ").Append((Ms(beforeAt, now) / 1000).ToString("0.0")).Append("s; main thread ").Append(mainId);
            sb.Append(" cpu +").Append(mainTicks * 10).Append("ms, states [").Append(string.Join(" | ", details.ToArray())).Append("]");
            sb.Append("; busiest threads ");
            if (grown.Count == 0) sb.Append("none");
            for (int i = 0; i < grown.Count && i < 5; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(grown[i].Name).Append('(').Append(grown[i].Id).Append(") +").Append(grown[i].CpuTicks * 10).Append("ms");
            }
            sb.Append("; ").Append(after.Count).Append(" threads; ").Append(ProcInfo.Memory());
            sb.Append("; psi cpu/mem/io ").Append(ProcInfo.Pressure("cpu")).Append('/').Append(ProcInfo.Pressure("memory")).Append('/').Append(ProcInfo.Pressure("io"));
            string sensors = ProcInfo.SensorLine();
            if (sensors.Length > 0) sb.Append("; ").Append(sensors);
            if (Log != null) Log(sb.ToString());
        }
    }
}
