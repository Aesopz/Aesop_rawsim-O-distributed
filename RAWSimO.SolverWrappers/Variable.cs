using Gurobi;
using System;

namespace RAWSimO.SolverWrappers
{
    /// <summary>
    /// Represents a variable that wraps a Gurobi variable.
    /// </summary>
    public class Variable : LinearExpression
    {
        public Variable(LinearModel solver, VariableType variableType, double lb, double ub, string name)
        {
            if (solver.Type != SolverType.Gurobi)
                throw new NotSupportedException("Only Gurobi is supported in this build.");

            Solver = solver;
            Solver.RegisterVariable(this);
            Expression = solver.GurobiModel.AddVar(
                lb,
                ub,
                0,
                variableType == VariableType.Continuous ? GRB.CONTINUOUS :
                variableType == VariableType.Binary ? GRB.BINARY :
                GRB.INTEGER,
                name);
        }

        internal Func<GRBVar, double> _gurobiIntermediateValueRetriever;

        public double CallbackValue { get { return _gurobiIntermediateValueRetriever(Expression); } }

        public double Value { get { return GetValue(); } }

        public double GetValue()
        {
            return Expression.Get(GRB.DoubleAttr.X);
        }

        public double LB { get { return GetLB(); } set { SetLB(value); } }

        public double GetLB()
        {
            return Expression.Get(GRB.DoubleAttr.LB);
        }

        public void SetLB(double value)
        {
            Expression.Set(GRB.DoubleAttr.LB, value);
        }

        public double UB { get { return GetUB(); } set { SetUB(value); } }

        public double GetUB()
        {
            return Expression.Get(GRB.DoubleAttr.UB);
        }

        public void SetUB(double value)
        {
            Expression.Set(GRB.DoubleAttr.UB, value);
        }
    }
}
