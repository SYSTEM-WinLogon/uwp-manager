using System;
using System.Linq;
using System.Threading;
using UwpManager;

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "list";

try
{
    switch (command)
    {
        case "list":
        {
            var items = await AppxService.LoadAsync(null, CancellationToken.None);
            foreach (var p in items.Where(p => p.IdName.Contains("SmokeTest", StringComparison.OrdinalIgnoreCase)))
                Console.WriteLine($"{p.Name} | {p.FullName} | v{p.VersionText} | {p.SizeText} | {p.KindText}");
            Console.WriteLine($"TOTAL={items.Count}");
            break;
        }
        case "install":
            await AppxService.InstallAsync(args[1]);
            Console.WriteLine("INSTALL_OK");
            break;
        case "uninstall":
            await AppxService.UninstallAsync(args[1]);
            Console.WriteLine("UNINSTALL_OK");
            break;
        case "probe":
        {
            try
            {
                var pm = new Windows.Management.Deployment.PackageManager();
                int n = 0;
                foreach (var _ in pm.FindPackagesForUser("")) n++;
                Console.WriteLine("PROBE_OK count=" + n);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PROBE_FAIL {ex.GetType().FullName} 0x{ex.HResult:X8} inner={ex.InnerException?.GetType().Name}:{ex.InnerException?.HResult:X8} msg={ex.Message}");
            }
            break;
        }
        case "search":
        {
            var items = await AppxService.LoadAsync(null, CancellationToken.None);
            var matched = items.Where(p => p.Matches(args[1])).ToList();
            foreach (var p in matched.Take(5))
                Console.WriteLine($"MATCH {p.Name} | {p.FullName} | {p.SizeText}");
            Console.WriteLine($"MATCHED={matched.Count}");
            break;
        }
        default:
            Console.WriteLine("usage: SmokeTest list | install <path> | uninstall <PackageFullName>");
            return 1;
    }
    return 0;
}
catch (AppxOperationException ex)
{
    Console.WriteLine($"APPX_ERROR {ex.CodeText} {ex.Message}");
    if (!string.IsNullOrWhiteSpace(ex.Hint)) Console.WriteLine("HINT " + ex.Hint);
    return 2;
}
catch (Exception ex)
{
    Console.WriteLine("ERROR " + ex);
    return 3;
}
