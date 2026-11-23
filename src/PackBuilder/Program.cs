using System;
using System.IO;
using System.Linq;
using WifiFix.Core;

class ConsoleLog : ILog
{
    public void Info(string m) => Console.WriteLine(m);
    public void Warn(string m) => Console.WriteLine("WARNING: " + m);
}

static class Program
{
    static int Main(string[] args)
    {
        Console.WriteLine("PackBuilder v" + AppInfo.Version);
        // Double-clicked (no arguments): do the useful thing and keep the window open.
        var doubleClicked = args.Length == 0;
        if (doubleClicked) args = new[] { "fetch", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "drivers") };
        var fetch = args.Length == 2 && args[0].Equals("fetch", StringComparison.OrdinalIgnoreCase);
        var root = fetch ? args[1] : (args.Length == 1 ? args[0] : "");
        if (root.Length == 0)
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  PackBuilder fetch <drivers folder>   download/refresh WiFi drivers for 7th-14th gen PCs (needs internet), then rebuild index");
            Console.WriteLine("  PackBuilder <drivers folder>         just rebuild <drivers folder>\\index.json");
            return 1;
        }
        if (fetch)
        {
            var rep = new PackUpdater(new MsCatalogSource(root), new ConsoleLog()).Run(root, KnownChips.All.Concat(KnownChips.Extras));
            Console.WriteLine("Done: " + rep.Downloaded + " downloaded, " + rep.UpToDate + " up to date, " +
                              rep.NotFound + " not in catalog, " + rep.Errors + " errors. See PACK-REPORT.txt");
        }
        Directory.CreateDirectory(root);
        var idx = PackIndex.Build(root);
        idx.Save(Path.Combine(root, "index.json"));
        Console.WriteLine("Indexed " + idx.Count + " hardware IDs.");
        if (doubleClicked) { Console.WriteLine(); Console.WriteLine("Finished. Press any key to close."); Console.ReadKey(); }
        return 0;
    }
}
