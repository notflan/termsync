using System;
using System.Threading.Tasks;
using System.Threading;
using System.Threading.Channels;

using termsync.Tools;
using System.Collections.Generic;

namespace termsync
{
    /// <summary>
    /// A Write container that aquires a mutex on `Terminal`, preventing other writes while held.
    /// </summary>
    public sealed class TermLock : IDisposable
    {
        private readonly CancellationTokenSource cancel;
        private readonly AsyncMutex GlobalMutex;
        private IDisposable held_lock = null;
        internal TermLock(AsyncMutex mutex, CancellationToken token=default)
        {
            GlobalMutex = mutex;

            cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
        }

        internal async Task Acquire()
        {
            held_lock = await GlobalMutex.AcquireAsync(cancel.Token);
        }

        /// <summary>
        /// Write a line with lock held.
        /// </summary>
        public Task WriteLine(string line)
        {
            return Terminal.WriteLineAndWait(line, cancel.Token);
        }

        public void Dispose()
        {
            cancel.Cancel();
            cancel.Dispose();
            if(held_lock!=null)
            {
                held_lock.Dispose();
                held_lock = null;
            }
        }

        ~TermLock()
        {
            if (held_lock != null)
            {
                held_lock.Dispose();
                held_lock = null;
            }
        }
    }

    public sealed class TermStage : IAsyncDisposable
    {
        private readonly AsyncMutex mutex = new AsyncMutex();
        private readonly List<string> lines = new List<string>();
        private readonly CancellationTokenSource cancel;
        private readonly AsyncMutex globalMutex;
        private readonly CancellationToken originalToken;

        internal TermStage(AsyncMutex globalM, CancellationToken token)
        {
            originalToken = token;
            globalMutex = globalM;
            cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
        }

        public async Task WriteLine(string line)
        {
            using (await mutex.AcquireAsync(cancel.Token))
            {
                lines.Add(line);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await mutex.AcquireAsync(cancel.Token);

            cancel.Cancel();

            mutex.Dispose();
            cancel.Dispose();

            using (await globalMutex.AcquireAsync(originalToken))
            {
                foreach (var line in lines)
                {
                    await Terminal.WriteLineAndWait(line, originalToken);
                }
            }

            lines.Clear();
        }
    }

    //TODO: WriteLine staging that doesn't block until read to commit all.

    /// <summary>
    /// Terminal control global state.
    /// </summary>
    public static partial class Terminal
    {
        #region Sync
        private static readonly CancellationTokenSource CancelAll = new CancellationTokenSource();
        private static ChannelReader<string> Input;
        private static ChannelWriter<ControlValue> Output;

        private static readonly AsyncMutex ConsoleMutex = new AsyncMutex();

        #endregion
        #region Buffer
        private static readonly List<char> InputBuffer = new List<char>();
        /// <summary>
        /// The place in the <see cref="InputBuffer"/> that the user is writing to.
        /// </summary>
        private static int InputAt = -1;
        public static string Prompt { get; private set; } = "> ";
        private static bool WriteLineOnFlush = true;

        #endregion

        #region Control
        private static readonly AsyncMutex UserWriteMutex = new AsyncMutex();
        private static readonly AsyncMutex UserReadMutex = new AsyncMutex();

        /// <summary>
        /// Acquire global Write lock mutex.
        /// </summary>
        public static async Task<TermLock> Lock()
        {
            var l = new TermLock(UserWriteMutex, CancelAll.Token);
            await l.Acquire();
            return l;
        }

        /// <summary>
        /// Create a staging container that will write all lines at once on DisposeAsync().
        /// </summary>
        public static TermStage Stage()
        {
            return new TermStage(UserWriteMutex, CancelAll.Token);
        }

