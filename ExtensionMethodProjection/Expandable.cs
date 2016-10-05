using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;


namespace ExtensionMethodProjection
{
	public class ExpandableMethodAttribute :
				Attribute
	{
		public ExpandableMethodAttribute()
		{
		}
	}

	public class ReplaceInExpressionTree : Attribute
	{
		public string MethodName { get; set; }
	}

	public static class ExtendableExtensions
	{
		public static IQueryable<T> AsExtendable<T>(this IQueryable<T> source)
		{

			if (source is ExtendableQuery<T>)
			{
				return (ExtendableQuery<T>)source;
			}

			return new ExtendableQueryProvider(source.Provider).CreateQuery<T>(source.Expression);
		}
	}

	class ExtendableVisitor : ExpressionVisitor
	{
		private readonly IQueryProvider _provider;
		private readonly Dictionary<ParameterExpression, Expression> _replacements = new Dictionary<ParameterExpression, Expression>();

		internal ExtendableVisitor(IQueryProvider provider)
		{
			_provider = provider;
		}

		protected override Expression VisitMethodCall(MethodCallExpression node)
		{
			bool expandNode = node.Method.GetCustomAttributes(typeof(ExpandableMethodAttribute), false).Any();
			if (expandNode && node.Method.IsStatic)
			{
				object[] args = new object[node.Arguments.Count];
				args[0] = _provider.CreateQuery(node.Arguments[0]);

				for (int i = 1; i < node.Arguments.Count; i++)
				{
					Expression arg = node.Arguments[i];
					args[i] = (arg.NodeType == ExpressionType.Constant) ? ((ConstantExpression)arg).Value : arg;
				}
				return ((IQueryable)node.Method.Invoke(null, args)).Expression;
			}
			var replaceNodeAttributes = node.Method.GetCustomAttributes(typeof(ReplaceInExpressionTree), false).Cast<ReplaceInExpressionTree>();
			if (replaceNodeAttributes.Any() && node.Method.IsStatic)
			{
				var replaceWith = node.Method.DeclaringType.GetMethod(replaceNodeAttributes.First().MethodName).Invoke(null, null);
				if (replaceWith is LambdaExpression)
				{
					RegisterReplacementParameters(node.Arguments.ToArray(), replaceWith as LambdaExpression);
					return Visit((replaceWith as LambdaExpression).Body);
				}
			}
			return base.VisitMethodCall(node);
		}
		protected override Expression VisitParameter(ParameterExpression node)
		{
			Expression replacement;
			if (_replacements.TryGetValue(node, out replacement))
				return Visit(replacement);
			return base.VisitParameter(node);
		}
		private void RegisterReplacementParameters(Expression[] parameterValues, LambdaExpression expressionToVisit)
		{
			if (parameterValues.Length != expressionToVisit.Parameters.Count)
				throw new ArgumentException(string.Format("The parameter values count ({0}) does not match the expression parameter count ({1})", parameterValues.Length, expressionToVisit.Parameters.Count));
			foreach (var x in expressionToVisit.Parameters.Select((p, idx) => new { Index = idx, Parameter = p }))
			{
				if (_replacements.ContainsKey(x.Parameter))
				{
					throw new Exception("Parameter already registered, this shouldn't happen.");
				}
				_replacements.Add(x.Parameter, parameterValues[x.Index]);
			}
		}
	}


	class ExtendableQuery<T> : IQueryable<T>, IOrderedQueryable<T>
	{
		ExtendableQueryProvider _provider;
		Expression _expression;

		public ExtendableQuery(ExtendableQueryProvider provider, Expression expression)
		{
			_provider = provider;
			_expression = expression;
		}

		public IEnumerator<T> GetEnumerator()
		{
			return _provider.ExecuteQuery<T>(_expression).GetEnumerator();
		}

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		public Type ElementType
		{
			get
			{
				return typeof(T);
			}
		}

		public Expression Expression
		{
			get
			{
				return _expression;
			}
		}

		public IQueryProvider Provider
		{
			get
			{
				return _provider;
			}
		}
	}

	class ExtendableQueryProvider : IQueryProvider
	{
		IQueryProvider _underlyingQueryProvider;

		private ExtendableQueryProvider()
		{
		}

		internal ExtendableQueryProvider(IQueryProvider underlyingQueryProvider)
		{
			_underlyingQueryProvider = underlyingQueryProvider;
		}

		public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
		{
			return new ExtendableQuery<TElement>(this, expression);
		}

		public IQueryable CreateQuery(Expression expression)
		{
			Type elementType = expression.Type.GetElementType();
			try
			{
				return (IQueryable)Activator.CreateInstance(typeof(ExtendableQuery<>).MakeGenericType(elementType), new object[] { this, expression });
			}
			catch (System.Reflection.TargetInvocationException tie)
			{
				throw tie.InnerException;
			}
		}

		internal IEnumerable<T> ExecuteQuery<T>(Expression expression)
		{
			return _underlyingQueryProvider.CreateQuery<T>(Visit(expression)).AsEnumerable();
		}

		public TResult Execute<TResult>(Expression expression)
		{
			return _underlyingQueryProvider.Execute<TResult>(Visit(expression));
		}

		public object Execute(Expression expression)
		{
			return _underlyingQueryProvider.Execute(Visit(expression));
		}

		private Expression Visit(Expression exp)
		{
			ExtendableVisitor vstr = new ExtendableVisitor(_underlyingQueryProvider);
			Expression visitedExp = vstr.Visit(exp);

			return visitedExp;
		}
	}
}
