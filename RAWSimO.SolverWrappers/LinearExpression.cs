using Gurobi;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RAWSimO.SolverWrappers
{
    /// <summary>
    /// Stores a Gurobi linear expression and exposes the operators used by RAWSim-O.
    /// </summary>
    public class LinearExpression
    {
        internal dynamic Expression;
        protected internal LinearModel Solver;

        public static LinearExpression Sum(IEnumerable<double> coeffs, IEnumerable<Variable> variables)
        {
            Variable[] variableArray = variables.ToArray();
            LinearModel solver = variableArray.First().Solver;
            GRBLinExpr expr = new GRBLinExpr();
            expr.AddTerms(coeffs.ToArray(), variableArray.Select(e => e.Expression as GRBVar).ToArray());
            return new LinearExpression() { Solver = solver, Expression = expr };
        }

        public static LinearExpression Sum(IEnumerable<Variable> variables)
        {
            Variable[] variableArray = variables.ToArray();
            LinearModel solver = variableArray.First().Solver;
            GRBLinExpr expr = new GRBLinExpr();
            expr.AddTerms(Enumerable.Repeat(1.0, variableArray.Length).ToArray(), variableArray.Select(e => e.Expression as GRBVar).ToArray());
            return new LinearExpression() { Solver = solver, Expression = expr };
        }

        public static LinearExpression Sum(IEnumerable<LinearExpression> expressions)
        {
            LinearExpression[] expressionArray = expressions.ToArray();
            LinearModel solver = expressionArray.First().Solver;
            GRBLinExpr expr = new GRBLinExpr();
            foreach (var exp in expressionArray)
                expr += exp.Expression;
            return new LinearExpression() { Solver = solver, Expression = expr };
        }

        public static LinearExpression operator +(LinearExpression exp1, LinearExpression exp2)
        {
            return new LinearExpression() { Solver = exp1.Solver, Expression = exp1.Expression + exp2.Expression };
        }

        public static LinearExpression operator +(LinearExpression exp1, double exp2)
        {
            return new LinearExpression() { Solver = exp1.Solver, Expression = exp1.Expression + exp2 };
        }

        public static LinearExpression operator +(double exp1, LinearExpression exp2)
        {
            return new LinearExpression() { Solver = exp2.Solver, Expression = exp1 + exp2.Expression };
        }

        public static LinearExpression operator -(LinearExpression exp1, LinearExpression exp2)
        {
            return new LinearExpression() { Solver = exp1.Solver, Expression = exp1.Expression - exp2.Expression };
        }

        public static LinearExpression operator -(LinearExpression exp1, double exp2)
        {
            return new LinearExpression() { Solver = exp1.Solver, Expression = exp1.Expression - exp2 };
        }

        public static LinearExpression operator -(double exp1, LinearExpression exp2)
        {
            return new LinearExpression() { Solver = exp2.Solver, Expression = exp1 - exp2.Expression };
        }

        public static LinearExpression operator *(LinearExpression exp1, LinearExpression exp2)
        {
            return new LinearExpression() { Solver = exp1.Solver, Expression = exp1.Expression * exp2.Expression };
        }

        public static LinearExpression operator *(LinearExpression exp1, double exp2)
        {
            return new LinearExpression() { Solver = exp1.Solver, Expression = exp1.Expression * exp2 };
        }

        public static LinearExpression operator *(double exp1, LinearExpression exp2)
        {
            return new LinearExpression() { Solver = exp2.Solver, Expression = exp1 * exp2.Expression };
        }

        public static LinearExpression operator <=(LinearExpression exp1, LinearExpression exp2)
        {
            return new LinearExpression() { Solver = exp1.Solver, Expression = exp1.Expression <= exp2.Expression };
        }

        public static LinearExpression operator <=(LinearExpression exp1, double exp2)
        {
            return new LinearExpression() { Solver = exp1.Solver, Expression = exp1.Expression <= exp2 };
        }

        public static LinearExpression operator <=(double exp1, LinearExpression exp2)
        {
            return new LinearExpression() { Solver = exp2.Solver, Expression = exp1 <= exp2.Expression };
        }

        public static LinearExpression operator >=(LinearExpression exp1, LinearExpression exp2)
        {
            return new LinearExpression() { Solver = exp1.Solver, Expression = exp1.Expression >= exp2.Expression };
        }

        public static LinearExpression operator >=(LinearExpression exp1, double exp2)
        {
            return new LinearExpression() { Solver = exp1.Solver, Expression = exp1.Expression >= exp2 };
        }

        public static LinearExpression operator >=(double exp1, LinearExpression exp2)
        {
            return new LinearExpression() { Solver = exp2.Solver, Expression = exp1 >= exp2.Expression };
        }

        public static LinearExpression operator ==(LinearExpression exp1, LinearExpression exp2)
        {
            return new LinearExpression() { Solver = exp1.Solver, Expression = exp1.Expression == exp2.Expression };
        }

        public static LinearExpression operator ==(LinearExpression exp1, double exp2)
        {
            return new LinearExpression() { Solver = exp1.Solver, Expression = exp1.Expression == exp2 };
        }

        public static LinearExpression operator ==(double exp1, LinearExpression exp2)
        {
            return new LinearExpression() { Solver = exp2.Solver, Expression = exp1 == exp2.Expression };
        }

        public static LinearExpression operator !=(LinearExpression exp1, LinearExpression exp2) { throw new InvalidOperationException("No inequality operator defined."); }

        public static LinearExpression operator !=(LinearExpression exp1, double exp2) { throw new InvalidOperationException("No inequality operator defined."); }

        public static LinearExpression operator !=(double exp1, LinearExpression exp2) { throw new InvalidOperationException("No inequality operator defined."); }

        public override bool Equals(object obj) { return base.Equals(obj); }

        public override int GetHashCode() { return base.GetHashCode(); }
    }
}
