using System;
using System.IO;

namespace RAWSimO.MultiAgentPathFinding.Toolbox
{
    /// <summary>
    /// Lightweight diagnostic logger for comparing CBS vs ECBS agent paths.
    ///
    /// Activation via environment variables (read once at first access):
    ///   RMFS_PATH_DIAG_FILE       : absolute/relative path to output file (required)
    ///   RMFS_PATH_DIAG_MAX_CALLS  : max number of FindPaths() calls to log (default 1)
    ///
    /// Intended use: run CBS with RMFS_PATH_DIAG_FILE=cbs_paths.log; run ECBS with
    /// RMFS_PATH_DIAG_FILE=ecbs_paths.log; diff the two to see per-agent path choice
    /// differences on identical reservation states.
    ///
    /// Thread-safe. Zero overhead when disabled (Enabled returns false early).
    /// </summary>
    public static class PathDiagnosticLogger
    {
        private static StreamWriter _writer;
        private static readonly object _lock = new object();
        private static bool _enabled;
        private static int _maxCalls = 1;
        private static bool _initialized = false;

        private static void EnsureInit()
        {
            if (_initialized) return;
            lock (_lock)
            {
                if (_initialized) return;
                _initialized = true;

                string path = Environment.GetEnvironmentVariable("RMFS_PATH_DIAG_FILE");
                if (!string.IsNullOrWhiteSpace(path))
                {
                    try
                    {
                        _writer = new StreamWriter(path, append: false);
                        _enabled = true;
                    }
                    catch
                    {
                        _enabled = false;
                    }
                }

                string mc = Environment.GetEnvironmentVariable("RMFS_PATH_DIAG_MAX_CALLS");
                if (!string.IsNullOrWhiteSpace(mc) && int.TryParse(mc, out int v) && v > 0)
                    _maxCalls = v;
            }
        }

        public static bool Enabled
        {
            get { EnsureInit(); return _enabled; }
        }

        public static int MaxCalls
        {
            get { EnsureInit(); return _maxCalls; }
        }

        public static void WriteLine(string line)
        {
            EnsureInit();
            if (!_enabled) return;
            lock (_lock)
            {
                _writer.WriteLine(line);
                _writer.Flush();
            }
        }
    }
}