        private static async Task<object> SendAndWait(ControlValue value, CancellationToken token = default)
        {
            TaskCompletionSource<bool> onCancel = new TaskCompletionSource<bool>();
            using var reg = token.Register(() =>
            {
                onCancel.SetResult(false);
            });
            await Output.WriteAsync(value, token);


            if (await Task.WhenAny(value.Processed.Task, onCancel.Task) == value.Processed.Task)
            {
                return value.Processed.Task.Result;
            }
            else throw new OperationCanceledException();
        }
        /// <summary>
        /// Write a line to the Terminal and wait for it to appear.
        /// </summary>
        /// <param name="line">The line</param>
        public static async Task WriteLine(string line)
        {
            using(await UserWriteMutex.AcquireAsync(CancelAll.Token))
            {
                await SendAndWait(new ControlValue(ControlType.Print, line), CancelAll.Token);
            }
        }
        internal static ValueTask WriteLine(string str, CancellationToken token)
        {
            return Output.WriteAsync(new ControlValue(ControlType.Print, str), token);
        }
        internal static Task WriteLineAndWait(string str, CancellationToken token)
        {
            return SendAndWait(new ControlValue(ControlType.Print, str), token);
        }

        /// <summary>
        /// Read a line from the user.
        /// </summary>
        public static async Task<string> ReadLine(CancellationToken token=default)
        {
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(CancelAll.Token, token);
            token = cancel.Token;

            using (await UserReadMutex.AcquireAsync(token))
            {
               return await Input.ReadAsync(token);
            }
        }

        /// <summary>
        /// Change the user prompt.
        /// </summary>
        /// <param name="prompt">New prompt.</param>
        public static async Task ChangePrompt(string prompt)
        {
            using (await UserWriteMutex.AcquireAsync(CancelAll.Token))
            {
                await SendAndWait(new ControlValue(ControlType.ChangePrompt, prompt), CancelAll.Token);
            }
        }

        #endregion

        /// <summary>
        /// Initialise terminal control. Writes go through <see cref="Terminal"/> after this.
        /// </summary>
        /// <returns>The control task.</returns>
        public static Task Initialise()
        {

            var input = Channel.CreateUnbounded<string>();
            var output = Channel.CreateBounded<ControlValue>(10);

            Input = input.Reader;
            Output = output.Writer;

            AppDomain.CurrentDomain.ProcessExit += (_, __) => Cleanup();
            var t_output = Task.Run(async () =>
            {
                try
                {
                    await ctrl_output(output.Reader, input.Writer);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    if (!CancelAll.IsCancellationRequested)
                        CancelAll.Cancel();
                }
            });

            var t_input = Task.Run(async () =>
            {
                try
                {
                    await ctrl_intput();
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    if(!CancelAll.IsCancellationRequested)
                    CancelAll.Cancel();
                }
            });

            return Task.Run(async () =>
            {
                await Task.WhenAll(t_input, t_output);
            });
        }

