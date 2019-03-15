using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Collections;
using System.Reflection;
#if !(NET35 || NOEF)
using System.Threading;
using System.Threading.Tasks;
#if EFCORE
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query.Internal;
#else
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
#endif
#endif

namespace LinqKit
{
    // An IEnumerator<T> that applies the AsSafe() paradigm, knowing that
    // normally the exception happens only on the first MoveFirst().
    public class SafeEnumerator<T> : IEnumerator<T>
    {
        protected readonly ExpandableQuery<T> SafeQueryable_;

        protected IEnumerator<T> Enumerator { get; set; }

        public SafeEnumerator(ExpandableQuery<T> safeQueryable)
        {
            SafeQueryable_ = safeQueryable;
        }

        public T Current
        {
            get
            {
                return Enumerator != null ? Enumerator.Current : default(T);
            }
        }

        public void Dispose()
        {
            if (Enumerator != null)
            {
                Enumerator.Dispose();
            }
        }

        object IEnumerator.Current
        {
            get { return Current; }
        }

        public bool MoveNext()
        {
            // We handle exceptions only on first MoveNext()
            if (Enumerator != null)
            {
                return Enumerator.MoveNext();
            }

            try
            {
                // We try executing it directly
                Enumerator = SafeQueryable_._inner.GetEnumerator();
                bool result = Enumerator.MoveNext();

                // Success!
                // SafeQueryable_.IsSafe = true;
                return result;
            }
            catch (NotSupportedException e1)
            {
                // Clearly there was a NotSupportedException: -)
                Tuple<IEnumerator<T>, bool, T> result = SafeQueryable_.HandleEnumerationFailure<T>(e1, SafeQueryable_._inner.Expression, false);

                if (result == null)
                {
                    throw;
                }

                Enumerator = result.Item1;
                return result.Item2;
            }
        }

        public void Reset()
        {
            if (Enumerator != null)
            {
                Enumerator.Reset();
            }
        }
    }

