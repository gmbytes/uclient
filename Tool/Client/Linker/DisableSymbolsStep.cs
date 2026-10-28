using System;
using Mono.Linker.Steps;

namespace Client.Build.Linker
{
    // Runs before the official output step; only the symbol writer policy changes.
    public sealed class DisableSymbolsStep : BaseStep
    {
        protected override void Process()
        {
            Context.LinkSymbols = false;
            Console.WriteLine("F6_LINKER_SYMBOLS_DISABLED");
        }
    }
}
