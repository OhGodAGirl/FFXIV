using System.Reflection;
using Lumina.Excel.Sheets;

namespace Exportaramus.ApiContractTests;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "facewear-game-data")
        {
            using var game = new Lumina.GameData(args[1]);
            var glasses = game.GetExcelSheet<Glasses>()!.Where(x => x.RowId != 0).ToArray();
            var styles = glasses.Select(x => checked(x.Style.RowId + 1)).Distinct().Order().ToArray();
            Console.WriteLine($"Glasses rows: {glasses.Length}; maximum ID: {glasses.Max(x => x.RowId)}");
            Console.WriteLine($"Canonical style IDs ({styles.Length}): {string.Join(',', styles)}");
            foreach (var id in new uint[] { 59, 61 })
                Console.WriteLine($"Canonical style {id}: Glasses rows {string.Join(',', glasses.Where(x => x.Style.RowId + 1 == id).Select(x => x.RowId))}");
            return glasses.All(x => x.RowId <= ushort.MaxValue) && styles.All(x => x > 0) ? 0 : 1;
        }
        if (args.Length == 2 && args[0] == "wardrobe-game-data")
        {
            using var game = new Lumina.GameData(args[1]);
            var items = game.GetExcelSheet<Item>()!;
            foreach (var id in new uint[] { 21800, 3363 })
                Console.WriteLine($"Item {id}: {items.GetRow(id).Name}");
            var glasses = game.GetExcelSheet<Glasses>()!.Where(x => x.RowId != 0).ToArray();
            Console.WriteLine($"Glasses rows: {glasses.Length}; maximum ID: {glasses.Max(x => x.RowId)}; style domain: {string.Join(',', glasses.Select(x => x.Style.RowId).Distinct().Order())}");
            return items.GetRow(21800).Name.ToString() == "Glamour Prism" ? 0 : 1;
        }
        if (args.Length == 4 && args[0] == "verify-game-data")
            return LocalGameDataVerification.Run(args[1], args[2], args[3]);
        if (args.Length > 2 && args[0] == "native-metadata")
        {
            var assembly = Assembly.LoadFrom(args[1]);
            foreach (var name in args.Skip(2))
            {
                var type = assembly.GetType(name)!;
                Console.WriteLine(name + ": " + string.Join("; ", type.GetProperties().Select(p => p.PropertyType + " " + p.Name).Concat(type.GetFields().Select(p => p.FieldType + " " + p.Name))));
            }
            return 0;
        }
        if (args.Length == 4 && args[0] == "method-evidence")
        {
            var assembly = Assembly.LoadFrom(args[1]);
            var type = assembly.GetType(args[2])!;
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Where(m => m.Name == args[3]))
            {
                var body = method.GetMethodBody();
                Console.WriteLine(method + ": exceptionHandlers=" + body?.ExceptionHandlingClauses.Count + "; il=" + Convert.ToHexString(body?.GetILAsByteArray() ?? []));
                foreach (var clause in body?.ExceptionHandlingClauses ?? []) Console.WriteLine("Handler: " + clause.Flags);
                var il = body?.GetILAsByteArray() ?? [];
                for (var offset = 0; offset + 4 < il.Length; offset++)
                    if (il[offset] is 0x28 or 0x6f)
                    {
                        try { Console.WriteLine($"IL_{offset:x4}: {method.Module.ResolveMethod(BitConverter.ToInt32(il, offset + 1))}"); }
                        catch (ArgumentException) { }
                    }
            }
            return 0;
        }
        if (args.Length > 0 && args[0] == "metadata")
        {
            foreach (var name in args.Skip(1))
            {
                var type = typeof(Mount).Assembly.GetType("Lumina.Excel.Sheets." + name)!;
                Console.WriteLine(name + ": " + string.Join("; ", type.GetProperties().Select(p => p.PropertyType.Name + " " + p.Name)));
            }
            return 0;
        }
        Console.WriteLine("API contract probe compiled; native calls are never executed by this harness.");
        return 0;
    }
}
