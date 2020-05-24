using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;

namespace termsync.Tools
{
    sealed class AsyncMutex : IDisposable
    {
        private readonly SemaphoreSlim sem;

        public AsyncMutex()
        {
            sem = new SemaphoreSlim(1, 1);
        }
        private AsyncMutex(SemaphoreSlim from)
        {
            sem = from;
        }

        private class Lock : IDisposable
        {
            public AsyncMutex Parent { get; }
            public Lock(AsyncMutex held)
            {
                Parent = held;
            }

            public void Dispose()
            {
                Parent.sem.Release();
            }
        }

        public IDisposable Acquire(int msTimeout, CancellationToken token = default)
        {
            sem.Wait(msTimeout, token);
            return new Lock(this);
        }

        public IDisposable Acquire(CancellationToken token)
        {
            sem.Wait(token);
            return new Lock(this);
        }

        public IDisposable Acquire()
        {
            sem.Wait();
            return new Lock(this);
        }

        public async Task<IDisposable> AcquireAsync(int msTimeout, CancellationToken token = default)
        {
            await sem.WaitAsync(msTimeout, token);
            return new Lock(this);
        }

        public async Task<IDisposable> AcquireAsync(CancellationToken token = default)
        {
            await sem.WaitAsync(token);
            return new Lock(this);
        }

        public void Dispose()
        {
            sem.Dispose();
        }

        public static AsyncMutex Semaphore(int count, int max)
        {
            SemaphoreSlim sem = new SemaphoreSlim(count, max);
            return new AsyncMutex(sem);
        }
        public static AsyncMutex Semaphore(int count)
         => Semaphore(count, count);
    }

    static class ExAsyncTools
    {
        #region Then()
        public static async Task Then(this Task t, Action lam)
        {
            await t;
            lam();
        }
        public static Task Then(this Task t, Action lam, CancellationToken token)
            => Then(t, (_) => lam(), token);
        public static async Task Then(this Task t, Action<CancellationToken> lam, CancellationToken token)
        {
            TaskCompletionSource<bool> fail = new TaskCompletionSource<bool>();

            using (token.Register(() => fail.TrySetResult(false)))
            {
                if ((await Task.WhenAny(t, fail.Task)) == t)
                {
                    lam(token);
                }
                token.ThrowIfCancellationRequested();
            }
        }
        public static async Task Then(this Task t, Func<Task> lam)
        {
            await t;
            await lam();
        }
        public static Task Then(this Task t, Func<Task> lam, CancellationToken token)
            => Then(t, (_) => lam(), token);
        public static async Task Then(this Task t, Func<CancellationToken, Task> lam, CancellationToken token)
        {
            TaskCompletionSource<bool> fail = new TaskCompletionSource<bool>();

            using (token.Register(() => fail.TrySetResult(false)))
            {
                if ((await Task.WhenAny(t, fail.Task)) == t)
                {
                    await lam(token);
                }
                token.ThrowIfCancellationRequested();
            }
        }
        public static async Task<T> Then<T>(this Task<T> t, Action<T> lam)
        {
            T res = await t;
            lam(res);
            return res;
        }
        public static Task<T> Then<T>(this Task<T> t, Action<T> lam, CancellationToken token)
            => Then(t, (v, _) => lam(v), token);
        public static async Task<T> Then<T>(this Task<T> t, Action<T, CancellationToken> lam, CancellationToken token)
        {
            TaskCompletionSource<bool> fail = new TaskCompletionSource<bool>();

            using (token.Register(() => fail.TrySetResult(false)))
            {
                if ((await Task.WhenAny(t, fail.Task)) == t)
                {
                    var res = t.Result;
                    lam(res, token);
                    return res;
                }
                throw new OperationCanceledException();
            }
        }
        public static async Task<T> Then<T>(this Task<T> t, Func<T, T> lam)
        {
            T res = await t;
            return lam(res);
        }
        public static Task<T> Then<T>(this Task<T> t, Func<T, T> lam, CancellationToken token)
            => Then(t, (v, _) => lam(v), token);
        public static async Task<T> Then<T>(this Task<T> t, Func<T, CancellationToken, T> lam, CancellationToken token)
        {
            TaskCompletionSource<bool> fail = new TaskCompletionSource<bool>();

            using (token.Register(() => fail.TrySetResult(false)))
            {
                if ((await Task.WhenAny(t, fail.Task)) == t)
                {
                    var res = t.Result;
                    res = lam(res, token);
                    return res;
                }
                throw new OperationCanceledException();
            }
        }
        public static async Task<T> Then<T>(this Task<T> t, Func<T, Task<T>> lam)
        {
            return await lam(await t);
        }
        public static Task<T> Then<T>(this Task<T> t, Func<T, Task<T>> lam, CancellationToken token)
            => Then(t, (x, _) => lam(x), token);
        public static async Task<T> Then<T>(this Task<T> t, Func<T, CancellationToken, Task<T>> lam, CancellationToken token)
        {
            TaskCompletionSource<bool> fail = new TaskCompletionSource<bool>();

            using (token.Register(() => fail.TrySetResult(false)))
            {
                if ((await Task.WhenAny(t, fail.Task)) == t)
                {
                    var res = t.Result;
                    res = await lam(res, token);
                    return res;
                }
                throw new OperationCanceledException();
            }
        }
        #endregion

        public static CancellationTokenSource Link(this in CancellationToken token, params CancellationToken[] others)
        {
            return CancellationTokenSource.CreateLinkedTokenSource(new CancellationToken[] { token }.Concat(others).ToArray());
        }


    }


}
