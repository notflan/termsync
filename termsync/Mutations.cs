using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace termsync
{
    static partial class Terminal
    {
        private static class Mutation
        {
            public static void ClearInputBuffer()
            {
                Console.CursorLeft = 0;
                Console.Write(new string(' ', Console.WindowWidth - 1));
                Console.CursorLeft = 0;
            }

            public static void RedrawInputBuffer(bool prompt=true)
            {
                if(prompt)
                    Console.Write(Prompt);

                if (InputBuffer.Count > 0)
                    Console.Write(InputBuffer.ToArray());
                if (InputAt > -1)
                    Console.CursorLeft -= InputBuffer.Count - InputAt;
            }

            public static void MoveBackOne()
            {
                Console.MoveBufferArea(Console.CursorLeft + 1, Console.CursorTop, InputBuffer.Count - InputAt, 1, Console.CursorLeft, Console.CursorTop);
            }
            public static void MoveForwardOne()
            {
                Console.MoveBufferArea(Console.CursorLeft, Console.CursorTop, InputBuffer.Count - InputAt, 1, Console.CursorLeft + 1, Console.CursorTop);
            }

            public static async Task FlushAsync(ChannelWriter<string> lines, CancellationToken token =default)
            {
                ClearInputBuffer();
                var inp = new string(InputBuffer.ToArray());
                var line_send= lines.WriteAsync(inp, token);
                InputBuffer.Clear();
                InputAt = -1;

                RedrawInputBuffer();

                if (WriteLineOnFlush)
                    await WriteLine(Prompt + inp, token);

                await line_send;
            }
        }
    }
}
