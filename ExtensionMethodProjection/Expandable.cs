using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;


namespace ExtensionMethodProjection
{
    namespace StaticVoid.ToolSite.Site.Helpers
    {
        class ExpandableMethodAttribute :
                Attribute
        {
            public ExpandableMethodAttribute()
            {
            }
        }

        static class Extensions
        {
            // Example extension method
            //[ExpandableMethod]
            //public static IQueryable<YOUR_ENTITY> ExampleMethod(this IQueryable<YOUR_ENTITY> source, other args... )
            //{
            //	return source.Where( ... ) or something similar;
            //}

            public static IQueryable<T> AsExtendable<T>(this IQueryable<T> source)
            {

                if (source is ExtendableQuery<T>)
                {
                    return (ExtendableQuery<T>)source;
                }

                return new ExtendableQueryProvider(source.Provider).CreateQuery<T>(source.Expression);
            }
        }

        class ExtendableVisitor : System.Linq.Expressions.ExpressionVisitor
        {
            IQueryProvider _provider;

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

                    Expression exp = ((IQueryable)node.Method.Invoke(null, args)).Expression;

                    return exp;
                }

                return base.VisitMethodCall(node);
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

        // Usage
        // YOUR_OBJECT_SET.AsExtendable().ExampleMethod( ... );
    }
}