    /// <summary>
    /// An IQueryable wrapper that allows us to visit the query's expression tree just before LINQ to SQL gets to it.
    /// This is based on the excellent work of Tomas Petricek: http://tomasp.net/blog/linq-expand.aspx
    /// </summary>
#if (NET35 || NOEF)
    public sealed class ExpandableQuery<T> : IQueryable<T>, IOrderedQueryable<T>, IOrderedQueryable
#elif EFCORE
    public class ExpandableQuery<T> : IQueryable<T>, IOrderedQueryable<T>, IOrderedQueryable, IAsyncEnumerable<T>
#else
    public class ExpandableQuery<T> : IQueryable<T>, IOrderedQueryable<T>, IOrderedQueryable, IDbAsyncEnumerable<T>
#endif
    {
        //readonly ExpandableQueryProvider<T> _provider;
        readonly IQueryProvider _provider;
        public readonly IQueryable<T> _inner;

        internal IQueryable<T> InnerQuery => _inner; // Original query, that we're wrapping

        internal ExpandableQuery(IQueryable<T> inner, Func<Expression, Expression> queryOptimizer)
        {
            _inner = inner;
            _provider = new ExpandableQueryProvider<T>(this, queryOptimizer);
        }

        // Gets the T of IQueryablelt;T&gt;
        protected static Type GetIQueryableTypeArgument(Type type)
        {
            IEnumerable<Type> interfaces = type.GetTypeInfo().IsInterface ? new[] { type }.Concat(type.GetInterfaces()) : type.GetInterfaces();

            Type argument = (from x in interfaces
                where x.GetTypeInfo().IsGenericType
                let gt = x.GetGenericTypeDefinition()
                where gt == typeof(IQueryable<>)
                select x.GetGenericArguments()[0]).FirstOrDefault();

            return argument;
        }

        // Is used both indirectly by GetEnumerator() and by Execute<>.
        // The returned Tuple<,,> has the first two elements that are valid
        // when used by the GetEnumerator() and the last that is valid
        // when used by Execute<>.
        public Tuple<IEnumerator<T>, bool, TResult> HandleEnumerationFailure<TResult>(NotSupportedException e1, Expression expression, bool singleResult)
        {
            // We "augment" the exception with the full stack trace
            //AugmentStackTrace(e1, 3);

            //if (SafeQueryable.Logger != null)
            //{
            //    SafeQueryable.Logger(this, expression, e1);
            //}

            // We save this first exception
            var Exception = e1;

            {
                var query = _inner;

                MethodInfo executeSplittedMethod = typeof(ExpandableQuery<T>).GetMethod("ExecuteSplitted", BindingFlags.Instance | BindingFlags.NonPublic);

                MethodCallExpression call;
                Expression innerExpression = expression;
                Type iqueryableArgument;

                // We want to check that there is a MethodCallExpression with 
                // at least one argument, and that argument is an Expression
                // of type IQueryable<iqueryableArgument>, and we save the
                // iqueryableArgument
                while ((call = innerExpression as MethodCallExpression) != null &&
                    call.Arguments.Count > 0 &&
                    (innerExpression = call.Arguments[0] as Expression) != null &&
                    (iqueryableArgument = GetIQueryableTypeArgument(innerExpression.Type)) != null)
                {
                    try
                    {
                        Tuple<IEnumerator<T>, bool, TResult> result2 = (Tuple<IEnumerator<T>, bool, TResult>)executeSplittedMethod.MakeGenericMethod(iqueryableArgument, typeof(TResult)).Invoke(this, new object[] { expression, call, innerExpression, singleResult });
                        return result2;
                    }
                    catch (TargetInvocationException e2)
                    {
                        if (!(e2.InnerException is NotSupportedException))
                        {
                            throw;
                        }
                    }
                }

                return null;
            }
        }

        /// <inheritdoc cref="IQueryable.Expression"/>
        public Expression Expression
        {
            get
            {
                return _inner.Expression;
            }
        }

        /// <inheritdoc cref="IQueryable.ElementType"/>
        public Type ElementType
        {
            get
            {
                return typeof(T);
            }
        }

        /// <inheritdoc cref="IQueryable.Provider"/>
        public IQueryProvider Provider
        {
            get
            {
                return _provider;
            }
        }

        /// <inheritdoc cref="IEnumerable{T}.GetEnumerator"/>
        public IEnumerator<T> GetEnumerator()
        {
            return _inner.GetEnumerator();
            //return ((IEnumerable<T>)Provider.Execute(Expression)).GetEnumerator();
        }

        /// <inheritdoc cref="IEnumerable.GetEnumerator"/>
        IEnumerator IEnumerable.GetEnumerator()
        {
            // return ((IEnumerable)Provider.Execute(Expression)).GetEnumerator();
            //return new SafeEnumerator<T>(this);
            return _inner.GetEnumerator();
        }

        /// <summary>
        /// IQueryable string presentation.
        /// </summary>
        public override string ToString() { return _inner.ToString(); }

#if !(NET35 || NOEF)
#if EFCORE
        IAsyncEnumerator<T> IAsyncEnumerable<T>.GetEnumerator()
        {
            if (_inner is IAsyncEnumerable<T>)
            {
                return ((IAsyncEnumerable<T>)_inner).GetEnumerator();
            }

            return (_inner as IAsyncEnumerableAccessor<T>)?.AsyncEnumerable.GetEnumerator();
        }
#else
        /// <summary> Enumerator for async-await </summary>
        public IDbAsyncEnumerator<T> GetAsyncEnumerator()
        {
            var asyncEnumerable = _inner as IDbAsyncEnumerable<T>;
            if (asyncEnumerable != null)
            {
                return asyncEnumerable.GetAsyncEnumerator();
            }
            return new ExpandableDbAsyncEnumerator<T>(_inner.GetEnumerator());
        }

        IDbAsyncEnumerator IDbAsyncEnumerable.GetAsyncEnumerator()
        {
            return GetAsyncEnumerator();
        }
#endif
#endif
    }

#if !(NET35 || NOEF)
    internal class ExpandableQueryOfClass<T> : ExpandableQuery<T> where T : class
    {
        public ExpandableQueryOfClass(IQueryable<T> inner, Func<Expression, Expression> queryOptimizer) : base(inner, queryOptimizer)
        {
        }

#if EFCORE
        public IQueryable<T> Include<TProperty>(Expression<Func<T, TProperty>> navigationPropertyPath)
        {
            return ((IQueryable<T>)InnerQuery.Include(navigationPropertyPath)).AsExpandable();
        }
#else
        public IQueryable<T> Include(string path)
        {
            return InnerQuery.Include(path).AsExpandable();
        }
#endif
    }
#endif