        private static async Task ctrl_output(ChannelReader<ControlValue> output, ChannelWriter<string> lines)
        {
            await foreach(var value in output.ReadAllAsync(CancelAll.Token))
            {
                object result = null;
                try
                {
                    switch (value.Type)
                    {
                        case ControlType.Print when value.Value is string line:
                            using (await ConsoleMutex.AcquireAsync(CancelAll.Token))
                            {
                                Mutation.ClearInputBuffer();
                                Console.WriteLine(line);
                                Mutation.RedrawInputBuffer();
                            }
                            break;
                        case ControlType.Move:
                            using (await ConsoleMutex.AcquireAsync(CancelAll.Token))
                            {
                                if (InputAt == -1) InputAt = InputBuffer.Count;
                                switch ((MoveDirection)value.Value)
                                {
                                    case MoveDirection.Left:
                                        if (InputAt > 0)
                                        {
                                            InputAt -= 1;
                                            Console.CursorLeft -= 1;
                                        }
                                        break;
                                    case MoveDirection.Right:
                                        if (InputAt < InputBuffer.Count)
                                        {
                                            InputAt += 1;
                                            Console.CursorLeft += 1;
                                        }
                                        break;
                                }
                                if (InputAt >= InputBuffer.Count)
                                    InputAt = -1;
                            }
                            break;
                        case ControlType.Delete:
                            using (await ConsoleMutex.AcquireAsync(CancelAll.Token))
                            {
                                switch ((DeleteLocation)value.Value)
                                {
                                    case DeleteLocation.After:
                                        if (InputBuffer.Count > 0)
                                        {
                                            if (InputAt >= 0 && InputAt < InputBuffer.Count)
                                            {
                                                InputBuffer.RemoveAt(InputAt);

                                                Console.Write(" \b");
                                                Mutation.MoveBackOne();
                                            }
                                        }
                                        break;
                                    case DeleteLocation.Before:
                                        if (InputBuffer.Count > 0)
                                        {
                                            if (InputAt > 0 && InputAt < InputBuffer.Count)
                                            {
                                                InputBuffer.RemoveAt(InputAt - 1);
                                                InputAt -= 1;

                                                Console.Write("\b");
                                                Mutation.MoveBackOne();
                                            }
                                            else if (InputAt != 0)
                                            {
                                                InputBuffer.RemoveAt(InputBuffer.Count - 1);
                                                Console.Write("\b \b");
                                            }
                                        }
                                        break;
                                }
                            }
                            break;
                        case ControlType.Commit:
                            using (await ConsoleMutex.AcquireAsync(CancelAll.Token))
                            {
                                await Mutation.FlushAsync(lines, CancelAll.Token);
                            }
                            break;
                        case ControlType.Echo:
                            using (await ConsoleMutex.AcquireAsync(CancelAll.Token))
                            {
                                var input = value.Value as string;
                                if (input == null) continue;
                                for (int i = 0; i < input.Length; i++)
                                {
                                    switch (input[i])
                                    {
                                        case '\n':
                                        case '\r':
                                            //Should be handled by `Commit`, something went wrong if they end up here.
                                            break;
                                        case '\b':
                                            //Ditto for `Delete`.
                                            break;
                                        default:
                                            {
                                                if (((InputAt == -1) ? Console.CursorLeft : Console.CursorLeft + (InputBuffer.Count - InputAt)) >= Console.BufferWidth - 1) break; //TODO: Scrolling buffer?

                                                if (InputAt >= 0 && InputAt < InputBuffer.Count)
                                                {
                                                    InputBuffer.Insert(InputAt, input[i]);
                                                    InputAt += 1;

                                                    Mutation.MoveForwardOne();
                                                }
                                                else
                                                {
                                                    InputBuffer.Add(input[i]);
                                                }
                                                Console.Write(input[i]);
                                            }
                                            break;
                                    }
                                }
                            }
                            break;
                        case ControlType.ChangePrompt:
                            using (await ConsoleMutex.AcquireAsync(CancelAll.Token))
                            {
                                Prompt = (string)value.Value;

                                Mutation.ClearInputBuffer();
                                Mutation.RedrawInputBuffer();
                            }
                            break;
                    }
                }
                finally
                {
                    value.Processed.SetResult(result);
                }
            }

            lines.Complete();
            InputBuffer.Clear();
        }
        /// <summary>
        /// Read from the Console and pipe buffer control events to `Output`.
        /// </summary>
        private static async Task ctrl_intput()
        {
            while (!CancelAll.IsCancellationRequested)
            {
                var key = Console.ReadKey(true); // This blocks.

                if (CancelAll.IsCancellationRequested) break;
                switch (key.Key)
                {
                    case ConsoleKey.LeftArrow:
                        await Output.WriteAsync(new ControlValue(ControlType.Move, MoveDirection.Left), CancelAll.Token);

                        break;
                    case ConsoleKey.RightArrow:
                        await Output.WriteAsync(new ControlValue(ControlType.Move, MoveDirection.Right), CancelAll.Token);

                        break;
                    case ConsoleKey.Delete:
                        await Output.WriteAsync(new ControlValue(ControlType.Delete, DeleteLocation.After), CancelAll.Token);
                        break;
                    case ConsoleKey.Backspace:
                        await Output.WriteAsync(new ControlValue(ControlType.Delete, DeleteLocation.Before), CancelAll.Token);
                        break;
                    case ConsoleKey.Enter:
                        await Output.WriteAsync(new ControlValue(ControlType.Commit, null), CancelAll.Token);
                        break;
                    default:
                        await Output.WriteAsync(new ControlValue(ControlType.Echo, new string(new[] { key.KeyChar })), CancelAll.Token);
                        break;
                }

            }
        }

        /// <summary>
        /// Close all <see cref="Terminal"/> channels.
        /// </summary>
        public static void Close()
        {
            Output.Complete();
        }
        private static void Cleanup()
        {
            try
            {
                Output.Complete();
            }
            catch { }
            if (!CancelAll.IsCancellationRequested)
                CancelAll.Cancel();

            CancelAll.Dispose();
            ConsoleMutex.Dispose();
        }
    }
}
