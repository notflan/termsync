using System;
using System.Threading.Tasks;
using termsync;

namespace test
{
    class Program
    {
        static async Task Main(string[] args)
        {
            _ = Terminal.Initialise();

            await Terminal.WriteLine("Hiya!");
            await Terminal.WriteLine("Another line...");
            var line = await Terminal.ReadLine();
            await Terminal.WriteLine("Line: " + line);

            await Task.WhenAll(Task.Run(async () =>
            {
                await using (var stage = Terminal.Stage())
                {
                    await stage.WriteLine("One line");
                    await stage.WriteLine("Two line");
                    await stage.WriteLine("Three line");
                    await stage.WriteLine("Four");
                }
            }),
            Task.Run(async () =>
            {
                using (var lck = await Terminal.Lock())
                {
                    await lck.WriteLine("One line locked");
                    await lck.WriteLine("Two line locked");
                    await lck.WriteLine("Three line locked");
                    await lck.WriteLine("Four locked");
                }
            }));

            await Terminal.ChangePrompt("USER> " + await Terminal.ReadLine()+":: ");

            await Terminal.WriteLine("New prompt!");
            await Terminal.ReadLine();

            Terminal.Close();
        }
    }
}
