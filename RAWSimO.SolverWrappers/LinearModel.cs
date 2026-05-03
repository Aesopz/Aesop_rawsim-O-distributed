using Gurobi;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RAWSimO.SolverWrappers
{
    /// <summary>
    /// Gurobi-backed linear model wrapper used by RAWSim-O.
    /// </summary>
    public class LinearModel
    {
        public SolverType Type { get; private set; }

        private HashSet<Variable> _variables = new HashSet<Variable>();

        internal GRBModel GurobiModel;

        public IStatusCallback StatusCallback { get { return _gurobiStatusCallback; } }

        private GurobiStatusCallback _gurobiStatusCallback;

        private Action<string> _logger;

        private void LogLine(string msg) { if (_logger != null) _logger(msg + Environment.NewLine); }

        public LinearModel(SolverType type, Action<string> logger, int threadCount = 0)
        {
            if (type != SolverType.Gurobi)
                throw new NotSupportedException("Only Gurobi is supported in this build.");

            Type = type;
            _logger = logger;

            GRBEnv gurobiEnvironment = new GRBEnv();
            GurobiModel = new GRBModel(gurobiEnvironment);
            GurobiModel.GetEnv().Set(GRB.IntParam.OutputFlag, 0);
            if (threadCount > 0)
                GurobiModel.GetEnv().Set(GRB.IntParam.Threads, threadCount);

            _gurobiStatusCallback = new GurobiStatusCallback(this) { Logger = logger };
            GurobiModel.SetCallback(_gurobiStatusCallback);
        }

        internal void RegisterVariable(Variable variable)
        {
            _variables.Add(variable);
        }

        public void Update()
        {
            GurobiModel.Update();
        }

        public void Optimize()
        {
            _isBusy = true;
            Update();
            GurobiModel.Optimize();
            _isBusy = false;
        }

        public void Abort()
        {
            GurobiModel.Terminate();
        }

        public bool HasSolution()
        {
            return GurobiModel.Get(GRB.IntAttr.SolCount) > 0;
        }

        public bool IsOptimal()
        {
            return GurobiModel.Get(GRB.IntAttr.Status) == GRB.Status.OPTIMAL;
        }

        public double GetObjectiveValue()
        {
            return GurobiModel.Get(GRB.DoubleAttr.ObjVal);
        }

        public double GetBestBound()
        {
            return GurobiModel.Get(GRB.DoubleAttr.ObjBound);
        }

        public double GetGap()
        {
            return GurobiModel.Get(GRB.DoubleAttr.MIPGap);
        }

        private bool _isBusy = false;

        public bool IsBusy() { return _isBusy; }

        public void SetObjective(LinearExpression expression, OptimizationSense sense)
        {
            GurobiModel.SetObjective(expression.Expression, sense == OptimizationSense.Minimize ? GRB.MINIMIZE : GRB.MAXIMIZE);
        }

        public void AddConstr(LinearExpression expression, string name)
        {
            GurobiModel.AddConstr(expression.Expression, name);
        }

        public void SetParam(string paramName, string paramValue)
        {
            GurobiModel.GetEnv().Set(paramName, paramValue);
            LogLine(paramName + " set to (set by name): " + paramValue);
        }

        public void SetTimelimit(TimeSpan timelimit)
        {
            GurobiModel.GetEnv().Set(GRB.DoubleParam.TimeLimit, timelimit.TotalSeconds);
            LogLine("Timelimit set to: " + GurobiModel.GetEnv().Get(GRB.DoubleParam.TimeLimit));
        }

        public enum ParamScaling
        {
            None,
            Default,
            Aggressive,
        }

        public void SetScaling(ParamScaling scaling)
        {
            int scaleFlag = scaling == ParamScaling.None ? 0 : scaling == ParamScaling.Aggressive ? 2 : -1;
            GurobiModel.GetEnv().Set(GRB.IntParam.ScaleFlag, scaleFlag);
            LogLine("Scaling set to: " + GurobiModel.GetEnv().Get(GRB.IntParam.ScaleFlag));
        }

        public void ExportMPS(string path)
        {
            path = path.EndsWith(".mps") ? path : path + ".mps";
            GurobiModel.Update();
            GurobiModel.Write(path);
        }

        public void ExportLP(string path)
        {
            path = path.EndsWith(".lp") ? path : path + ".lp";
            GurobiModel.Update();
            GurobiModel.Write(path);
        }

        public interface IStatusCallback
        {
            Action<string> Logger { get; }
            Action NewIncumbent { get; set; }
            Action<double> LogIncumbent { get; set; }
        }

        public class GurobiStatusCallback : GRBCallback, IStatusCallback
        {
            public GurobiStatusCallback(LinearModel solver) { _solver = solver; }

            private LinearModel _solver;

            public Action<string> Logger { get; set; }

            public Action NewIncumbent { get; set; }

            public Action<double> LogIncumbent { get; set; }

            protected override void Callback()
            {
                if (where == GRB.Callback.MESSAGE && Logger != null)
                    Logger(GetStringInfo(GRB.Callback.MSG_STRING));

                if (where == GRB.Callback.MIPSOL)
                {
                    if (LogIncumbent != null)
                        LogIncumbent(GetDoubleInfo(GRB.Callback.MIPSOL_OBJ));
                    if (NewIncumbent != null)
                    {
                        foreach (var variable in _solver._variables)
                            variable._gurobiIntermediateValueRetriever = GetSolution;
                        NewIncumbent();
                    }
                }
            }
        }

        public static void Test(SolverType type)
        {
            Console.WriteLine("Is64BitProcess: " + Environment.Is64BitProcess);
            LinearModel wrapper = new LinearModel(type, (string s) => { Console.Write(s); });
            Console.WriteLine("Setting up model and optimizing it with " + wrapper.Type);
            string x = "x"; string y = "y"; string z = "z";
            List<string> variableNames = new List<string>() { x, y, z };
            VariableCollection<string> variables = new VariableCollection<string>(wrapper, VariableType.Binary, 0, 1, (string s) => { return s; });
            wrapper.SetObjective(0.5 * variables[x] + variables[y] + 4 * variables[z], OptimizationSense.Maximize);
            wrapper.AddConstr(LinearExpression.Sum(variableNames.Select(v => 2 * variables[v])) <= 4, "Summation");
            wrapper.AddConstr(variables[x] + variables[y] + variables[z] <= 2.0, "limit");
            wrapper.AddConstr(variables[x] == variables[z], "equal");
            wrapper.AddConstr(variables[x] * 2 == 2, "times");
            wrapper.Update();
            wrapper.Optimize();
            wrapper.ExportLP("lpexl.lp");
            Console.WriteLine("Solution:");
            if (wrapper.HasSolution())
            {
                Console.WriteLine("Obj: " + wrapper.GetObjectiveValue());
                foreach (var variableName in variableNames)
                    Console.WriteLine(variableName + ": " + variables[variableName].GetValue());
            }
            else
            {
                Console.WriteLine("No solution!");
            }
        }
    }
}
