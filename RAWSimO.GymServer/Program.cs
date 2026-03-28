using System;
using System.Globalization;

namespace RAWSimO.GymServer
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: RAWSimO.GymServer <instance.xinst> <setting.xsett> <config.xconf> [port] [step_duration] [max_steps] [--baseline]");
                Console.WriteLine("  --baseline  Run in baseline mode: AgentAStar controls destinations (policy actions ignored)");
                return;
            }

            string instPath = args[0];
            string settPath = args[1];
            string confPath = args[2];

            // Parse named flags and positional args beyond the first 3
            int port = int.TryParse(Environment.GetEnvironmentVariable("RMFS_GYM_PORT"), out int envPort) ? envPort : 7654;
            double stepDuration = 1.0;
            int maxSteps = 1000;
            bool baselineMode = false;

            for (int i = 3; i < args.Length; i++)
            {
                if (args[i] == "--baseline") { baselineMode = true; continue; }
                if (i == 3) port = int.Parse(args[i]);
                else if (i == 4) stepDuration = double.Parse(args[i], CultureInfo.InvariantCulture);
                else if (i == 5) maxSteps = int.Parse(args[i]);
            }

            Console.WriteLine($"Starting GymServer: {instPath}");
            Console.WriteLine($"Port: {port}, StepDuration: {stepDuration}s, MaxSteps: {maxSteps}, Baseline: {baselineMode}");

            var host = new GymSimulationHost(instPath, settPath, confPath, stepDuration, maxSteps);
            if (baselineMode)
                host.EnableBaselineMode();

            using var server = new TcpGymServer(host, port);
            server.Start();
        }
    }
}