    internal class ExpandableQueryProvider<T> : IQueryProvider
#if NET35 || NOEF
#elif EFCORE
        , IAsyncQueryProvider
#else
        , IDbAsyncQueryProvider
#endif
    {
        readonly ExpandableQuery<T> _query;
        readonly Func<Expression, Expression> _queryOptimizer;

        internal ExpandableQueryProvider(ExpandableQuery<T> query, Func<Expression, Expression> queryOptimizer)
        {
            _query = query;
            _queryOptimizer = queryOptimizer;
        }

        // The following four methods first call ExpressionExpander to visit the expression tree, then call
        // upon the inner query to do the remaining work.
        IQueryable<TElement> IQueryProvider.CreateQuery<TElement>(Expression expression)
        {
            var expanded = expression.Expand();
            var optimized = _queryOptimizer(expanded);
            return _query.InnerQuery.Provider.CreateQuery<TElement>(optimized).AsExpandable();
        }

        public IQueryable CreateQuery(Expression expression)
        {
            return _query.InnerQuery.Provider.CreateQuery(expression.Expand());
        }

        public TResult Execute<TResult>(Expression expression)
        {
            var expanded = expression.Expand();
            var optimized = _queryOptimizer(expanded);
            var e =  _query.InnerQuery.Provider.Execute<TResult>(optimized);
            return e;
        }

        public object Execute(Expression expression)
        {
            var expanded = expression.Expand();
            var optimized = _queryOptimizer(expanded);
            var e =  _query.InnerQuery.Provider.Execute(optimized);
            return e;
        }

#if !(NET35 || NOEF)
#if EFCORE
        public IAsyncEnumerable<TResult> ExecuteAsync<TResult>(Expression expression)
        {
            var asyncProvider = _query.InnerQuery.Provider as IAsyncQueryProvider;
            return asyncProvider.ExecuteAsync<TResult>(expression.Expand());
        }
#else
        public Task<object> ExecuteAsync(Expression expression, CancellationToken cancellationToken)
        {
            var asyncProvider = _query.InnerQuery.Provider as IDbAsyncQueryProvider;
            var expanded = expression.Expand();
            var optimized = _queryOptimizer(expanded);
            if (asyncProvider != null)
            {
                return asyncProvider.ExecuteAsync(optimized, cancellationToken);
            }
            return Task.FromResult(_query.InnerQuery.Provider.Execute(optimized));
        }
#endif

        public Task<TResult> ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken)
        {
#if EFCORE
            var asyncProvider = _query.InnerQuery.Provider as IAsyncQueryProvider;
#else
            var asyncProvider = _query.InnerQuery.Provider as IDbAsyncQueryProvider;
#endif
            var expanded = expression.Expand();
            var optimized = _queryOptimizer(expanded);
            if (asyncProvider != null)
            {
                return asyncProvider.ExecuteAsync<TResult>(optimized, cancellationToken);
            }

            return Task.FromResult(_query.InnerQuery.Provider.Execute<TResult>(optimized));
        }
#endif
    }
}